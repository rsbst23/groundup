using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Core.Dtos;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for flows that auto-join new users to the configured default tenant:
/// SimpleSignup, and Login for a user with zero memberships.
/// <para>
/// Uses <see cref="DefaultTenantAuthFlowFixture"/> (own database, default tenant seeded before
/// startup). These tests prove the membership is actually written: the membership repository
/// stamps the current tenant onto new rows, and during a sign-in callback no tenant is selected.
/// </para>
/// </summary>
[Collection("AuthFlowDefaultTenant")]
public sealed class DefaultTenantFlowTests : AuthFlowTestBase
{
    private readonly DefaultTenantAuthFlowFixture _defaultTenantFixture;

    public DefaultTenantFlowTests(DefaultTenantAuthFlowFixture fixture) : base(fixture)
    {
        _defaultTenantFixture = fixture;
    }

    private Guid DefaultTenantId => _defaultTenantFixture.DefaultTenantId;

    // ────────────────────────────────────────────────────────────────────────
    // SimpleSignup
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_SimpleSignup_NewUser_CreatesUserAndMembershipInDefaultTenant()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sub = $"kc-simple-{suffix}";
        var email = $"simple-{suffix}@example.com";
        var displayName = "Simple Signup User";

        var flowState = await CreatePendingFlowStateAsync(FlowType.SimpleSignup);
        SetupFullSuccessfulCallback(sub, flowState.Nonce, email, displayName);

        // Act
        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-simple", flowState.StateToken));

        // Assert — HTTP 200 with a token scoped to the default tenant
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = ReadToken(await response.Content.ReadAsStringAsync());
        ReadTid(token).Should().Be(DefaultTenantId.ToString());
        ExtractAuthCookie(response).Should().NotBeNullOrEmpty();

        // Assert — user created and joined to the default tenant
        var user = await GetUserByExternalIdAsync(sub);
        user.Should().NotBeNull();
        user!.Email.Should().Be(email);
        user.DisplayName.Should().Be(displayName);

