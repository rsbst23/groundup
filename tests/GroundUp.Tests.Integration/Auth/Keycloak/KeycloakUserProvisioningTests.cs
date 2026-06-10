using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Keycloak;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundUp.Tests.Integration.Auth.Keycloak;

/// <summary>
/// Integration tests for user provisioning and role extraction against a real Keycloak container.
/// Validates: Requirements 11.1-11.8, 12.1-12.5, 15.5, 15.6
/// </summary>
[Collection("Keycloak")]
[Trait("Category", "Integration")]
public sealed class KeycloakUserProvisioningTests
{
    private readonly KeycloakFixture _fixture;

    public KeycloakUserProvisioningTests(KeycloakFixture fixture)
    {
        _fixture = fixture;
    }

    private KeycloakIdentityProviderAdminService CreateAdminService()
    {
        var factory = _fixture.CreateHttpClientFactory();
        var optionsMonitor = _fixture.CreateOptionsMonitor();
        var tokenCache = _fixture.CreateAdminTokenCache();
        var logger = NullLogger<KeycloakIdentityProviderAdminService>.Instance;
        return new KeycloakIdentityProviderAdminService(factory, optionsMonitor, tokenCache, logger);
    }

    /// <summary>
    /// User provisioning round-trip: create with password → verify exists → set credentials → delete.
    /// </summary>
    [Fact]
    public async Task UserProvisioning_FullRoundTrip_WorksEndToEnd()
    {
        var service = CreateAdminService();
        var email = $"provision-{Guid.NewGuid():N}@example.com";

        // Create user with initial password
        var provisionRequest = new ProvisionUserRequest(
            Email: email,
            DisplayName: "Test User",
            InitialPassword: "InitialPass123!",
            RequirePasswordReset: false);

        var createResult = await service.ProvisionUserAsync(KeycloakFixture.TestRealmName, provisionRequest);
        createResult.Success.Should().BeTrue($"Provision failed: {createResult.Message}");
        createResult.Data!.ExternalUserId.Should().NotBeNullOrEmpty();
        createResult.Data.Email.Should().Be(email);

        var userId = createResult.Data.ExternalUserId;

        try
        {
            // Verify user exists by getting it via admin API
            var adminToken = await _fixture.GetAdminTokenAsync();
            var userExists = await VerifyUserExists(adminToken, userId);
            userExists.Should().BeTrue("provisioned user should exist in Keycloak");

            // Set new credentials
            var credResult = await service.SetUserCredentialsAsync(
                KeycloakFixture.TestRealmName,
                userId,
                new IdentityProviderUserCredentialsDto("NewPass456!", Temporary: false));
            credResult.Success.Should().BeTrue($"SetCredentials failed: {credResult.Message}");

            // Delete user
            var deleteResult = await service.DeleteUserAsync(KeycloakFixture.TestRealmName, userId);
            deleteResult.Success.Should().BeTrue($"Delete failed: {deleteResult.Message}");

            // Verify gone
            var userGone = await VerifyUserExists(adminToken, userId);
            userGone.Should().BeFalse("deleted user should not exist");
        }
        finally
        {
            // Cleanup attempt
            await service.DeleteUserAsync(KeycloakFixture.TestRealmName, userId);
        }
    }

    /// <summary>
    /// Provisioning a user with RequirePasswordReset but no password sets the UPDATE_PASSWORD action.
    /// </summary>
    [Fact]
    public async Task ProvisionUser_RequirePasswordResetNoPassword_SetsRequiredAction()
    {
        var service = CreateAdminService();
        var email = $"pwreset-{Guid.NewGuid():N}@example.com";

        var provisionRequest = new ProvisionUserRequest(
            Email: email,
            DisplayName: "Reset User",
            InitialPassword: null,
            RequirePasswordReset: true);

        var createResult = await service.ProvisionUserAsync(KeycloakFixture.TestRealmName, provisionRequest);
        createResult.Success.Should().BeTrue($"Provision failed: {createResult.Message}");

        var userId = createResult.Data!.ExternalUserId;

        try
        {
            // Verify the user has UPDATE_PASSWORD required action
            var adminToken = await _fixture.GetAdminTokenAsync();
            var requiredActions = await GetUserRequiredActions(adminToken, userId);
            requiredActions.Should().Contain("UPDATE_PASSWORD");
        }
        finally
        {
            await service.DeleteUserAsync(KeycloakFixture.TestRealmName, userId);
        }
    }

    /// <summary>
    /// Provisioning a duplicate email returns a conflict error.
    /// </summary>
    [Fact]
    public async Task ProvisionUser_DuplicateEmail_ReturnsConflict()
    {
        var service = CreateAdminService();
        var email = $"duplicate-{Guid.NewGuid():N}@example.com";

        var request = new ProvisionUserRequest(email, "First User", "Pass123!", false);

        var first = await service.ProvisionUserAsync(KeycloakFixture.TestRealmName, request);
        first.Success.Should().BeTrue();

        try
        {
            // Try to create the same user again
            var duplicate = await service.ProvisionUserAsync(KeycloakFixture.TestRealmName, request);
            duplicate.Success.Should().BeFalse();
        }
        finally
        {
            await service.DeleteUserAsync(KeycloakFixture.TestRealmName, first.Data!.ExternalUserId);
        }
    }

