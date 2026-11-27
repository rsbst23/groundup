using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-68 Requirement 9: join links through the sample app against real Postgres: administration
/// (<c>/api/join-links</c>), the public look-up and join start (<c>/auth/join-links</c>), and the
/// callback, including concurrent last use.
/// </summary>
[Collection("AuthFlow")]
public sealed class JoinLinkTests : AuthFlowTestBase
{
    public JoinLinkTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ── Administration ────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsTheTokenOnce_AndStoresOnlyItsHash()
    {
        var org = await SeedOrganizationAsync();

        var link = await CreateLinkAsync(org);

        link.Token.Should().HaveLength(43);
        await using var context = Fixture.CreateAuthDbContext();
        var stored = await context.JoinLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id);
        stored.TokenHash.Should().Be(Sha256Hex(link.Token));
        stored.CreatedByUserId.Should().Be(org.AdminId);

        var list = await Client.SendAsync(CreateBearerRequest(HttpMethod.Get, "/api/join-links", org.AdminToken));
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadAsStringAsync()).Should().NotContain(link.Token, "listing never shows a token");
    }

    [Fact]
    public async Task Create_AsPlainMember_Forbidden()
    {
        var org = await SeedOrganizationAsync();
        var request = CreateBearerRequest(HttpMethod.Post, "/api/join-links", org.MemberToken);
        request.Content = JsonContent.Create(new { roleId = org.EditorRoleId });

        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_EnterpriseTenant_Unprocessable()
    {
        var org = await SeedOrganizationAsync(realmName: $"realm-{Guid.NewGuid():N}");
        var request = CreateBearerRequest(HttpMethod.Post, "/api/join-links", org.AdminToken);
        request.Content = JsonContent.Create(new { roleId = org.EditorRoleId });

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.EnterpriseNotSupported);
    }

    [Fact]
    public async Task Regenerate_TheOldLinkStopsWorking()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org);

        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, $"/api/join-links/{link.Id}/regenerate", org.AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var newToken = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("data").GetProperty("token").GetString()!;
        newToken.Should().NotBe(link.Token);
        (await LookupAsync(link.Token)).GetProperty("reason").GetString().Should().Be("not_found");
        (await LookupAsync(newToken)).GetProperty("usable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_TheLinkReportsRevoked_AndCannotRepeat()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org);

        (await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, $"/api/join-links/{link.Id}/revoke", org.AdminToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await LookupAsync(link.Token)).GetProperty("reason").GetString().Should().Be("revoked");
        (await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, $"/api/join-links/{link.Id}/revoke", org.AdminToken)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Look-up and start ─────────────────────────────────────────

    [Fact]
    public async Task Lookup_ReportsExpiredAndUsedUp()
    {
        var org = await SeedOrganizationAsync();
        var expired = await CreateLinkAsync(org);
        var usedUp = await CreateLinkAsync(org, maxUses: 1);
        await using (var context = Fixture.CreateAuthDbContext())
        {
            await context.JoinLinks.Where(l => l.Id == expired.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            await context.JoinLinks.Where(l => l.Id == usedUp.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.UseCount, 1));
        }

        (await LookupAsync(expired.Token)).GetProperty("reason").GetString().Should().Be("expired");
        (await LookupAsync(usedUp.Token)).GetProperty("reason").GetString().Should().Be("used_up");
    }

    [Fact]
    public async Task Lookup_Usable_ShowsOrganizationAndRole()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org);

        var body = await LookupAsync(link.Token);

        body.GetProperty("usable").GetBoolean().Should().BeTrue();
        body.GetProperty("organizationName").GetString().Should().Be(org.Name);
        body.GetProperty("roleName").GetString().Should().Be("Editor");
    }

    [Fact]
    public async Task Join_OpensTheSignInPageWithoutAnEmailHint()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org);

        var url = await StartAsync(link.Token);

        url.Should().NotContain("prompt=");
        url.Should().NotContain("login_hint");
    }

    [Fact]
    public async Task Join_ForceLogin_ForcesSignIn()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org);

        (await StartAsync(link.Token, forceLogin: true)).Should().Contain("prompt=login");
    }

    [Fact]
    public async Task Join_OnAnotherOrganizationsHost_Forbidden()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org);
        var other = await SeedOrganizationAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/join-links/join") { Content = JsonContent.Create(new { token = link.Token }) };
        request.Headers.Host = $"{other.Slug}.{AuthFlowTestFixture.TestDefaultDomain}";

        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Callback ──────────────────────────────────────────────────

    [Fact]
    public async Task Callback_NewUser_JoinsActiveWithTheRoleAndCountsTheUse()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org);
        var sub = $"kc-join-{org.Suffix}";

        var response = await CallbackAsync(link.Id, sub, $"join-{org.Suffix}@example.com");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("alreadyMember").GetBoolean().Should().BeFalse();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.GetProperty("token").GetString());
        jwt.Claims.Single(c => c.Type == "tid").Value.Should().Be(org.TenantId.ToString());
        jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).Should().Contain("Editor");

        await using var context = Fixture.CreateAuthDbContext();
        var user = await context.Users.AsNoTracking().SingleAsync(u => u.ExternalUserId == sub);
        (await StatusOfAsync(user.Id, org.TenantId)).Should().Be(MembershipStatus.Active);
        (await context.UserRoles.AsNoTracking().SingleAsync(ur => ur.UserId == user.Id)).Source.Should().Be(RoleAssignmentSource.JoinLink);
        (await context.JoinLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id)).UseCount.Should().Be(1);
    }

    [Fact]
    public async Task Callback_FormerMember_RejoinsThroughTheSameRow()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org);
        var sub = $"kc-former-{org.Suffix}";
        var formerId = await SeedUserAsync(sub, $"former-{org.Suffix}@example.com");
        await SeedMembershipAsync(formerId, org.TenantId, isActive: false);

        var response = await CallbackAsync(link.Id, sub, $"former-{org.Suffix}@example.com");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = Fixture.CreateAuthDbContext();
        (await context.UserTenants.CountAsync(ut => ut.UserId == formerId && ut.TenantId == org.TenantId)).Should().Be(1);
        (await StatusOfAsync(formerId, org.TenantId)).Should().Be(MembershipStatus.Active);
    }

    [Fact]
    public async Task Callback_PendingMember_BecomesActiveKeepsTheInvitationRoleAndClosesTheInvitation()
    {
        var org = await SeedOrganizationAsync();
        var viewerRoleId = await SeedRoleAsync(org.TenantId, "Viewer");
        var link = await CreateLinkAsync(org);
        var email = $"pending-{org.Suffix}@example.com";
        var invitationRequest = CreateBearerRequest(HttpMethod.Post, "/api/invitations", org.AdminToken);
        invitationRequest.Content = JsonContent.Create(new { email, roleId = viewerRoleId });
        var invitationResponse = await Client.SendAsync(invitationRequest);
        invitationResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var invitation = JsonDocument.Parse(await invitationResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        var invitationId = Guid.Parse(invitation.GetProperty("id").GetString()!);
        var pendingId = Guid.Parse(invitation.GetProperty("userId").GetString()!);
        var sub = $"kc-pendjoin-{org.Suffix}";
        // The invitee's account is linked (verified email elsewhere), then they use the join link
        await using (var context = Fixture.CreateAuthDbContext())
        {
            await context.Users.Where(u => u.Id == pendingId).ExecuteUpdateAsync(s => s.SetProperty(u => u.ExternalUserId, sub));
        }

        var response = await CallbackAsync(link.Id, sub, email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var verify = Fixture.CreateAuthDbContext();
        (await StatusOfAsync(pendingId, org.TenantId)).Should().Be(MembershipStatus.Active);
        var roles = await verify.UserRoles.AsNoTracking().Where(ur => ur.UserId == pendingId).ToListAsync();
        roles.Select(r => (r.RoleId, r.Source)).Should().BeEquivalentTo(new[]
        {
            (viewerRoleId, RoleAssignmentSource.Invitation),
            (org.EditorRoleId, RoleAssignmentSource.JoinLink)
        });
        (await verify.Invitations.AsNoTracking().SingleAsync(i => i.Id == invitationId)).Status.Should().Be(InvitationStatus.Accepted);
    }

    [Fact]
    public async Task Callback_AlreadyActiveMember_SignsInWithoutCountingOrGranting()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org);

        var response = await CallbackAsync(link.Id, org.MemberSub, org.MemberEmail);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("alreadyMember").GetBoolean().Should().BeTrue();
        await using var context = Fixture.CreateAuthDbContext();
        (await context.JoinLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id)).UseCount.Should().Be(0);
        (await context.UserRoles.CountAsync(ur => ur.UserId == org.MemberId)).Should().Be(0);
    }

    [Fact]
    public async Task Callback_LinkRevokedDuringSignIn_Gone()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org);
        await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, $"/api/join-links/{link.Id}/revoke", org.AdminToken));

        var response = await CallbackAsync(link.Id, $"kc-late-{org.Suffix}", $"late-{org.Suffix}@example.com");

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.JoinLinkNoLongerValid);
    }

    [Fact]
    public async Task Callback_CreatorMayNoLongerGrantTheRole_RefusedWithoutJoining()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org);
        await using (var context = Fixture.CreateAuthDbContext())
        {
            await context.JoinLinks.Where(l => l.Id == link.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedByUserId, org.MemberId));
        }

        var sub = $"kc-lost-{org.Suffix}";
        var response = await CallbackAsync(link.Id, sub, $"lost-{org.Suffix}@example.com");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.JoinLinkNoLongerValid);
        await using var verify = Fixture.CreateAuthDbContext();
        (await verify.JoinLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id)).UseCount.Should().Be(0);
        var user = await verify.Users.AsNoTracking().SingleAsync(u => u.ExternalUserId == sub);
        (await verify.UserTenants.CountAsync(ut => ut.UserId == user.Id && ut.TenantId == org.TenantId)).Should().Be(0);
    }

    [Fact]
    public async Task Callback_ConcurrentLastUse_NeverExceedsTheMaximum()
    {
        var org = await SeedOrganizationAsync();
        var link = await CreateLinkAsync(org, maxUses: 1);
        var nonce = AuthFlowTestFixture.GenerateNonce();
        var states = new[]
        {
            await CreatePendingFlowStateAsync(FlowType.JoinLink, nonce: nonce, joinLinkId: link.Id),
            await CreatePendingFlowStateAsync(FlowType.JoinLink, nonce: nonce, joinLinkId: link.Id),
            await CreatePendingFlowStateAsync(FlowType.JoinLink, nonce: nonce, joinLinkId: link.Id)
        };
        // Three different people (the mocked identity provider answers per call, in order)
        var subs = Enumerable.Range(0, 3).Select(i => $"kc-last-{i}-{org.Suffix}").ToArray();
        SetupSuccessfulCodeExchange(subs[0], nonce, $"last-0-{org.Suffix}@example.com");
        Fixture.MockIdentityProviderService.GetUserInfoAsync(Arg.Any<string>()).Returns(
            new ExternalUserInfo(subs[0], $"last-0-{org.Suffix}@example.com", "L0", null),
            new ExternalUserInfo(subs[1], $"last-1-{org.Suffix}@example.com", "L1", null),
            new ExternalUserInfo(subs[2], $"last-2-{org.Suffix}@example.com", "L2", null));

        var responses = await Task.WhenAll(states.Select(s => Client.SendAsync(CreateCallbackRequest("auth-code-last", s.StateToken))));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Gone);
        await using var context = Fixture.CreateAuthDbContext();
        (await context.JoinLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id)).UseCount.Should().Be(1);
        (await context.UserTenants.CountAsync(ut => ut.TenantId == org.TenantId && ut.Status == MembershipStatus.Active))
            .Should().Be(3, "the admin, the member and exactly one newcomer");
    }

    // ── Helpers ───────────────────────────────────────────────────

    private sealed record Organization(
        Guid TenantId, string Name, string Slug, Guid AdminId, string AdminToken,
        Guid MemberId, string MemberSub, string MemberEmail, string MemberToken, Guid EditorRoleId, string Suffix);

    private sealed record Link(Guid Id, string Token);

    private async Task<Organization> SeedOrganizationAsync(string? realmName = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Join Org {suffix}";
        var slug = $"join-org-{suffix}";
        var tenantId = await SeedTenantAsync(name, slug, realmName: realmName);
        var adminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        var editorRoleId = await SeedRoleAsync(tenantId, "Editor");
        var adminId = await SeedUserAsync($"kc-jladmin-{suffix}", $"jladmin-{suffix}@example.com");
        await SeedMembershipAsync(adminId, tenantId);
        await AssignRoleToUserAsync(adminId, adminRoleId, tenantId);
        var memberSub = $"kc-jlmember-{suffix}";
        var memberEmail = $"jlmember-{suffix}@example.com";
        var memberId = await SeedUserAsync(memberSub, memberEmail);
        await SeedMembershipAsync(memberId, tenantId);
        return new Organization(tenantId, name, slug, adminId, Fixture.GenerateTestToken(adminId, tenantId),
            memberId, memberSub, memberEmail, Fixture.GenerateTestToken(memberId, tenantId), editorRoleId, suffix);
    }

    private async Task<Link> CreateLinkAsync(Organization org, int? maxUses = null)
    {
        var request = CreateBearerRequest(HttpMethod.Post, "/api/join-links", org.AdminToken);
        request.Content = JsonContent.Create(new { roleId = org.EditorRoleId, maxUses });
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        return new Link(Guid.Parse(data.GetProperty("link").GetProperty("id").GetString()!), data.GetProperty("token").GetString()!);
    }

    private async Task<JsonElement> LookupAsync(string token)
    {
        var response = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/join-links/lookup")
        {
            Content = JsonContent.Create(new { token })
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private async Task<string> StartAsync(string token, bool forceLogin = false)
    {
        var response = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/join-links/join")
        {
            Content = JsonContent.Create(new { token, forceLogin })
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("redirectUrl").GetString()!;
    }

    private async Task<HttpResponseMessage> CallbackAsync(Guid joinLinkId, string sub, string email)
    {
        var state = await CreatePendingFlowStateAsync(FlowType.JoinLink, joinLinkId: joinLinkId);
        SetupFullSuccessfulCallback(sub, state.Nonce, email);
        return await Client.SendAsync(CreateCallbackRequest("auth-code-join", state.StateToken));
    }

    private async Task<MembershipStatus> StatusOfAsync(Guid userId, Guid tenantId)
    {
        await using var context = Fixture.CreateAuthDbContext();
        return await context.UserTenants.AsNoTracking()
            .Where(ut => ut.UserId == userId && ut.TenantId == tenantId)
            .Select(ut => ut.Status)
            .SingleAsync();
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errorCode").GetString();

    private static string Sha256Hex(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
