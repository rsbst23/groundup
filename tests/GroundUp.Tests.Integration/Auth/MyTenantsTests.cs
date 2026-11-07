using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for <c>GET /auth/me/tenants</c>.
/// Validates Requirements 4.1, 4.2 — returns the authenticated user's tenant memberships
/// with correct <c>switchable</c>, <c>current</c>, and <c>type</c> flags.
/// </summary>
[Collection("AuthFlow")]
public sealed class MyTenantsTests : AuthFlowTestBase
{
    public MyTenantsTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // 1. Unauthenticated → 401
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyTenants_Unauthenticated_Returns401()
    {
        // Arrange — no auth cookie
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me/tenants");

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. Single standard tenant — returns list with correct fields
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyTenants_SingleStandardTenant_ReturnsListWithCorrectFields()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"My Corp {suffix}", $"my-corp-{suffix}");
        var userId = await SeedUserAsync($"sub-{suffix}", $"user-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        var token = Fixture.GenerateTestToken(userId, tenantId);
        var request = CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me/tenants", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 with tenant list
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        var tenants = json.RootElement.EnumerateArray().ToList();
        tenants.Should().ContainSingle("user has exactly one active membership");

        var tenant = tenants[0];
        tenant.GetProperty("id").GetString().Should().Be(tenantId.ToString());
        tenant.GetProperty("type").GetString().Should().Be("standard");
        tenant.GetProperty("switchable").GetBoolean().Should().BeTrue();
        tenant.GetProperty("current").GetBoolean().Should().BeTrue("the user's token is scoped to this tenant");
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3. Multiple standard tenants — current flag only on active tenant
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyTenants_MultipleTenants_OnlyCurrentTenantHasCurrentFlag()
    {
        // Arrange — two tenants; user is viewing tenant1 (tid in token)
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant1Id = await SeedTenantAsync($"Alpha {suffix}", $"alpha-{suffix}");
        var tenant2Id = await SeedTenantAsync($"Beta {suffix}", $"beta-{suffix}");
        var userId = await SeedUserAsync($"sub-multi-{suffix}", $"multi-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenant1Id);
        await SeedMembershipAsync(userId, tenant2Id);

        // Token scoped to tenant1
        var token = Fixture.GenerateTestToken(userId, tenant1Id);
        var request = CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me/tenants", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 with two tenants
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var tenants = json.RootElement.EnumerateArray().ToList();
        tenants.Should().HaveCount(2);

        // Find each tenant in the result
        var t1 = tenants.FirstOrDefault(t => t.GetProperty("id").GetString() == tenant1Id.ToString());
        var t2 = tenants.FirstOrDefault(t => t.GetProperty("id").GetString() == tenant2Id.ToString());

        t1.ValueKind.Should().NotBe(JsonValueKind.Undefined, "tenant1 must appear in the list");
        t2.ValueKind.Should().NotBe(JsonValueKind.Undefined, "tenant2 must appear in the list");

        t1.GetProperty("current").GetBoolean().Should().BeTrue("tenant1 is the active tid in the token");
        t2.GetProperty("current").GetBoolean().Should().BeFalse("tenant2 is not the active tid");

        t1.GetProperty("switchable").GetBoolean().Should().BeTrue();
        t2.GetProperty("switchable").GetBoolean().Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 4. Enterprise tenant — switchable: false, no current flag
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyTenants_EnterpriseTenant_MarkedSwitchableFalse()
    {
        // Arrange — one standard + one enterprise tenant
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var standardId = await SeedTenantAsync($"Standard {suffix}", $"std-{suffix}");
        var enterpriseId = await SeedTenantAsync(
            $"Enterprise {suffix}", $"ent-{suffix}",
            tenantType: TenantType.Standard, realmName: "ent-realm");
        var userId = await SeedUserAsync($"sub-ent-{suffix}", $"ent-{suffix}@example.com");
        await SeedMembershipAsync(userId, standardId);
        await SeedMembershipAsync(userId, enterpriseId);

        // Token scoped to the standard tenant
        var token = Fixture.GenerateTestToken(userId, standardId);
        var request = CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me/tenants", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var tenants = json.RootElement.EnumerateArray().ToList();
        tenants.Should().HaveCount(2);

        var entTenant = tenants.First(t => t.GetProperty("id").GetString() == enterpriseId.ToString());
        entTenant.GetProperty("type").GetString().Should().Be("enterprise");
        entTenant.GetProperty("switchable").GetBoolean().Should().BeFalse(
            "enterprise tenants require re-authentication via their subdomain");
        entTenant.TryGetProperty("subdomain", out var subdomainEl).Should().BeTrue();
        subdomainEl.GetString().Should().NotBeNullOrEmpty("enterprise tenants should expose their subdomain slug");

        var stdTenant = tenants.First(t => t.GetProperty("id").GetString() == standardId.ToString());
        stdTenant.GetProperty("type").GetString().Should().Be("standard");
        stdTenant.GetProperty("switchable").GetBoolean().Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 5. Inactive membership is excluded
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyTenants_InactiveMembership_IsExcludedFromList()
    {
        // Arrange — one active + one inactive membership
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var activeId = await SeedTenantAsync($"Active {suffix}", $"active-{suffix}");
        var inactiveId = await SeedTenantAsync($"Inactive {suffix}", $"inactive-{suffix}");
        var userId = await SeedUserAsync($"sub-inactive-{suffix}", $"inactive-{suffix}@example.com");
        await SeedMembershipAsync(userId, activeId, isActive: true);
        await SeedMembershipAsync(userId, inactiveId, isActive: false); // Inactive — must not appear

        var token = Fixture.GenerateTestToken(userId, activeId);
        var request = CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me/tenants", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — only the active membership is returned
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var tenants = json.RootElement.EnumerateArray().ToList();
        tenants.Should().ContainSingle("only the active membership should be returned");
        tenants[0].GetProperty("id").GetString().Should().Be(activeId.ToString());
    }

    // ────────────────────────────────────────────────────────────────────────
    // 6. No memberships → empty array
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyTenants_NoMemberships_ReturnsEmptyArray()
    {
        // Arrange — user with no memberships (token has no tid — pending selection)
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = await SeedUserAsync($"sub-nomem-{suffix}", $"nomem-{suffix}@example.com");

        // Token with userId but no tenantId (pending selection)
        var token = Fixture.GenerateTestToken(userId);
        var request = CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me/tenants", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 with empty array (not a 404 or error)
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        json.RootElement.EnumerateArray().Should().BeEmpty();
    }
}
