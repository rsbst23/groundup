using GroundUp.Auth.Core.Enums;
using System.Net;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Token revocation when a membership ends (GU-71), over HTTP. <c>GET /auth/me</c> only needs an
/// authenticated principal and does not re-check membership, so a 401 there after a leave shows the
/// token itself is no longer honored.
/// </summary>
[Collection("AuthFlow")]
public sealed class TokenRevocationIntegrationTests : AuthFlowTestBase
{
    public TokenRevocationIntegrationTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task BearerTokenHeldBeforeLeave_IsRejectedAfterwards()
    {
        var (userId, tenantId, _) = await SeedUserInTenantsAsync("bearer");
        var savedToken = Fixture.GenerateTestToken(userId, tenantId);
        (await MeAsync(savedToken)).Should().Be(HttpStatusCode.OK);

        (await LeaveAsync(savedToken, tenantId)).Should().Be(HttpStatusCode.OK);

        (await MeAsync(savedToken)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CookieTokenHeldBeforeLeave_IsRejectedAfterwards()
    {
        var (userId, tenantId, _) = await SeedUserInTenantsAsync("cookie");
        var savedToken = Fixture.GenerateTestToken(userId, tenantId);

        (await LeaveAsync(savedToken, tenantId)).Should().Be(HttpStatusCode.OK);

        var response = await Client.SendAsync(CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me", savedToken));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LeavingOneTenant_TokenForTheOtherTenantKeepsWorking()
    {
        var (userId, tenantA, tenantB) = await SeedUserInTenantsAsync("other", secondTenant: true);
        var tokenA = Fixture.GenerateTestToken(userId, tenantA);
        var tokenB = Fixture.GenerateTestToken(userId, tenantB!.Value);

        // Leave A while working in B
        (await LeaveAsync(tokenB, tenantA)).Should().Be(HttpStatusCode.OK);

        (await MeAsync(tokenA)).Should().Be(HttpStatusCode.Unauthorized);
        (await MeAsync(tokenB)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LeaveBlockedForLastAdmin_TokenKeepsWorking()
    {
        var (userId, tenantId, _) = await SeedUserInTenantsAsync("lastadmin");
        var roleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        await AssignRoleToUserAsync(userId, roleId, tenantId);
        var token = Fixture.GenerateTestToken(userId, tenantId);

        (await LeaveAsync(token, tenantId)).Should().Be(HttpStatusCode.Forbidden);

        (await MeAsync(token)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokedToken_CannotBeRefreshed()
    {
        var (userId, tenantId, _) = await SeedUserInTenantsAsync("refresh");
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString();
        var savedToken = Fixture.GenerateTestToken(userId, tenantId, additionalClaims:
            new[] { new System.Security.Claims.Claim("auth_time", authTime, System.Security.Claims.ClaimValueTypes.Integer64) });

        (await LeaveAsync(savedToken, tenantId)).Should().Be(HttpStatusCode.OK);

        var refresh = await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, "/auth/refresh", savedToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ExtractAuthCookie(refresh).Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task LeaveRejoinAndSignInAgain_NewTokenWorks()
    {
        var (userId, tenantId, _) = await SeedUserInTenantsAsync("rejoin");
        var oldToken = Fixture.GenerateTestToken(userId, tenantId);
        (await LeaveAsync(oldToken, tenantId)).Should().Be(HttpStatusCode.OK);

        // Rejoin (the login callback reactivates the same membership row)
        await using (var db = Fixture.CreateAuthDbContext())
        {
            var membership = await db.Set<UserTenant>().SingleAsync(m => m.UserId == userId && m.TenantId == tenantId);
            membership.Status = MembershipStatus.Active;
            await db.SaveChangesAsync();
        }

        // A real sign-in goes through Keycloak and takes longer than a second; the cutoff is rounded up
        // to the next whole second, so a token issued in the same second as the leave stays revoked.
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        var newToken = Fixture.GenerateTestToken(userId, tenantId);

        (await MeAsync(newToken)).Should().Be(HttpStatusCode.OK);
        (await MeAsync(oldToken)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Leave_EvictsCachedPermissionsForThatTenant()
    {
        var (userId, tenantId, _) = await SeedUserInTenantsAsync("cache");
        var cache = Fixture.Services.GetRequiredService<IMemoryCache>();
        var key = PermissionCacheKeys.For(userId, tenantId);
        cache.Set(key, new HashSet<string> { "orders.read" });

        (await LeaveAsync(Fixture.GenerateTestToken(userId, tenantId), tenantId)).Should().Be(HttpStatusCode.OK);

        cache.TryGetValue(key, out _).Should().BeFalse("a stale grant must not outlive the membership");
    }

    // ── Helpers ───────────────────────────────────────────────────

    private async Task<(Guid UserId, Guid TenantA, Guid? TenantB)> SeedUserInTenantsAsync(
        string label, bool secondTenant = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantA = await SeedTenantAsync($"Rev {label} A {suffix}", $"rev-{label}-a-{suffix}");
        var userId = await SeedUserAsync($"sub-rev-{label}-{suffix}", $"rev-{label}-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantA);

        Guid? tenantB = null;
        if (secondTenant)
        {
            tenantB = await SeedTenantAsync($"Rev {label} B {suffix}", $"rev-{label}-b-{suffix}");
            await SeedMembershipAsync(userId, tenantB.Value);
        }

        return (userId, tenantA, tenantB);
    }

    private async Task<HttpStatusCode> MeAsync(string token)
        => (await Client.SendAsync(CreateBearerRequest(HttpMethod.Get, "/auth/me", token))).StatusCode;

    private async Task<HttpStatusCode> LeaveAsync(string token, Guid tenantId)
        => (await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, $"/auth/tenants/{tenantId}/leave", token))).StatusCode;
}
