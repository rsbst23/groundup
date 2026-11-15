using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for sessions without a current tenant ("tenantless" sessions):
/// a signed-in user holding a GroundUp token with no <c>tid</c> claim.
/// Covers what such a session can call, how it enters a tenant, and that tenant-scoped data rejects it.
/// </summary>
[Collection("AuthFlow")]
public sealed class TenantlessSessionTests : AuthFlowTestBase
{
    public TenantlessSessionTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // GET /auth/me
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_TenantlessSession_ReturnsIdentityWithNullTenantAndNoRoles()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = await SeedUserAsync($"sub-tl-me-{suffix}", $"tl-me-{suffix}@example.com");
        var token = Fixture.GenerateTestToken(userId, email: $"tl-me-{suffix}@example.com");

        // Act
        var response = await Client.SendAsync(CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me", token));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("userId").GetString().Should().Be(userId.ToString());
        json.GetProperty("tenantId").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("roles").GetArrayLength().Should().Be(0);
    }

    // ────────────────────────────────────────────────────────────────────────
    // POST /auth/set-tenant
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetTenant_FromTenantlessSession_IssuesTenantScopedTokenAndPreservesAuthTime()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"TL Enter {suffix}", $"tl-enter-{suffix}");
        var userId = await SeedUserAsync($"sub-tl-enter-{suffix}", $"tl-enter-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        var authTime = DateTimeOffset.UtcNow.AddMinutes(-25).ToUnixTimeSeconds();
        var token = TenantlessToken(userId, authTime);

        // Act
        var response = await Client.SendAsync(SetTenantRequest(token, tenantId));

        // Assert — tenant-scoped GroundUp token, same user, original sign-in time
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var newCookie = ExtractAuthCookie(response);
        newCookie.Should().NotBeNullOrEmpty();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(newCookie);
        jwt.Issuer.Should().Be("GroundUp");
        jwt.Subject.Should().Be(userId.ToString());
        jwt.Claims.First(c => c.Type == "tid").Value.Should().Be(tenantId.ToString());
        jwt.Claims.First(c => c.Type == "auth_time").Value.Should().Be(authTime.ToString());
    }

    [Fact]
    public async Task SetTenant_FromTenantlessSession_NotAMember_Returns403AndWritesNoCookie()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var otherTenantId = await SeedTenantAsync($"TL Other {suffix}", $"tl-other-{suffix}");
        var userId = await SeedUserAsync($"sub-tl-nomem-{suffix}", $"tl-nomem-{suffix}@example.com");

        // Act
        var response = await Client.SendAsync(SetTenantRequest(TenantlessToken(userId), otherTenantId));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ExtractAuthCookie(response).Should().BeNull();
    }

    [Fact]
    public async Task SetTenant_SessionPastAbsoluteLifetime_Returns401AndWritesNoCookie()
    {
        // Arrange — the original sign-in is two days old; choosing a tenant must not restart that clock
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"TL Old {suffix}", $"tl-old-{suffix}");
        var userId = await SeedUserAsync($"sub-tl-old-{suffix}", $"tl-old-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);
        var token = TenantlessToken(userId, DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds());

        // Act
        var response = await Client.SendAsync(SetTenantRequest(token, tenantId));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ExtractAuthCookie(response).Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────────────────
    // POST /auth/organizations, then enter it
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrganization_FromTenantlessSession_ThenSetTenantEntersItAsTenantAdmin()
    {
        // Arrange — a user with no tenants at all
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = await SeedUserAsync($"sub-tl-org-{suffix}", $"tl-org-{suffix}@example.com");
        var token = TenantlessToken(userId);

        // Act 1 — create the organization; the session stays tenantless (no cookie is written)
        var createRequest = CreateBearerRequest(HttpMethod.Post, "/auth/organizations", token);
        createRequest.Content = new StringContent(
            JsonSerializer.Serialize(new { organizationName = $"First Org {suffix}" }), Encoding.UTF8, "application/json");
        var createResponse = await Client.SendAsync(createRequest);

        // Assert 1
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        ExtractAuthCookie(createResponse).Should().BeNull("creating an organization must not change the session");
        var newTenantId = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        // Act 2 — enter the new organization with the same tenantless token
        var enterResponse = await Client.SendAsync(SetTenantRequest(token, newTenantId));

        // Assert 2 — tenant-scoped token for the new organization, with the TenantAdmin role
        enterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(ExtractAuthCookie(enterResponse));
        jwt.Claims.First(c => c.Type == "tid").Value.Should().Be(newTenantId.ToString());
        jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).Should().Contain(AuthRoleNames.TenantAdmin);
    }

    // ────────────────────────────────────────────────────────────────────────
    // POST /auth/logout
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_TenantlessSession_ReturnsLogoutUrlAndClearsCookie()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = await SeedUserAsync($"sub-tl-out-{suffix}", $"tl-out-{suffix}@example.com");

        // Act
        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, "/auth/logout", TenantlessToken(userId)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("logoutUrl").GetString().Should().NotBeNullOrEmpty();
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.StartsWith("AuthToken=", StringComparison.Ordinal),
            "the auth cookie must be cleared on logout");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Tenant-scoped data rejects a tenantless session
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TenantScopedEndpoint_TenantlessSession_Returns403TenantRequired()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = await SeedUserAsync($"sub-tl-403-{suffix}", $"tl-403-{suffix}@example.com");
        var token = TenantlessToken(userId);

        var readRequest = CreateBearerRequest(HttpMethod.Get, "/api/projects", token);
        var writeRequest = CreateBearerRequest(HttpMethod.Post, "/api/projects", token);
        writeRequest.Content = new StringContent(
            JsonSerializer.Serialize(new { name = $"Orphan {suffix}", description = "x" }), Encoding.UTF8, "application/json");

        // Act
        var readResponse = await Client.SendAsync(readRequest);
        var writeResponse = await Client.SendAsync(writeRequest);

        // Assert
        readResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await readResponse.Content.ReadAsStringAsync()).Should().Contain("TENANT_REQUIRED");
        writeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await writeResponse.Content.ReadAsStringAsync()).Should().Contain("TENANT_REQUIRED");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Whole picker round trip: login callback → tenantless cookie → set-tenant
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Picker_CallbackCookieCanBeUsedToSetTenant()
    {
        // Arrange — a user with two standard memberships signs in
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant1Id = await SeedTenantAsync("Round Trip A", $"rt-a-{suffix}");
        var tenant2Id = await SeedTenantAsync("Round Trip B", $"rt-b-{suffix}");
        var externalSub = $"kc-roundtrip-{suffix}";
        var email = $"roundtrip-{suffix}@example.com";
        var userId = await SeedUserAsync(externalSub, email);
        await SeedMembershipAsync(userId, tenant1Id);
        await SeedMembershipAsync(userId, tenant2Id);

        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        SetupFullSuccessfulCallback(externalSub, flowState.Nonce, email);

        var callbackResponse = await Client.SendAsync(CreateCallbackRequest("auth-code-roundtrip", flowState.StateToken));
        callbackResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pickerCookie = ExtractAuthCookie(callbackResponse);
        pickerCookie.Should().NotBeNullOrEmpty();

        // Act — choose a tenant using only the cookie the callback wrote
        var response = await Client.SendAsync(SetTenantRequest(pickerCookie!, tenant2Id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(ExtractAuthCookie(response));
        jwt.Subject.Should().Be(userId.ToString());
        jwt.Claims.First(c => c.Type == "tid").Value.Should().Be(tenant2Id.ToString());
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>A GroundUp token with no tid claim, as a tenantless session holds.</summary>
    private string TenantlessToken(Guid userId, long? authTimeUnix = null) =>
        Fixture.GenerateTestToken(
            userId,
            additionalClaims: new[]
            {
                new Claim("auth_time",
                    (authTimeUnix ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(),
                    ClaimValueTypes.Integer64)
            });

    private HttpRequestMessage SetTenantRequest(string token, Guid tenantId)
    {
        var request = CreateBearerRequest(HttpMethod.Post, "/auth/set-tenant", token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { tenantId }), Encoding.UTF8, "application/json");
        return request;
    }
}
