using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for host/token reconciliation middleware behavior.
/// Validates Requirements 15.16, 15.17, 15.18 — standard tenant mismatch (409 switch required),
/// enterprise tenant mismatch (401 re-auth required), non-member mismatch (403 forbidden),
/// and /auth/* path exemption from reconciliation denial.
/// </summary>
[Collection("AuthFlow")]
public sealed class HostTokenReconciliationIntegrationTests : AuthFlowTestBase
{
    public HostTokenReconciliationIntegrationTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Generates a valid GroundUp JWT token with the required <c>kid</c> header for
    /// <c>TokenService.ValidateTokenAsync</c> to accept it.
    /// </summary>
    private static string GenerateValidToken(Guid userId, Guid tenantId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthFlowTestFixture.TestSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("tid", tenantId.ToString()),
            new("email", "test@example.com"),
            new("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = "GroundUp",
            Audience = "GroundUp",
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(60),
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);
        token.Header["kid"] = "default";

        return tokenHandler.WriteToken(token);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 1: Host matches token tenant → request proceeds normally
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_HostMatchesTokenTenant_ProceedsNormally()
    {
        // Arrange — seed a tenant with slug matching the Host subdomain, user with membership
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"match-{suffix}";
        var tenantId = await SeedTenantAsync("Match Corp", slug);

        var externalSub = $"kc-match-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"match-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        // Generate a GroundUp token scoped to this tenant
        var token = GenerateValidToken(userId, tenantId);

        // Act — request with Host = slug.testapp.local (matches token tid)
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/data");
        request.Headers.Host = $"{slug}.{AuthFlowTestFixture.TestDefaultDomain}";
        request.Headers.Add("Cookie", $"AuthToken={token}");
        var response = await Client.SendAsync(request);

        // Assert — should NOT be blocked by reconciliation (404 is fine — endpoint doesn't exist)
        response.StatusCode.Should().NotBe(HttpStatusCode.Conflict,
            "matching host/token tenants should not trigger reconciliation denial");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "matching host/token tenants should not require re-auth");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "matching host/token tenants should not deny access");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 2: No host tenant resolved → request proceeds normally
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_NoHostTenantResolved_ProceedsNormally()
    {
        // Arrange — user with a token, but Host does not resolve to any tenant
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync("NoHost Corp", $"nohost-{suffix}");
        var externalSub = $"kc-nohost-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"nohost-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        var token = GenerateValidToken(userId, tenantId);

        // Act — request with bare domain (no subdomain → no host resolution)
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/data");
        request.Headers.Host = AuthFlowTestFixture.TestDefaultDomain;
        request.Headers.Add("Cookie", $"AuthToken={token}");
        var response = await Client.SendAsync(request);

        // Assert — no reconciliation should fire
        response.StatusCode.Should().NotBe(HttpStatusCode.Conflict);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 3: Unauthenticated request → proceeds normally regardless of host
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_Unauthenticated_ProceedsNormally()
    {
        // Arrange — seed a tenant so host resolution succeeds, but send no auth cookie
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"unauth-{suffix}";
        await SeedTenantAsync("Unauth Corp", slug);

        // Act — request with Host subdomain but no auth cookie
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/data");
        request.Headers.Host = $"{slug}.{AuthFlowTestFixture.TestDefaultDomain}";
        var response = await Client.SendAsync(request);

        // Assert — reconciliation middleware should not fire for unauthenticated requests
        response.StatusCode.Should().NotBe(HttpStatusCode.Conflict);
        // 401 here might come from auth requirements downstream, not from reconciliation
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 4: Standard tenant mismatch — user IS a member → 409 TENANT_SWITCH_REQUIRED
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StandardMismatch_UserIsMember_Returns409TenantSwitchRequired()
    {
        // Arrange — user has membership in both tenants, token scoped to tenant A,
        // host resolves to tenant B (standard, RealmName null)
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slugA = $"std-a-{suffix}";
        var slugB = $"std-b-{suffix}";
        var tenantAId = await SeedTenantAsync("Standard A", slugA);
        var tenantBId = await SeedTenantAsync("Standard B", slugB);

        var externalSub = $"kc-std-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"std-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantAId);
        await SeedMembershipAsync(userId, tenantBId);

        // Token is scoped to tenant A
        var token = GenerateValidToken(userId, tenantAId);

        // Act — request with Host pointing to tenant B (standard tenant, RealmName null)
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/data");
        request.Headers.Host = $"{slugB}.{AuthFlowTestFixture.TestDefaultDomain}";
        request.Headers.Add("Cookie", $"AuthToken={token}");
        var response = await Client.SendAsync(request);

        // Assert — 409 Conflict with TENANT_SWITCH_REQUIRED
        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "standard tenant mismatch with membership should return 409 TENANT_SWITCH_REQUIRED");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("TENANT_SWITCH_REQUIRED");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 5: Enterprise tenant mismatch → 401 REAUTH_REQUIRED
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnterpriseMismatch_Returns401ReauthRequired()
    {
        // Arrange — host resolves to an enterprise tenant (RealmName set), token scoped elsewhere
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var standardSlug = $"ent-std-{suffix}";
        var enterpriseSlug = $"ent-ent-{suffix}";
        var standardTenantId = await SeedTenantAsync("Std Tenant", standardSlug);
        var enterpriseTenantId = await SeedTenantAsync(
            "Enterprise Tenant", enterpriseSlug, realmName: $"enterprise-realm-{suffix}");

        var externalSub = $"kc-ent-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"ent-{suffix}@example.com");
        await SeedMembershipAsync(userId, standardTenantId);
        await SeedMembershipAsync(userId, enterpriseTenantId);

        // Token scoped to the standard tenant
        var token = GenerateValidToken(userId, standardTenantId);

        // Act — Host resolves to the enterprise tenant
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/data");
        request.Headers.Host = $"{enterpriseSlug}.{AuthFlowTestFixture.TestDefaultDomain}";
        request.Headers.Add("Cookie", $"AuthToken={token}");
        var response = await Client.SendAsync(request);

        // Assert — 401 Unauthorized with REAUTH_REQUIRED
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "enterprise tenant mismatch should return 401 requiring fresh login to the enterprise realm");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("REAUTH_REQUIRED");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 6: Non-member mismatch → 403 Forbidden
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NonMemberMismatch_Returns403Forbidden()
    {
        // Arrange — user has a token for tenant A, host resolves to tenant B,
        // but user is NOT a member of tenant B
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slugA = $"nm-a-{suffix}";
        var slugB = $"nm-b-{suffix}";
        var tenantAId = await SeedTenantAsync("NonMem A", slugA);
        await SeedTenantAsync("NonMem B", slugB);

        var externalSub = $"kc-nm-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"nm-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantAId);
        // Deliberately NOT adding membership in tenant B

        var token = GenerateValidToken(userId, tenantAId);

        // Act — Host resolves to tenant B where user has no membership
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/data");
        request.Headers.Host = $"{slugB}.{AuthFlowTestFixture.TestDefaultDomain}";
        request.Headers.Add("Cookie", $"AuthToken={token}");
        var response = await Client.SendAsync(request);

        // Assert — 403 Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "mismatch where user is not a member of the host tenant should return 403 Forbidden");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("FORBIDDEN");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 7: /auth/* paths are exempt from reconciliation denial
    // ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/auth/me")]
    [InlineData("/auth/set-tenant")]
    [InlineData("/auth/login")]
    [InlineData("/auth/callback")]
    [InlineData("/auth/refresh")]
    [InlineData("/auth/logout")]
    public async Task AuthPaths_AreExemptFromReconciliation_DespiteMismatch(string path)
    {
        // Arrange — create a mismatch scenario (user token for tenant A, host → tenant B with membership)
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slugA = $"ex-a-{suffix}";
        var slugB = $"ex-b-{suffix}";
        var tenantAId = await SeedTenantAsync("Exempt A", slugA);
        var tenantBId = await SeedTenantAsync("Exempt B", slugB);

        var externalSub = $"kc-ex-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"ex-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantAId);
        await SeedMembershipAsync(userId, tenantBId);

        var token = GenerateValidToken(userId, tenantAId);

        // Act — request to an /auth/* path with mismatch
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = $"{slugB}.{AuthFlowTestFixture.TestDefaultDomain}";
        request.Headers.Add("Cookie", $"AuthToken={token}");
        var response = await Client.SendAsync(request);

        // Assert — should NOT be 409/401 from reconciliation
        // The endpoint may return other status codes (405, 401 for CSRF, etc.)
        // but NOT the reconciliation denial codes
        response.StatusCode.Should().NotBe(HttpStatusCode.Conflict,
            $"path '{path}' should be exempt from reconciliation — should not return 409");

        // For enterprise mismatch scenario too — let's verify /auth/* is exempt even then
        // (the reconciliation middleware exempts the path before checking the mismatch type)
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 8: /auth/* exemption also applies for enterprise tenant mismatch
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthPath_ExemptFromReconciliation_EvenForEnterpriseMismatch()
    {
        // Arrange — enterprise tenant mismatch but requesting /auth/me
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var standardSlug = $"exent-std-{suffix}";
        var enterpriseSlug = $"exent-ent-{suffix}";
        var standardTenantId = await SeedTenantAsync("ExEnt Std", standardSlug);
        await SeedTenantAsync("ExEnt Enterprise", enterpriseSlug, realmName: $"realm-{suffix}");

        var externalSub = $"kc-exent-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"exent-{suffix}@example.com");
        await SeedMembershipAsync(userId, standardTenantId);

        var token = GenerateValidToken(userId, standardTenantId);

        // Act — /auth/me with enterprise host mismatch
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Host = $"{enterpriseSlug}.{AuthFlowTestFixture.TestDefaultDomain}";
        request.Headers.Add("Cookie", $"AuthToken={token}");
        var response = await Client.SendAsync(request);

        // Assert — should NOT be 401 from reconciliation (it's an auth path)
        // auth/me should proceed and return user info or a different status, not reconciliation denial
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "/auth/me should be exempt from reconciliation even for enterprise tenant mismatch");
        response.StatusCode.Should().NotBe(HttpStatusCode.Conflict);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 9: Standard mismatch — user can switch via set-tenant without re-auth
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StandardMismatch_UserCanSwitchViaSetTenant_WithoutReAuth()
    {
        // Arrange — user has membership in both standard tenants, token scoped to tenant A
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slugA = $"sw-a-{suffix}";
        var slugB = $"sw-b-{suffix}";
        var tenantAId = await SeedTenantAsync("Switch A", slugA);
        var tenantBId = await SeedTenantAsync("Switch B", slugB);

        var externalSub = $"kc-sw-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"sw-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantAId);
        await SeedMembershipAsync(userId, tenantBId);

        var token = GenerateValidToken(userId, tenantAId);

        // Act — call POST /auth/set-tenant (an /auth/* exempt path) to switch to tenant B
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/set-tenant");
        request.Headers.Host = $"{slugB}.{AuthFlowTestFixture.TestDefaultDomain}";
        request.Headers.Add("Cookie", $"AuthToken={token}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { tenantId = tenantBId }),
            System.Text.Encoding.UTF8,
            "application/json");
        var response = await Client.SendAsync(request);

        // Assert — set-tenant should succeed (200 OK) without requiring re-auth
        // The /auth/set-tenant path is exempt from reconciliation, so the request reaches
        // the controller which issues a new token scoped to tenant B
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "user should be able to switch tenants via set-tenant without re-authentication for standard tenants");

        // Verify a new auth cookie was issued (scoped to tenant B)
        var newAuthCookie = ExtractAuthCookie(response);
        newAuthCookie.Should().NotBeNullOrEmpty(
            "set-tenant should issue a new token cookie scoped to the selected tenant");
    }
}
