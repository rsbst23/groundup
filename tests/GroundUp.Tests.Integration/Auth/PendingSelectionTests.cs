using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using NSubstitute;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for the pending-selection flow and POST /auth/set-tenant.
/// Validates Requirement 15.15 — retained Keycloak token during tenant selection
/// (no GroundUp token, no tid), GET /auth/me succeeds with null tenant, and
/// POST /auth/set-tenant resolves user by external sub and issues a full GroundUp token.
/// </summary>
[Collection("AuthFlow")]
public sealed class PendingSelectionTests : AuthFlowTestBase
{
    public PendingSelectionTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 1: Multi-membership returns Keycloak token in cookie (not GroundUp JWT)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_MultiMembership_RetainsKeycloakTokenInCookie_NotGroundUpJwt()
    {
        // Arrange — seed a user with 2+ memberships in standard tenants
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant1Id = await SeedTenantAsync("Picker Corp A", $"picker-a-{suffix}");
        var tenant2Id = await SeedTenantAsync("Picker Corp B", $"picker-b-{suffix}");

        var externalSub = $"kc-pending-{suffix}";
        var email = $"pending-{suffix}@example.com";
        var userId = await SeedUserAsync(externalSub, email);
        await SeedMembershipAsync(userId, tenant1Id);
        await SeedMembershipAsync(userId, tenant2Id);

        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        SetupFullSuccessfulCallback(externalSub, flowState.Nonce, email);

        // Act — trigger the login callback
        var request = CreateCallbackRequest("auth-code-pending", flowState.StateToken);
        var response = await Client.SendAsync(request);

        // Assert — 200 JSON with tenant list (picker)
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("tenants", out var tenantsEl).Should().BeTrue();
        tenantsEl.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

        // The auth cookie should contain the Keycloak access token (NOT a GroundUp JWT)
        var authCookie = ExtractAuthCookie(response);
        authCookie.Should().NotBeNullOrEmpty(
            "an auth cookie must be set during pending-selection to keep the user authenticated");

        // Verify it is NOT a valid GroundUp JWT:
        // The LoginFlowHandler writes the Keycloak access token (opaque) as the cookie.
        // A GroundUp JWT would be in JWS compact serialization (3 dot-separated parts with
        // "GroundUp" issuer). The Keycloak access token is NOT in that format.
        var handler = new JwtSecurityTokenHandler();
        var isValidJwt = handler.CanReadToken(authCookie);

        if (isValidJwt)
        {
            // If the token happens to be JWT-formatted (e.g. if implementation uses id_token),
            // verify it does NOT have a tid claim (not a GroundUp token)
            var jwt = handler.ReadJwtToken(authCookie);
            var tidClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tid");
            tidClaim.Should().BeNull(
                "the retained Keycloak token should NOT contain a tid claim — it is not a GroundUp JWT");
            jwt.Issuer.Should().NotBe("GroundUp",
                "the retained token should be the Keycloak token, not a GroundUp-issued JWT");
        }
        else
        {
            // Opaque token (not JWT format) — this is expected for the Keycloak access token.
            // It definitely is NOT a GroundUp JWT (which is always in JWS format).
            authCookie.Should().StartWith("fake-access-token-",
                "the LoginFlowHandler should retain the Keycloak access token as-is");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 2: GET /auth/me with pending-selection principal
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_WithPendingSelectionKeycloakToken_Returns200WithNullTenant()
    {
        // Arrange — create a fake Keycloak token (pending-selection scenario)
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var externalSub = $"kc-me-pending-{suffix}";
        var email = $"me-pending-{suffix}@example.com";
        var displayName = "Pending User";

        // Seed a user so the system recognizes the sub
        await SeedUserAsync(externalSub, email, displayName);

        // Generate a fake Keycloak id_token (signed with different key than GroundUp)
        var nonce = AuthFlowTestFixture.GenerateNonce();
        var keycloakToken = AuthFlowTestFixture.GenerateFakeIdToken(externalSub, nonce, email);

        // Configure the mock IDP to validate this Keycloak token via the fallback path
        Fixture.MockIdentityProviderService
            .ValidateTokenAsync(keycloakToken)
            .Returns(true);

        Fixture.MockIdentityProviderService
            .GetUserInfoAsync(keycloakToken)
            .Returns(new ExternalUserInfo(externalSub, email, displayName, null));

        // Act — send GET /auth/me with the Keycloak token as the auth cookie
        var request = CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me", keycloakToken);
        var response = await Client.SendAsync(request);

        // Assert — 200 OK with identity but no tenantId
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Should have user identity
        json.RootElement.GetProperty("userId").GetString().Should().Be(externalSub);
        json.RootElement.GetProperty("email").GetString().Should().Be(email);
        json.RootElement.GetProperty("displayName").GetString().Should().Be(displayName);

        // tenantId must be null (pending-selection: no tenant selected yet)
        json.RootElement.GetProperty("tenantId").ValueKind.Should().Be(JsonValueKind.Null,
            "pending-selection principal has no tid claim, so tenantId must be null");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 3: POST /auth/set-tenant resolves user by sub and issues full token
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetTenant_WithKeycloakToken_ResolvesUserBySubAndIssuesFullGroundUpJwt()
    {
        // Arrange — seed user with memberships
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync("SetTenant Corp", $"set-tenant-{suffix}");

        var externalSub = $"kc-set-tenant-{suffix}";
        var email = $"set-tenant-{suffix}@example.com";
        var userId = await SeedUserAsync(externalSub, email, "Set Tenant User");
        await SeedMembershipAsync(userId, tenantId);

        // Generate a fake Keycloak token (pending-selection — no tid)
        var nonce = AuthFlowTestFixture.GenerateNonce();
        var keycloakToken = AuthFlowTestFixture.GenerateFakeIdToken(externalSub, nonce, email);

        // Configure the mock IDP to validate this Keycloak token via the fallback path
        Fixture.MockIdentityProviderService
            .ValidateTokenAsync(keycloakToken)
            .Returns(true);

        Fixture.MockIdentityProviderService
            .GetUserInfoAsync(keycloakToken)
            .Returns(new ExternalUserInfo(externalSub, email, "Set Tenant User", null));

        // Act — POST /auth/set-tenant with the Keycloak token and the target tenantId
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/auth/set-tenant", keycloakToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { tenantId }),
            Encoding.UTF8,
            "application/json");
        var response = await Client.SendAsync(request);

        // Assert — 200 OK with a new auth cookie containing a full GroundUp JWT
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var newAuthCookie = ExtractAuthCookie(response);
        newAuthCookie.Should().NotBeNullOrEmpty(
            "set-tenant should issue a full GroundUp JWT and write it as the auth cookie");

        // Validate the new token IS a valid GroundUp JWT
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(newAuthCookie);

        // The issuer should be "GroundUp" (our token issuer)
        jwt.Issuer.Should().Be("GroundUp",
            "the new token must be a GroundUp-issued JWT, not a Keycloak token");

        // The tid claim must match the selected tenant
        var tidClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tid");
        tidClaim.Should().NotBeNull("the full GroundUp JWT must contain a tid claim");
        tidClaim!.Value.Should().Be(tenantId.ToString(),
            "the tid claim must match the tenant selected via set-tenant");

        // The sub claim must be the GroundUp user ID (not the external sub)
        var subClaim = jwt.Claims.FirstOrDefault(c => c.Type == "sub");
        subClaim.Should().NotBeNull("the GroundUp JWT must contain a sub claim");
        subClaim!.Value.Should().Be(userId.ToString(),
            "the sub claim should be the GroundUp user ID resolved from the external sub");

        // The auth_time claim should be set to approximately now (first issuance)
        var authTimeClaim = jwt.Claims.FirstOrDefault(c => c.Type == "auth_time");
        authTimeClaim.Should().NotBeNull("the first GroundUp token must include auth_time");
        var authTimeUnix = long.Parse(authTimeClaim!.Value);
        var authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeUnix);
        authTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30),
            "auth_time should be approximately now for first token issuance");
    }
}
