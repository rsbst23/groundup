using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for the Login flow (FlowType.Login) through the full HTTP pipeline.
/// Validates Requirements 15.3, 15.4, 15.5, 15.6 — single-membership auto-select,
/// multi-membership picker, host-pinned login (member + non-member), enterprise stub,
/// and zero-memberships access denied.
/// </summary>
[Collection("AuthFlow")]
public sealed class LoginFlowTests : AuthFlowTestBase
{
    public LoginFlowTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 1: Single-membership auto-select
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_SingleMembership_RedirectsWithAuthCookieAndCorrectTid()
    {
        // Arrange — seed user with one active membership in a standard tenant
        var tenantId = await SeedTenantAsync("Single Corp", $"single-{Guid.NewGuid():N}");
        var externalSub = $"kc-single-{Guid.NewGuid():N}";
        var userId = await SeedUserAsync(externalSub, "single@example.com");
        await SeedMembershipAsync(userId, tenantId);

        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        SetupFullSuccessfulCallback(externalSub, flowState.Nonce, "single@example.com");

        // Act
        var request = CreateCallbackRequest("auth-code-single", flowState.StateToken);
        var response = await Client.SendAsync(request);

        // Assert — redirect with auth cookie set
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var authCookie = ExtractAuthCookie(response);
        authCookie.Should().NotBeNullOrEmpty("a valid auth cookie should be set on successful login");

        // Validate the JWT token contains the correct tid claim
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(authCookie);
        var tidClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tid");
        tidClaim.Should().NotBeNull("the token must contain a tid claim");
        tidClaim!.Value.Should().Be(tenantId.ToString(), "the tid should match the single tenant");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 2: Multi-membership picker
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_MultipleMemberships_ReturnsTenantListWithoutEnterpriseTenants()
    {
        // Arrange — seed user with 2 standard tenants and 1 enterprise tenant
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant1Id = await SeedTenantAsync("Alpha Corp", $"alpha-{suffix}");
        var tenant2Id = await SeedTenantAsync("Beta Corp", $"beta-{suffix}");
        var enterpriseTenantId = await SeedTenantAsync(
            "Enterprise Inc", $"enterprise-{suffix}",
            tenantType: TenantType.Standard, realmName: "enterprise-realm");

        var externalSub = $"kc-multi-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"multi-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenant1Id);
        await SeedMembershipAsync(userId, tenant2Id);
        await SeedMembershipAsync(userId, enterpriseTenantId);

        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        SetupFullSuccessfulCallback(externalSub, flowState.Nonce, $"multi-{suffix}@example.com");

        // Act
        var request = CreateCallbackRequest("auth-code-multi", flowState.StateToken);
        var response = await Client.SendAsync(request);

        // Assert — HTTP 200 with tenant list
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var tenantsArray = json.RootElement.GetProperty("tenants");
        tenantsArray.ValueKind.Should().Be(JsonValueKind.Array);

        var tenantIds = tenantsArray.EnumerateArray()
            .Select(t => Guid.Parse(t.GetProperty("id").GetString()!))
            .ToList();

        // Standard tenants should be present
        tenantIds.Should().Contain(tenant1Id);
        tenantIds.Should().Contain(tenant2Id);

        // Enterprise tenant must NOT be in the list
        tenantIds.Should().NotContain(enterpriseTenantId,
            "enterprise tenants (RealmName set) must be excluded from the standard picker");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 3: Host-pinned login (member)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_HostPinnedMember_RedirectsWithTokenScopedToHostTenant()
    {
        // Arrange — seed the default-domain setting so host resolution works
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantSlug = $"acme-{suffix}";
        var tenantId = await SeedTenantAsync("Acme Corp", tenantSlug);

        var externalSub = $"kc-host-member-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"host-member-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        var flowState = await CreatePendingFlowStateAsync(FlowType.Login, tenantId: tenantId);
        SetupFullSuccessfulCallback(externalSub, flowState.Nonce, $"host-member-{suffix}@example.com");

        // Act — send callback with Host header matching the tenant slug
        var request = CreateCallbackRequest("auth-code-host-member", flowState.StateToken);
        request.Headers.Host = $"{tenantSlug}.{AuthFlowTestFixture.TestDefaultDomain}";
        var response = await Client.SendAsync(request);

        // Assert — redirect with token scoped to the host tenant (no picker)
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var authCookie = ExtractAuthCookie(response);
        authCookie.Should().NotBeNullOrEmpty("cookie must be set for host-pinned member login");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(authCookie);
        var tidClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tid");
        tidClaim.Should().NotBeNull();
        tidClaim!.Value.Should().Be(tenantId.ToString(),
            "token must be scoped to the host-pinned tenant");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 4: Host-pinned login (non-member)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_HostPinnedNonMember_Returns403Forbidden()
    {
        // Arrange — seed a tenant and a user with NO membership in that tenant
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantSlug = $"locked-{suffix}";
        var tenantId = await SeedTenantAsync("Locked Corp", tenantSlug);

        var externalSub = $"kc-host-nonmember-{suffix}";
        await SeedUserAsync(externalSub, $"host-nonmember-{suffix}@example.com");
        // NO membership created for this user in the host-pinned tenant

        var flowState = await CreatePendingFlowStateAsync(FlowType.Login, tenantId: tenantId);
        SetupFullSuccessfulCallback(externalSub, flowState.Nonce, $"host-nonmember-{suffix}@example.com");

        // Act — callback with Host header pinned to the tenant
        var request = CreateCallbackRequest("auth-code-host-nonmember", flowState.StateToken);
        request.Headers.Host = $"{tenantSlug}.{AuthFlowTestFixture.TestDefaultDomain}";
        var response = await Client.SendAsync(request);

        // Assert — 403 access denied
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 5: Enterprise routing stub
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_EnterpriseHostResolved_Returns501NotImplemented()
    {
        // Arrange — seed an enterprise tenant (RealmName set)
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantSlug = $"ent-{suffix}";
        var tenantId = await SeedTenantAsync(
            "Enterprise Inc", tenantSlug,
            tenantType: TenantType.Standard, realmName: "enterprise-realm");

        var externalSub = $"kc-enterprise-{suffix}";
        await SeedUserAsync(externalSub, $"enterprise-{suffix}@example.com");

        var flowState = await CreatePendingFlowStateAsync(
            FlowType.Login, tenantId: tenantId, realm: "enterprise-realm");
        SetupFullSuccessfulCallback(externalSub, flowState.Nonce, $"enterprise-{suffix}@example.com");

        // Act — callback with Host header matching the enterprise tenant
        var request = CreateCallbackRequest("auth-code-enterprise", flowState.StateToken);
        request.Headers.Host = $"{tenantSlug}.{AuthFlowTestFixture.TestDefaultDomain}";
        var response = await Client.SendAsync(request);

        // Assert — 501 Not Implemented (enterprise flow not yet available)
        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 6: Zero memberships + no default tenant
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_ZeroMembershipsNoDefaultTenant_Returns403Forbidden()
    {
        // Arrange — seed a user with no memberships and no default tenant configured
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var externalSub = $"kc-nomember-{suffix}";
        await SeedUserAsync(externalSub, $"nomember-{suffix}@example.com");

        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        SetupFullSuccessfulCallback(externalSub, flowState.Nonce, $"nomember-{suffix}@example.com");

        // Act
        var request = CreateCallbackRequest("auth-code-nomember", flowState.StateToken);
        var response = await Client.SendAsync(request);

        // Assert — 403 access denied (no memberships, no default tenant)
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

}
