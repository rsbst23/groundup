using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Results;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-68 Requirement 7: the public invitation endpoints and the invitation callback, through the
/// sample app against real Postgres, with the identity provider mocked.
/// </summary>
[Collection("AuthFlow")]
public sealed class InvitationAcceptanceTests : AuthFlowTestBase
{
    public InvitationAcceptanceTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ── Look-up ───────────────────────────────────────────────────

    [Fact]
    public async Task Lookup_UsableInvitation_ShowsOrganizationAndRole()
    {
        var invite = await InviteAsync();

        var body = await PostAsync("/auth/invitations/lookup", new { token = invite.Token });

        body.GetProperty("usable").GetBoolean().Should().BeTrue();
        body.GetProperty("organizationName").GetString().Should().Be(invite.Org.Name);
        body.GetProperty("roleName").GetString().Should().Be("Editor");
    }

    [Fact]
    public async Task Lookup_UnknownOrRevoked_GivesOnlyTheReason()
    {
        var invite = await InviteAsync();
        (await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, $"/api/invitations/{invite.Id}/revoke", invite.Org.AdminToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var revoked = await PostAsync("/auth/invitations/lookup", new { token = invite.Token });
        var unknown = await PostAsync("/auth/invitations/lookup", new { token = $"unknown-{Guid.NewGuid():N}" });

        revoked.GetProperty("reason").GetString().Should().Be("revoked");
        revoked.GetProperty("organizationName").ValueKind.Should().Be(JsonValueKind.Null);
        unknown.GetProperty("reason").GetString().Should().Be("not_found");
    }

    // ── Starting the flow: the page is chosen by the server ───────

    [Fact]
    public async Task Accept_NoSessionAndNoAccount_OpensRegistrationWithTheEmailFilledIn()
    {
        var invite = await InviteAsync();

        var url = await StartAsync(invite.Token);

        url.Should().Contain("prompt=create");
        url.Should().Contain($"login_hint={Uri.EscapeDataString(invite.Email)}");
    }

    [Fact]
    public async Task Accept_NoSessionAndAnExistingAccount_OpensSignIn()
    {
        var invite = await InviteAsync();
        Fixture.MockIdentityProviderAdminService
            .FindUserByEmailAsync(Arg.Any<string>(), invite.Email, Arg.Any<CancellationToken>())
            .Returns(OperationResult<IdentityProviderUserDto?>.Ok(new IdentityProviderUserDto("kc-x", invite.Email, true)));

        var url = await StartAsync(invite.Token);

        url.Should().NotContain("prompt=");
        url.Should().Contain("login_hint=");
    }

    [Fact]
    public async Task Accept_SessionForAnotherEmail_ForcesSignIn()
    {
        var invite = await InviteAsync();
        var otherSession = Fixture.GenerateTestToken(Guid.NewGuid(), null, email: "someone-else@example.com");

        var url = await StartAsync(invite.Token, sessionToken: otherSession);

        url.Should().Contain("prompt=login");
    }

    [Fact]
    public async Task Accept_SessionForTheInvitedEmail_NoPrompt()
    {
        var invite = await InviteAsync();
        var session = Fixture.GenerateTestToken(Guid.NewGuid(), null, email: invite.Email.ToUpperInvariant());

        var url = await StartAsync(invite.Token, sessionToken: session);

        url.Should().NotContain("prompt=");
    }

    [Fact]
    public async Task Accept_ForceLogin_ForcesSignIn()
    {
        var invite = await InviteAsync();

        var url = await StartAsync(invite.Token, forceLogin: true);

        url.Should().Contain("prompt=login");
    }

    [Fact]
    public async Task Accept_OnAnotherOrganizationsHost_Forbidden()
    {
        var invite = await InviteAsync();
        var other = await SeedOrganizationAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/invitations/accept")
        {
            Content = JsonContent.Create(new { token = invite.Token })
        };
        request.Headers.Host = $"{other.Slug}.{AuthFlowTestFixture.TestDefaultDomain}";

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.InvitationWrongOrganization);
    }