    /// <summary>
    /// Delete a non-existent user returns not-found.
    /// </summary>
    [Fact]
    public async Task DeleteUser_NonExistent_ReturnsNotFound()
    {
        var service = CreateAdminService();

        var result = await service.DeleteUserAsync(
            KeycloakFixture.TestRealmName,
            Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
    }

    /// <summary>
    /// Role extraction: create user, assign client role, get token, extract roles via ResourceAccessRoleExtractor.
    /// </summary>
    [Fact]
    public async Task RoleExtraction_AssignedClientRole_ExtractsCorrectly()
    {
        var adminToken = await _fixture.GetAdminTokenAsync();
        var email = $"roles-{Guid.NewGuid():N}@example.com";
        var password = "RolesPass123!";

        // Create a test client with a role
        var clientId = $"role-test-{Guid.NewGuid():N}"[..25];
        var roleName = "test-role";

        // Create client
        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        await client.PostAsJsonAsync(
            $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/clients",
            new
            {
                clientId,
                publicClient = false,
                directAccessGrantsEnabled = true,
                serviceAccountsEnabled = false
            });

        // Get internal client ID
        var clientsResponse = await client.GetFromJsonAsync<JsonElement[]>(
            $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/clients?clientId={clientId}");
        var internalClientId = clientsResponse![0].GetProperty("id").GetString()!;

        // Create a client role
        await client.PostAsJsonAsync(
            $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/clients/{internalClientId}/roles",
            new { name = roleName });

        // Create user with password
        await client.PostAsJsonAsync(
            $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/users",
            new
            {
                username = email,
                email,
                emailVerified = true,
                enabled = true,
                credentials = new[] { new { type = "password", value = password, temporary = false } }
            });

        // Get user ID
        var usersResponse = await client.GetFromJsonAsync<JsonElement[]>(
            $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/users?email={email}");
        var userId = usersResponse![0].GetProperty("id").GetString()!;

        // Get the role representation
        var rolesResponse = await client.GetFromJsonAsync<JsonElement[]>(
            $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/clients/{internalClientId}/roles");
        var roleRepresentation = rolesResponse![0];

        // Assign the role to the user
        var roleAssignBody = new[] { new { id = roleRepresentation.GetProperty("id").GetString(), name = roleName } };
        await client.PostAsJsonAsync(
            $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/users/{userId}/role-mappings/clients/{internalClientId}",
            roleAssignBody);

        try
        {
            // Get a token for this user via direct access grant
            // Need to enable direct access grants on the groundup-app client first
            var appClientsResponse = await client.GetFromJsonAsync<JsonElement[]>(
                $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/clients?clientId={KeycloakFixture.AppClientId}");
            var appInternalId = appClientsResponse![0].GetProperty("id").GetString()!;
            await client.PutAsJsonAsync(
                $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/clients/{appInternalId}",
                new { clientId = KeycloakFixture.AppClientId, directAccessGrantsEnabled = true, publicClient = true });

            // Get token
            using var tokenClient = _fixture.CreateHttpClient();
            var tokenResponse = await tokenClient.PostAsync(
                $"{_fixture.BaseUrl}/realms/{KeycloakFixture.TestRealmName}/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = KeycloakFixture.AppClientId,
                    ["username"] = email,
                    ["password"] = password
                }));
            tokenResponse.EnsureSuccessStatusCode();
            var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
            var accessToken = tokenJson.GetProperty("access_token").GetString()!;

            // Extract roles
            var extractedRoles = ResourceAccessRoleExtractor.ExtractRoles(accessToken, clientId);

            // Assert
            extractedRoles.Should().Contain(roleName);
        }
        finally
        {
            // Cleanup
            await client.DeleteAsync($"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/users/{userId}");
            await client.DeleteAsync($"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/clients/{internalClientId}");
        }
    }

    private async Task<bool> VerifyUserExists(string adminToken, string userId)
    {
        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync(
            $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/users/{userId}");

        return response.IsSuccessStatusCode;
    }

    private async Task<List<string>> GetUserRequiredActions(string adminToken, string userId)
    {
        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync(
            $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/users/{userId}");

        if (!response.IsSuccessStatusCode)
            return new List<string>();

        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (user.TryGetProperty("requiredActions", out var actions) && actions.ValueKind == JsonValueKind.Array)
        {
            return actions.EnumerateArray()
                .Select(a => a.GetString()!)
                .ToList();
        }

        return new List<string>();
    }
}
