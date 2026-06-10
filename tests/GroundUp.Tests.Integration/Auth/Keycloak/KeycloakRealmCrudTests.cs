using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Keycloak;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundUp.Tests.Integration.Auth.Keycloak;

/// <summary>
/// Integration tests for realm and client CRUD operations against a real Keycloak container.
/// Validates: Requirements 9.1-9.7, 10.1-10.7, 15.3, 15.4
/// </summary>
[Collection("Keycloak")]
[Trait("Category", "Integration")]
public sealed class KeycloakRealmCrudTests
{
    private readonly KeycloakFixture _fixture;

    public KeycloakRealmCrudTests(KeycloakFixture fixture)
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
    /// Full realm CRUD round-trip: create → get → update → verify → delete → verify gone.
    /// </summary>
    [Fact]
    public async Task Realm_CrudRoundTrip_WorksEndToEnd()
    {
        var service = CreateAdminService();
        var realmName = $"test-realm-{Guid.NewGuid():N}"[..30]; // Keycloak has max realm name length

        try
        {
            // Create
            var createResult = await service.CreateRealmAsync(new CreateRealmRequest(realmName, "Test Realm"));
            createResult.Success.Should().BeTrue($"Create failed: {createResult.Message}");
            createResult.Data!.RealmName.Should().Be(realmName);
            createResult.Data.DisplayName.Should().Be("Test Realm");
            createResult.Data.Enabled.Should().BeTrue();

            // Get
            var getResult = await service.GetRealmAsync(realmName);
            getResult.Success.Should().BeTrue($"Get failed: {getResult.Message}");
            getResult.Data!.RealmName.Should().Be(realmName);

            // Update
            var updateResult = await service.UpdateRealmAsync(realmName, new UpdateRealmRequest("Updated Realm", null));
            updateResult.Success.Should().BeTrue($"Update failed: {updateResult.Message}");
            updateResult.Data!.DisplayName.Should().Be("Updated Realm");

            // Verify update persisted
            var verifyResult = await service.GetRealmAsync(realmName);
            verifyResult.Success.Should().BeTrue();
            verifyResult.Data!.DisplayName.Should().Be("Updated Realm");

            // Delete
            var deleteResult = await service.DeleteRealmAsync(realmName);
            deleteResult.Success.Should().BeTrue($"Delete failed: {deleteResult.Message}");

            // Verify gone
            var goneResult = await service.GetRealmAsync(realmName);
            goneResult.Success.Should().BeFalse();
        }
        finally
        {
            // Cleanup — attempt to delete in case test failed mid-way
            await service.DeleteRealmAsync(realmName);
        }
    }

    /// <summary>
    /// GetRealmAsync returns not-found for a non-existent realm.
    /// </summary>
    [Fact]
    public async Task GetRealm_NonExistent_ReturnsNotFound()
    {
        var service = CreateAdminService();

        var result = await service.GetRealmAsync("definitely-does-not-exist-realm");

        result.Success.Should().BeFalse();
    }

    /// <summary>
    /// Full client CRUD round-trip: create → get → verify settings → update → delete.
    /// </summary>
    [Fact]
    public async Task Client_CrudRoundTrip_WorksEndToEnd()
    {
        var service = CreateAdminService();
        var clientId = $"test-client-{Guid.NewGuid():N}"[..30];

        // Create
        var createRequest = new CreateIdentityProviderClientRequest(
            clientId,
            new List<string> { "http://localhost:3000/callback" },
            RequiresPkce: true);

        var createResult = await service.CreateClientAsync(KeycloakFixture.TestRealmName, createRequest);
        createResult.Success.Should().BeTrue($"Create failed: {createResult.Message}");
        createResult.Data!.ClientId.Should().Be(clientId);
        createResult.Data.RequiresPkce.Should().BeTrue();
        createResult.Data.RedirectUris.Should().Contain("http://localhost:3000/callback");
        createResult.Data.ClientSecret.Should().NotBeNullOrEmpty("confidential client should have a secret");

        // Get
        var getResult = await service.GetClientAsync(KeycloakFixture.TestRealmName, clientId);
        getResult.Success.Should().BeTrue($"Get failed: {getResult.Message}");
        getResult.Data!.ClientId.Should().Be(clientId);

        // Update — change redirect URIs and disable PKCE
        var updateRequest = new UpdateIdentityProviderClientRequest(
            new List<string> { "http://localhost:4000/callback", "http://localhost:5000/callback" },
            RequiresPkce: false);

        var updateResult = await service.UpdateClientAsync(KeycloakFixture.TestRealmName, clientId, updateRequest);
        updateResult.Success.Should().BeTrue($"Update failed: {updateResult.Message}");
        updateResult.Data!.RedirectUris.Should().HaveCount(2);
        updateResult.Data.RequiresPkce.Should().BeFalse();

        // Delete
        var deleteResult = await service.DeleteClientAsync(KeycloakFixture.TestRealmName, clientId);
        deleteResult.Success.Should().BeTrue($"Delete failed: {deleteResult.Message}");

        // Verify gone
        var goneResult = await service.GetClientAsync(KeycloakFixture.TestRealmName, clientId);
        goneResult.Success.Should().BeFalse();
    }

    /// <summary>
    /// Creating a duplicate realm returns a conflict error.
    /// </summary>
    [Fact]
    public async Task CreateRealm_Duplicate_ReturnsConflict()
    {
        var service = CreateAdminService();
        var realmName = $"dup-realm-{Guid.NewGuid():N}"[..25];

        try
        {
            // Create first
            var first = await service.CreateRealmAsync(new CreateRealmRequest(realmName, "First"));
            first.Success.Should().BeTrue();

            // Create duplicate
            var duplicate = await service.CreateRealmAsync(new CreateRealmRequest(realmName, "Duplicate"));
            duplicate.Success.Should().BeFalse();
        }
        finally
        {
            await service.DeleteRealmAsync(realmName);
        }
    }
}