    [Fact]
    public async Task Accept_RevokedInvitation_RefusedBeforeTheFlowStarts()
    {
        var invite = await InviteAsync();
        await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, $"/api/invitations/{invite.Id}/revoke", invite.Org.AdminToken));

        var request = CreateJsonPost("/auth/invitations/accept", new { token = invite.Token });
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.InvitationNoLongerValid);
    }

    // ── Callback ──────────────────────────────────────────────────

    [Fact]
    public async Task Callback_NewInvitee_ClaimsThePendingUserActivatesTheMembershipAndSignsIn()
    {
        var invite = await InviteAsync();
        var sub = $"kc-invitee-{invite.Org.Suffix}";

        var response = await CallbackAsync(invite, sub, invite.Email.ToUpperInvariant());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("alreadyMember").GetBoolean().Should().BeFalse();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.GetProperty("token").GetString());
        jwt.Claims.Single(c => c.Type == "tid").Value.Should().Be(invite.Org.TenantId.ToString());
        jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).Should().Contain("Editor", "the invitation's role now counts");

        await using var context = Fixture.CreateAuthDbContext();
        var user = await context.Users.AsNoTracking().SingleAsync(u => u.ExternalUserId == sub);
        user.Id.Should().Be(invite.UserId, "the pending user is claimed, not duplicated");
        (await StatusOfAsync(user.Id, invite.Org.TenantId)).Should().Be(MembershipStatus.Active);
        var invitation = await context.Invitations.AsNoTracking().SingleAsync(i => i.Id == invite.Id);
        invitation.Status.Should().Be(InvitationStatus.Accepted);
        invitation.AcceptedByUserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Callback_ExistingUserWithAnotherRecord_MergesThePendingUser()
    {
        var invite = await InviteAsync();
        // The invitee already had an account under another record (created after the invitation)
        var sub = $"kc-merge-{invite.Org.Suffix}";
        var existingId = await SeedUserAsync(sub, invite.Email);

        var response = await CallbackAsync(invite, sub, invite.Email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusOfAsync(existingId, invite.Org.TenantId)).Should().Be(MembershipStatus.Active);
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Users.AsNoTracking().SingleAsync(u => u.Id == invite.UserId)).MergedIntoUserId.Should().Be(existingId);
        (await context.UserRoles.CountAsync(ur => ur.UserId == existingId && ur.TenantId == invite.Org.TenantId)).Should().Be(1);
    }

    [Fact]
    public async Task Callback_AlreadyActiveMember_SignsInWithoutRoleChangesAndClosesTheInvitation()
    {
        var invite = await InviteAsync();
        var sub = $"kc-already-{invite.Org.Suffix}";
        // The invitee joined another way and already holds their own role set
        await using (var context = Fixture.CreateAuthDbContext())
        {
            await context.Users.Where(u => u.Id == invite.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.ExternalUserId, sub));
            await context.UserTenants.Where(ut => ut.UserId == invite.UserId).ExecuteUpdateAsync(s => s.SetProperty(ut => ut.Status, MembershipStatus.Active));
            await context.UserRoles.Where(ur => ur.UserId == invite.UserId).ExecuteDeleteAsync();
        }

        var response = await CallbackAsync(invite, sub, invite.Email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("alreadyMember").GetBoolean().Should().BeTrue();
        await using var verify = Fixture.CreateAuthDbContext();
        (await verify.UserRoles.CountAsync(ur => ur.UserId == invite.UserId)).Should().Be(0, "the invitation is no back door to a role");
        (await verify.Invitations.AsNoTracking().SingleAsync(i => i.Id == invite.Id)).Status.Should().Be(InvitationStatus.Accepted);
    }

    [Fact]
    public async Task Callback_DifferentEmail_RefusedAndTheInvitationStaysPending()
    {
        var invite = await InviteAsync();

        var response = await CallbackAsync(invite, $"kc-wrong-{invite.Org.Suffix}", $"wrong-{invite.Org.Suffix}@example.com");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("errorCode").GetString().Should().Be(AuthErrorCodes.InvitationEmailMismatch);
        body.GetProperty("message").GetString().Should().Contain(invite.Email, "the message names the invited address");
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Invitations.AsNoTracking().SingleAsync(i => i.Id == invite.Id)).Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task Callback_InvitationExpiredDuringSignIn_Gone()
    {
        var invite = await InviteAsync();
        await using (var context = Fixture.CreateAuthDbContext())
        {
            await context.Invitations.Where(i => i.Id == invite.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        var response = await CallbackAsync(invite, $"kc-late-{invite.Org.Suffix}", invite.Email);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.InvitationNoLongerValid);
    }

    [Fact]
    public async Task Callback_CreatorMayNoLongerGrantTheRole_RefusedAndTheInvitationStaysPending()
    {
        var invite = await InviteAsync();
        // The creator is now someone without the right to grant Editor
        await using (var context = Fixture.CreateAuthDbContext())
        {
            await context.Invitations.Where(i => i.Id == invite.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedByUserId, invite.Org.MemberId));
        }

        var response = await CallbackAsync(invite, $"kc-lost-{invite.Org.Suffix}", invite.Email);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.InvitationNoLongerValid);
        await using var verify = Fixture.CreateAuthDbContext();
        (await verify.Invitations.AsNoTracking().SingleAsync(i => i.Id == invite.Id)).Status.Should().Be(InvitationStatus.Pending);
        (await StatusOfAsync(invite.UserId, invite.Org.TenantId)).Should().Be(MembershipStatus.Pending);
    }

    [Fact]
    public async Task Callback_ConcurrentAcceptances_AcceptExactlyOnce()
    {
        var invite = await InviteAsync();
        var sub = $"kc-race-{invite.Org.Suffix}";
        // Two sign-ins in flight at once; the mocked identity provider returns one ID token, so both
        // states share its nonce
        var nonce = AuthFlowTestFixture.GenerateNonce();
        var states = new[]
        {
            await CreatePendingFlowStateAsync(FlowType.Invitation, nonce: nonce, invitationId: invite.Id),
            await CreatePendingFlowStateAsync(FlowType.Invitation, nonce: nonce, invitationId: invite.Id)
        };
        SetupFullSuccessfulCallback(sub, nonce, invite.Email);

        var responses = await Task.WhenAll(states.Select(s =>
            Client.SendAsync(CreateCallbackRequest("auth-code-race", s.StateToken))));

        var outcomes = new List<string>();
        foreach (var response in responses)
        {
            var text = await response.Content.ReadAsStringAsync();
            outcomes.Add(response.StatusCode == HttpStatusCode.OK
                ? JsonDocument.Parse(text).RootElement.GetProperty("alreadyMember").GetBoolean() ? "already-member" : "accepted"
                : $"{(int)response.StatusCode}");
        }

        outcomes.Count(o => o == "accepted").Should().Be(1, $"outcomes were {string.Join(", ", outcomes)}");
        outcomes.Where(o => o != "accepted").Should().OnlyContain(o => o == "already-member" || o == "410");
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Invitations.AsNoTracking().SingleAsync(i => i.Id == invite.Id)).Status.Should().Be(InvitationStatus.Accepted);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private sealed record Organization(Guid TenantId, string Name, string Slug, Guid AdminId, string AdminToken, Guid MemberId, Guid EditorRoleId, string Suffix);

    private sealed record Invite(Organization Org, Guid Id, Guid UserId, string Email, string Token);

    private async Task<Organization> SeedOrganizationAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Accept Org {suffix}";
        var slug = $"acc-org-{suffix}";
        var tenantId = await SeedTenantAsync(name, slug);
        var adminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        var editorRoleId = await SeedRoleAsync(tenantId, "Editor");
        var adminId = await SeedUserAsync($"kc-accadmin-{suffix}", $"accadmin-{suffix}@example.com");
        await SeedMembershipAsync(adminId, tenantId);
        await AssignRoleToUserAsync(adminId, adminRoleId, tenantId);
        var memberId = await SeedUserAsync($"kc-accmember-{suffix}", $"accmember-{suffix}@example.com");
        await SeedMembershipAsync(memberId, tenantId);
        return new Organization(tenantId, name, slug, adminId, Fixture.GenerateTestToken(adminId, tenantId), memberId, editorRoleId, suffix);
    }

    /// <summary>An organization and an invitation (for a new email, so a pending user) to it.</summary>
    private async Task<Invite> InviteAsync()
    {
        var org = await SeedOrganizationAsync();
        var email = $"invitee-{org.Suffix}@example.com";
        var request = CreateBearerRequest(HttpMethod.Post, "/api/invitations", org.AdminToken);
        request.Content = JsonContent.Create(new { email, roleId = org.EditorRoleId });
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        var id = Guid.Parse(data.GetProperty("id").GetString()!);
        return new Invite(org, id, Guid.Parse(data.GetProperty("userId").GetString()!), email, Fixture.InvitationEmails.TokenFor(id));
    }

    private async Task<string> StartAsync(string token, string? sessionToken = null, bool forceLogin = false)
    {
        var request = sessionToken is null
            ? CreateJsonPost("/auth/invitations/accept", new { token, forceLogin })
            : CreateBearerRequest(HttpMethod.Post, "/auth/invitations/accept", sessionToken);
        if (sessionToken is not null)
        {
            request.Content = JsonContent.Create(new { token, forceLogin });
        }

        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("redirectUrl").GetString()!;
    }

    private async Task<HttpResponseMessage> CallbackAsync(Invite invite, string sub, string email)
    {
        var state = await CreatePendingFlowStateAsync(FlowType.Invitation, invitationId: invite.Id);
        SetupFullSuccessfulCallback(sub, state.Nonce, email);
        return await Client.SendAsync(CreateCallbackRequest("auth-code-invite", state.StateToken));
    }

    private async Task<JsonElement> PostAsync(string url, object body)
    {
        var response = await Client.SendAsync(CreateJsonPost(url, body));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static HttpRequestMessage CreateJsonPost(string url, object body)
        => new(HttpMethod.Post, url) { Content = JsonContent.Create(body) };

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
}
