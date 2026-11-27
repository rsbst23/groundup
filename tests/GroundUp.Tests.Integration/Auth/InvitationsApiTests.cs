using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-68 Requirements 6 and 8: invitation administration (<c>/api/invitations</c>) through the
/// sample app against real Postgres: the pending user, membership and role it creates, the token
/// handling, resend, revoke, listing, and the conflicts.
/// </summary>
[Collection("AuthFlow")]
public sealed class InvitationsApiTests : AuthFlowTestBase
{
    public InvitationsApiTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Create_NewEmail_CreatesPendingUserMembershipAndRole_AndEmailsTheLink()
    {
        var org = await SeedOrganizationAsync();
        var email = $"New.Invitee-{org.Suffix}@Example.com";

        var response = await CreateAsync(org, email, org.EditorRoleId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        var invitationId = Guid.Parse(JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("id").GetString()!);

        await using var context = Fixture.CreateAuthDbContext();
        var invitation = await context.Invitations.AsNoTracking().SingleAsync(i => i.Id == invitationId);
        invitation.Status.Should().Be(InvitationStatus.Pending);
        invitation.NormalizedEmail.Should().Be(email.ToLowerInvariant());
        invitation.CreatedByUserId.Should().Be(org.AdminId);

        var user = await context.Users.AsNoTracking().SingleAsync(u => u.Id == invitation.UserId);
        user.ExternalUserId.Should().BeNull("the invitee has not signed in yet");
        user.Email.Should().Be(email);

        var membership = await context.UserTenants.AsNoTracking().SingleAsync(ut => ut.UserId == user.Id);
        membership.TenantId.Should().Be(org.TenantId);
        membership.Status.Should().Be(MembershipStatus.Pending);

        var role = await context.UserRoles.AsNoTracking().SingleAsync(ur => ur.UserId == user.Id);
        role.RoleId.Should().Be(org.EditorRoleId);
        role.Source.Should().Be(RoleAssignmentSource.Invitation);

        // The link carries the token; only its hash is stored, and the API never returns it
        var sent = Fixture.InvitationEmails.LatestFor(invitationId);
        sent.To.Should().Be(email);
        sent.AcceptUrl.Should().Contain($"org={org.Slug}");
        var token = Fixture.InvitationEmails.TokenFor(invitationId);
        token.Should().HaveLength(43);
        invitation.TokenHash.Should().Be(Sha256Hex(token));
        body.Should().NotContain(token);
    }

    [Fact]
    public async Task Create_EmailOfAnExistingNonMember_AttachesToThatUser()
    {
        var org = await SeedOrganizationAsync();
        var email = $"existing-{org.Suffix}@example.com";
        var existingId = await SeedUserAsync($"kc-existing-{org.Suffix}", email);

        var response = await CreateAsync(org, email.ToUpperInvariant(), org.EditorRoleId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Invitations.AsNoTracking().SingleAsync(i => i.TenantId == org.TenantId)).UserId.Should().Be(existingId);
        (await context.Users.CountAsync(u => u.Email.ToLower() == email)).Should().Be(1, "no second user is created");
    }

    [Fact]
    public async Task Create_FormerMember_SetsTheirMembershipBackToPending()
    {
        var org = await SeedOrganizationAsync();
        var email = $"former-{org.Suffix}@example.com";
        var formerId = await SeedUserAsync($"kc-former-{org.Suffix}", email);
        await SeedMembershipAsync(formerId, org.TenantId, isActive: false);

        (await CreateAsync(org, email, org.EditorRoleId)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await StatusOfAsync(formerId, org.TenantId)).Should().Be(MembershipStatus.Pending);
    }

    [Fact]
    public async Task Create_ActiveMember_ConflictAlreadyMember()
    {
        var org = await SeedOrganizationAsync();

        var response = await CreateAsync(org, $"member-{org.Suffix}@example.com", org.EditorRoleId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.AlreadyMember);
    }

    [Fact]
    public async Task Create_SecondInvitationForTheSameEmail_ConflictInvitationPending()
    {
        var org = await SeedOrganizationAsync();
        var email = $"twice-{org.Suffix}@example.com";
        (await CreateAsync(org, email, org.EditorRoleId)).StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await CreateAsync(org, email, org.EditorRoleId);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(second)).Should().Be(AuthErrorCodes.InvitationPending);
    }