        var memberships = await GetMembershipsForUserAsync(user.Id);
        memberships.Should().ContainSingle(m => m.TenantId == DefaultTenantId);
    }

    [Fact]
    public async Task Callback_SimpleSignup_ExistingMember_DoesNotCreateDuplicateMembership()
    {
        // Arrange — user is already a member of the default tenant
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sub = $"kc-idem-{suffix}";
        var email = $"idem-{suffix}@example.com";
        var userId = await SeedUserAsync(sub, email);
        await SeedMembershipAsync(userId, DefaultTenantId);

        var flowState = await CreatePendingFlowStateAsync(FlowType.SimpleSignup);
        SetupFullSuccessfulCallback(sub, flowState.Nonce, email);

        // Act
        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-idem", flowState.StateToken));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ReadTid(ReadToken(await response.Content.ReadAsStringAsync())).Should().Be(DefaultTenantId.ToString());

        var memberships = await GetMembershipsForUserAsync(userId);
        memberships.Should().ContainSingle(m => m.TenantId == DefaultTenantId,
            "no duplicate membership should be created for an existing member");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Login with zero memberships → auto-join
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_Login_ZeroMemberships_AutoJoinsDefaultTenant()
    {
        // Arrange — a known user with no memberships logs in
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sub = $"kc-autojoin-{suffix}";
        var email = $"autojoin-{suffix}@example.com";
        var userId = await SeedUserAsync(sub, email);

        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        SetupFullSuccessfulCallback(sub, flowState.Nonce, email);

        // Act
        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-autojoin", flowState.StateToken));

        // Assert — token scoped to the default tenant, and the membership exists
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ReadTid(ReadToken(await response.Content.ReadAsStringAsync())).Should().Be(DefaultTenantId.ToString());

        var memberships = await GetMembershipsForUserAsync(userId);
        memberships.Should().ContainSingle(m => m.TenantId == DefaultTenantId);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Rejoining after leaving: the membership row is kept (IsActive = false) and a user has
    // exactly one row per tenant, so rejoining must reactivate it rather than insert another.
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_Login_UserPreviouslyLeftDefaultTenant_ReactivatesTheSameMembershipRow()
    {
        // Arrange — a user who left the default tenant: their only membership is inactive
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sub = $"kc-rejoin-login-{suffix}";
        var email = $"rejoin-login-{suffix}@example.com";
        var userId = await SeedUserAsync(sub, email);
        var originalMembershipId = await SeedMembershipAsync(userId, DefaultTenantId, isActive: false);

        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        SetupFullSuccessfulCallback(sub, flowState.Nonce, email);

        // Act
        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-rejoin-login", flowState.StateToken));

        // Assert — was AUTO_JOIN_FAILED (unique index violation) before the fix
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ReadTid(ReadToken(await response.Content.ReadAsStringAsync())).Should().Be(DefaultTenantId.ToString());

        var rows = await GetAllMembershipRowsAsync(userId, DefaultTenantId);
        rows.Should().ContainSingle("a user has exactly one membership row per tenant");
        rows[0].Id.Should().Be(originalMembershipId, "the old row is reused");
        rows[0].Status.Should().Be(MembershipStatus.Active);
    }

    [Fact]
    public async Task Callback_SimpleSignup_UserPreviouslyLeftDefaultTenant_ReactivatesTheSameMembershipRow()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sub = $"kc-rejoin-signup-{suffix}";
        var email = $"rejoin-signup-{suffix}@example.com";
        var userId = await SeedUserAsync(sub, email);
        var originalMembershipId = await SeedMembershipAsync(userId, DefaultTenantId, isActive: false);

        var flowState = await CreatePendingFlowStateAsync(FlowType.SimpleSignup);
        SetupFullSuccessfulCallback(sub, flowState.Nonce, email);

        // Act
        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-rejoin-signup", flowState.StateToken));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ReadTid(ReadToken(await response.Content.ReadAsStringAsync())).Should().Be(DefaultTenantId.ToString());

        var rows = await GetAllMembershipRowsAsync(userId, DefaultTenantId);
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(originalMembershipId);
        rows[0].Status.Should().Be(MembershipStatus.Active);
    }

    [Fact]
    public async Task Login_FormerAdminWhoLeftAndIsAutoRejoined_DoesNotRegainTheAdminRole()
    {
        // Arrange — an admin of the default tenant (a second admin exists, so leaving is allowed)
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sub = $"kc-formeradmin-{suffix}";
        var email = $"formeradmin-{suffix}@example.com";
        var userId = await SeedUserAsync(sub, email);
        var otherAdminId = await SeedUserAsync($"kc-otheradmin-{suffix}", $"otheradmin-{suffix}@example.com");
        await SeedMembershipAsync(userId, DefaultTenantId);
        await SeedMembershipAsync(otherAdminId, DefaultTenantId);

        var adminRoleId = await SeedRoleAsync(DefaultTenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        await AssignRoleToUserAsync(userId, adminRoleId, DefaultTenantId);
        await AssignRoleToUserAsync(otherAdminId, adminRoleId, DefaultTenantId);

        // The admin leaves the default tenant through the API
        var leave = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, $"/auth/tenants/{DefaultTenantId}/leave",
            Fixture.GenerateTestToken(userId, DefaultTenantId)));
        leave.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — their next login auto-joins them back into the default tenant
        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        SetupFullSuccessfulCallback(sub, flowState.Nonce, email);
        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-former-admin", flowState.StateToken));

        // Assert — back in as an ordinary member, not as TenantAdmin
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetMembershipsForUserAsync(userId)).Should().ContainSingle(m => m.TenantId == DefaultTenantId);

        await using var context = Fixture.CreateAuthDbContext();
        var roles = await context.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.TenantId == DefaultTenantId)
            .ToListAsync();
        roles.Should().NotContain(ur => ur.RoleId == adminRoleId, "rejoining must not silently restore the roles the user had before leaving");
        roles.Should().ContainSingle("a rejoining member gets the default role again")
            .Which.Source.Should().Be(RoleAssignmentSource.DefaultRole);
    }

    // ── GU-68: default role and claiming pending users ─────────────

    [Fact]
    public async Task Callback_SimpleSignup_NewUser_GetsTheDefaultRole()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sub = $"kc-defrole-{suffix}";
        var flowState = await CreatePendingFlowStateAsync(FlowType.SimpleSignup);
        SetupFullSuccessfulCallback(sub, flowState.Nonce, $"defrole-{suffix}@example.com");

        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-defrole", flowState.StateToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await GetUserByExternalIdAsync(sub);
        await using var context = Fixture.CreateAuthDbContext();
        var role = await context.UserRoles.AsNoTracking().SingleAsync(ur => ur.UserId == user!.Id && ur.TenantId == DefaultTenantId);
        role.RoleId.Should().Be(_defaultTenantFixture.DefaultRoleId);
        role.Source.Should().Be(RoleAssignmentSource.DefaultRole);
    }

    [Fact]
    public async Task Callback_Login_VerifiedEmailOfAPendingUser_ClaimsThatUser()
    {
        // An invited person (pending user, no identity yet) signs in normally with the invited email
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"claim-{suffix}@example.com";
        var pendingId = await SeedPendingUserAsync(email);
        var sub = $"kc-claim-{suffix}";
        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        SetupSuccessfulCodeExchange(sub, flowState.Nonce, email);
        SetupUserInfoWithVerifiedEmail(sub, email, verified: true);

        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-claim", flowState.StateToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await GetUserByExternalIdAsync(sub);
        user!.Id.Should().Be(pendingId, "the pending user is linked instead of a new user being created");
    }

    [Fact]
    public async Task Callback_Login_UnverifiedEmailOfAPendingUser_CreatesANewUser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"noclaim-{suffix}@example.com";
        var pendingId = await SeedPendingUserAsync(email);
        var sub = $"kc-noclaim-{suffix}";
        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        SetupSuccessfulCodeExchange(sub, flowState.Nonce, email);
        SetupUserInfoWithVerifiedEmail(sub, email, verified: false);

        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-noclaim", flowState.StateToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await GetUserByExternalIdAsync(sub);
        user!.Id.Should().NotBe(pendingId, "an unverified email is no proof of owning the pending user");
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Users.AsNoTracking().SingleAsync(u => u.Id == pendingId)).ExternalUserId.Should().BeNull();
    }

    private void SetupUserInfoWithVerifiedEmail(string sub, string email, bool verified)
    {
        var userInfo = new ExternalUserInfo(sub, email, "Claimed User",
            new Dictionary<string, string> { ["email_verified"] = verified ? "true" : "false" });
        Fixture.MockIdentityProviderService
            .GetUserInfoAsync(Arg.Any<string>())
            .Returns(Task.FromResult<ExternalUserInfo?>(userInfo));
    }

    private async Task<Guid> SeedPendingUserAsync(string email)
    {
        await using var context = Fixture.CreateAuthDbContext();
        var user = new GroundUp.Auth.Core.Entities.User { ExternalUserId = null, Email = email, DisplayName = email, IsActive = true };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>All membership rows (active or not) for a user in a tenant.</summary>
    private async Task<List<GroundUp.Auth.Core.Entities.UserTenant>> GetAllMembershipRowsAsync(Guid userId, Guid tenantId)
    {
        await using var context = Fixture.CreateAuthDbContext();
        return await context.Set<GroundUp.Auth.Core.Entities.UserTenant>()
            .AsNoTracking()
            .Where(ut => ut.UserId == userId && ut.TenantId == tenantId)
            .ToListAsync();
    }

    private static string ReadToken(string responseBody)
    {
        var json = JsonDocument.Parse(responseBody);
        json.RootElement.TryGetProperty("token", out var tokenElement).Should().BeTrue(
            $"a successful callback returns a token; body was: {responseBody}");
        return tokenElement.GetString()!;
    }

    private static string? ReadTid(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.FirstOrDefault(c => c.Type == "tid")?.Value;
}