    [Fact]
    public async Task Create_ConcurrentForTheSameEmail_ExactlyOneSucceeds()
    {
        var org = await SeedOrganizationAsync();
        var email = $"race-{org.Suffix}@example.com";

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CreateAsync(org, email, org.EditorRoleId)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Invitations.CountAsync(i => i.TenantId == org.TenantId)).Should().Be(1);
        (await context.UserTenants.CountAsync(ut => ut.TenantId == org.TenantId && ut.Status == MembershipStatus.Pending)).Should().Be(1);
    }

    [Fact]
    public async Task Create_AsPlainMember_Forbidden()
    {
        var org = await SeedOrganizationAsync();

        var request = CreateBearerRequest(HttpMethod.Post, "/api/invitations", org.MemberToken);
        request.Content = JsonContent.Create(new { email = $"x-{org.Suffix}@example.com", roleId = org.EditorRoleId });
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_InvalidEmail_BadRequest()
    {
        var org = await SeedOrganizationAsync();

        (await CreateAsync(org, "not-an-email", org.EditorRoleId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_EnterpriseTenant_Unprocessable()
    {
        var org = await SeedOrganizationAsync(realmName: $"realm-{Guid.NewGuid():N}");

        var response = await CreateAsync(org, $"ent-{org.Suffix}@example.com", org.EditorRoleId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.EnterpriseNotSupported);
    }

    [Fact]
    public async Task Resend_ReplacesTheTokenAndRestartsTheExpiry()
    {
        var org = await SeedOrganizationAsync();
        var invitationId = await CreateAndReadIdAsync(org, $"resend-{org.Suffix}@example.com");
        var oldToken = Fixture.InvitationEmails.TokenFor(invitationId);
        await SetExpiryAsync(invitationId, DateTime.UtcNow.AddHours(-1));

        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, $"/api/invitations/{invitationId}/resend", org.AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var newToken = Fixture.InvitationEmails.TokenFor(invitationId);
        newToken.Should().NotBe(oldToken);
        await using var context = Fixture.CreateAuthDbContext();
        var invitation = await context.Invitations.AsNoTracking().SingleAsync(i => i.Id == invitationId);
        invitation.TokenHash.Should().Be(Sha256Hex(newToken), "the old link stops working");
        invitation.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task Revoke_RemovesThePendingMembershipAndItsRoles_AndCannotRepeat()
    {
        var org = await SeedOrganizationAsync();
        var invitationId = await CreateAndReadIdAsync(org, $"revoke-{org.Suffix}@example.com");
        Guid inviteeId;
        await using (var context = Fixture.CreateAuthDbContext())
        {
            inviteeId = (await context.Invitations.AsNoTracking().SingleAsync(i => i.Id == invitationId)).UserId;
        }

        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, $"/api/invitations/{invitationId}/revoke", org.AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var context = Fixture.CreateAuthDbContext())
        {
            (await context.Invitations.AsNoTracking().SingleAsync(i => i.Id == invitationId)).Status.Should().Be(InvitationStatus.Revoked);
            (await context.UserRoles.CountAsync(ur => ur.UserId == inviteeId)).Should().Be(0);
            (await context.Users.CountAsync(u => u.Id == inviteeId)).Should().Be(1, "the pending user record is kept");
        }

        (await StatusOfAsync(inviteeId, org.TenantId)).Should().Be(MembershipStatus.Inactive);

        var again = await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, $"/api/invitations/{invitationId}/revoke", org.AdminToken));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_ReportsAnInvitationPastItsExpiryAsExpired()
    {
        var org = await SeedOrganizationAsync();
        var invitationId = await CreateAndReadIdAsync(org, $"expired-{org.Suffix}@example.com");
        await SetExpiryAsync(invitationId, DateTime.UtcNow.AddMinutes(-1));

        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Get, "/api/invitations", org.AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Total-Count").Single().Should().Be("1");
        var item = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("data").GetProperty("items").EnumerateArray().Single();
        item.GetProperty("status").GetInt32().Should().Be((int)InvitationStatus.Expired);
    }

    [Fact]
    public async Task RemovingThePendingMember_RevokesTheirInvitation()
    {
        var org = await SeedOrganizationAsync();
        var invitationId = await CreateAndReadIdAsync(org, $"removed-{org.Suffix}@example.com");
        await using var context = Fixture.CreateAuthDbContext();
        var inviteeId = (await context.Invitations.AsNoTracking().SingleAsync(i => i.Id == invitationId)).UserId;

        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Delete, $"/api/members/{inviteeId}", org.AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await context.Invitations.AsNoTracking().SingleAsync(i => i.Id == invitationId)).Status.Should().Be(InvitationStatus.Revoked);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private sealed record Organization(
        Guid TenantId, string Slug, Guid AdminId, string AdminToken, string MemberToken, Guid EditorRoleId, string Suffix);

    private async Task<Organization> SeedOrganizationAsync(string? realmName = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"inv-org-{suffix}";
        var tenantId = await SeedTenantAsync($"Invitations Org {suffix}", slug, realmName: realmName);
        var adminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        var editorRoleId = await SeedRoleAsync(tenantId, "Editor");

        var adminId = await SeedUserAsync($"kc-invadmin-{suffix}", $"admin-{suffix}@example.com");
        await SeedMembershipAsync(adminId, tenantId);
        await AssignRoleToUserAsync(adminId, adminRoleId, tenantId);

        var memberId = await SeedUserAsync($"kc-invmember-{suffix}", $"member-{suffix}@example.com");
        await SeedMembershipAsync(memberId, tenantId);

        return new Organization(tenantId, slug, adminId, Fixture.GenerateTestToken(adminId, tenantId),
            Fixture.GenerateTestToken(memberId, tenantId), editorRoleId, suffix);
    }

    private Task<HttpResponseMessage> CreateAsync(Organization org, string email, Guid roleId)
    {
        var request = CreateBearerRequest(HttpMethod.Post, "/api/invitations", org.AdminToken);
        request.Content = JsonContent.Create(new { email, roleId });
        return Client.SendAsync(request);
    }

    private async Task<Guid> CreateAndReadIdAsync(Organization org, string email)
    {
        var response = await CreateAsync(org, email, org.EditorRoleId);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return Guid.Parse(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("data").GetProperty("id").GetString()!);
    }

    private async Task SetExpiryAsync(Guid invitationId, DateTime expiresAt)
    {
        await using var context = Fixture.CreateAuthDbContext();
        await context.Invitations.Where(i => i.Id == invitationId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, expiresAt));
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
