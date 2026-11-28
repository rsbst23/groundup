using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-98: sorting, filtering, search and paging on <c>/api/members</c>, <c>/api/invitations</c> and
/// <c>/api/join-links</c> against Postgres, through the shared list pipeline and each list's field map.
/// </summary>
[Collection("AuthFlow")]
public sealed class AdminListsApiTests : AdminApiTestBase
{
    public AdminListsApiTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ── Members ─────────────────────────────────────────────

    private sealed record MemberOrg(Organization Org, Guid CarolId, Guid DaveId);

    /// <summary>
    /// The admin and the member ("Test User"), Carol ("Zed"), Dave (Pending, "Alpha") and Erin (Inactive,
    /// never listed).
    /// </summary>
    private async Task<MemberOrg> SeedMembersAsync()
    {
        var org = await SeedOrganizationAsync();
        var s = org.Suffix;
        var carol = await SeedUserAsync($"kc-carol-{s}", $"carol-{s}@example.com", "Zed");
        await SeedMembershipAsync(carol, org.TenantId);
        var dave = await SeedUserAsync($"kc-dave-{s}", $"dave-{s}@example.com", "Alpha");
        await SeedMembershipAsync(dave, org.TenantId);
        var erin = await SeedUserAsync($"kc-erin-{s}", $"erin-{s}@example.com", "Erin");
        await SeedMembershipAsync(erin, org.TenantId, isActive: false);

        await using var context = Fixture.CreateAuthDbContext();
        await context.Set<UserTenant>()
            .Where(ut => ut.UserId == dave && ut.TenantId == org.TenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(ut => ut.Status, MembershipStatus.Pending));
        return new MemberOrg(org, carol, dave);
    }

    private async Task<List<string>> MemberEmailsAsync(MemberOrg m, string query)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/members?{query}", m.Org.AdminToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Items(await DataAsync(response)).Select(i => i.GetProperty("email").GetString()!).ToList();
    }

    private static string Email(Organization org, string name) => $"{name}-{org.Suffix}@example.com";

    [Fact]
    public async Task Members_NoSort_ByEmail()
    {
        var m = await SeedMembersAsync();

        var emails = await MemberEmailsAsync(m, "pageSize=50");

        emails.Should().Equal(Email(m.Org, "aadmin"), Email(m.Org, "amember"), Email(m.Org, "carol"), Email(m.Org, "dave"));
    }

    [Fact]
    public async Task Members_SortByEmailDescAndDisplayName()
    {
        var m = await SeedMembersAsync();

        var byEmailDesc = await MemberEmailsAsync(m, "sortBy=email%20desc");
        var byName = await MemberEmailsAsync(m, "sortBy=DISPLAYNAME");

        byEmailDesc.Should().Equal(Email(m.Org, "dave"), Email(m.Org, "carol"), Email(m.Org, "amember"), Email(m.Org, "aadmin"));
        byName.First().Should().Be(Email(m.Org, "dave"));   // Alpha
        byName.Last().Should().Be(Email(m.Org, "carol"));   // Zed
    }

    [Fact]
    public async Task Members_SortByStatus_ActiveBeforePending()
    {
        var m = await SeedMembersAsync();

        var emails = await MemberEmailsAsync(m, "sortBy=status%20desc,email");

        emails.First().Should().Be(Email(m.Org, "dave"));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("2")]
    public async Task Members_FilterByStatus_NumberOrName(string status)
    {
        var m = await SeedMembersAsync();

        var emails = await MemberEmailsAsync(m, $"filters[status]={status}");

        emails.Should().Equal(Email(m.Org, "dave"));
    }

    [Fact]
    public async Task Members_FilterByInactive_Empty()
    {
        var m = await SeedMembersAsync();

        var emails = await MemberEmailsAsync(m, "filters[status]=Inactive");

        emails.Should().BeEmpty();
    }

    [Fact]
    public async Task Members_Search_EmailAndDisplayName()
    {
        var m = await SeedMembersAsync();

        var byDisplayName = await MemberEmailsAsync(m, "searchTerm=ZE");
        var byEmail = await MemberEmailsAsync(m, $"searchTerm=CAROL-{m.Org.Suffix}");

        byDisplayName.Should().Equal(Email(m.Org, "carol"));
        byEmail.Should().Equal(Email(m.Org, "carol"));
    }

    [Fact]
    public async Task Members_UnknownFields_Ignored()
    {
        var m = await SeedMembersAsync();

        var emails = await MemberEmailsAsync(m, $"sortBy=ExternalUserId%20desc&filters[TenantId]={Guid.NewGuid()}&containsFilters[ExternalUserId]=zzz");

        emails.Should().HaveCount(4);
        emails.First().Should().Be(Email(m.Org, "aadmin"));
    }

    [Fact]
    public async Task Members_PagingAcrossTies_NoOverlap()
    {
        var m = await SeedMembersAsync();

        // The admin and the member share the display name "Test User"
        var pages = new List<string>();
        for (var page = 1; page <= 4; page++)
        {
            pages.AddRange(await MemberEmailsAsync(m, $"sortBy=displayName&pageSize=1&pageNumber={page}"));
        }

        pages.Should().OnlyHaveUniqueItems().And.HaveCount(4);
    }

    [Fact]
    public async Task Members_OtherTenant_NotListed()
    {
        var m = await SeedMembersAsync();
        var other = await SeedOrganizationAsync();

        var emails = await MemberEmailsAsync(m, "searchTerm=example.com&pageSize=50");

        emails.Should().HaveCount(4).And.NotContain(Email(other, "aadmin"));
    }

    // ── Invitations ─────────────────────────────────────────

    private sealed record InvitationOrg(Organization Org, Guid AlphaRoleId, Guid BetaRoleId);

    /// <summary>b (Beta, Pending), a (Alpha, Pending past expiry = Expired), c (Alpha, Revoked), in that order.</summary>
    private async Task<InvitationOrg> SeedInvitationsAsync()
    {
        var org = await SeedOrganizationAsync();
        var alpha = await SeedRoleAsync(org.TenantId, "Alpha role");
        var beta = await SeedRoleAsync(org.TenantId, "Beta role");
        await SeedInvitationAsync(org, "b", beta, InvitationStatus.Pending, DateTime.UtcNow.AddDays(7));
        await SeedInvitationAsync(org, "a", alpha, InvitationStatus.Pending, DateTime.UtcNow.AddDays(-1));
        await SeedInvitationAsync(org, "c", alpha, InvitationStatus.Revoked, DateTime.UtcNow.AddDays(7));
        return new InvitationOrg(org, alpha, beta);
    }

    private async Task SeedInvitationAsync(Organization org, string name, Guid roleId, InvitationStatus status, DateTime expiresAt)
    {
        var email = Email(org, name);
        await using var context = Fixture.CreateAuthDbContext();
        context.Invitations.Add(new Invitation
        {
            TenantId = org.TenantId,
            Email = email,
            NormalizedEmail = email,
            RoleId = roleId,
            UserId = org.MemberId,
            CreatedByUserId = org.AdminId,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = expiresAt,
            Status = status,
            CreatedAt = NextCreatedAt()
        });
        await context.SaveChangesAsync();
    }

    private async Task<List<JsonElement>> InvitationsAsync(Organization org, string query)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/invitations?{query}", org.AdminToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Items(await DataAsync(response));
    }

    private async Task<List<string>> InvitationNamesAsync(Organization org, string query)
        => (await InvitationsAsync(org, query)).Select(i => i.GetProperty("email").GetString()!.Split('-')[0]).ToList();

    [Fact]
    public async Task Invitations_NoSort_NewestFirst()
    {
        var i = await SeedInvitationsAsync();

        (await InvitationNamesAsync(i.Org, "")).Should().Equal("c", "a", "b");
    }

    [Fact]
    public async Task Invitations_SortByEmailBothWays()
    {
        var i = await SeedInvitationsAsync();

        (await InvitationNamesAsync(i.Org, "sortBy=email")).Should().Equal("a", "b", "c");
        (await InvitationNamesAsync(i.Org, "sortBy=email%20desc")).Should().Equal("c", "b", "a");
    }

    [Fact]
    public async Task Invitations_SortByRoleName_UsesTheRoleLookup()
    {
        var i = await SeedInvitationsAsync();

        (await InvitationNamesAsync(i.Org, "sortBy=roleName,email")).Should().Equal("a", "c", "b");
        (await InvitationNamesAsync(i.Org, "sortBy=roleName%20desc,email")).Should().Equal("b", "a", "c");
    }

    [Fact]
    public async Task Invitations_SortByStatus_UsesTheShownStatus()
    {
        var i = await SeedInvitationsAsync();

        // Pending (0) b, Revoked (2) c, Expired (3) a: "a" is stored as Pending
        (await InvitationNamesAsync(i.Org, "sortBy=status")).Should().Equal("b", "c", "a");
    }

    [Fact]
    public async Task Invitations_SortByExpiresAtAndCreatedAt()
    {
        var i = await SeedInvitationsAsync();

        (await InvitationNamesAsync(i.Org, "sortBy=expiresAt,email")).First().Should().Be("a");
        (await InvitationNamesAsync(i.Org, "sortBy=createdAt")).Should().Equal("b", "a", "c");
    }

    [Fact]
    public async Task Invitations_FilterByStatus_UsesTheShownStatus()
    {
        var i = await SeedInvitationsAsync();

        var expired = await InvitationsAsync(i.Org, "filters[status]=Expired");
        var pending = await InvitationNamesAsync(i.Org, "filters[status]=0");

        expired.Should().ContainSingle();
        expired[0].GetProperty("email").GetString().Should().Be(Email(i.Org, "a"));
        expired[0].GetProperty("status").GetInt32().Should().Be((int)InvitationStatus.Expired);
        pending.Should().Equal("b");
    }

    [Fact]
    public async Task Invitations_FilterByEveryStatus_RowsShowThatStatus()
    {
        var i = await SeedInvitationsAsync();

        foreach (var status in Enum.GetValues<InvitationStatus>())
        {
            var rows = await InvitationsAsync(i.Org, $"filters[status]={status}");
            rows.All(r => r.GetProperty("status").GetInt32() == (int)status)
                .Should().BeTrue($"every row filtered by {status} shows {status}");
        }
    }

    [Fact]
    public async Task Invitations_FilterByRoleIdAndSearchEmail()
    {
        var i = await SeedInvitationsAsync();

        (await InvitationNamesAsync(i.Org, $"filters[roleId]={i.AlphaRoleId}&sortBy=email")).Should().Equal("a", "c");
        (await InvitationNamesAsync(i.Org, $"searchTerm=B-{i.Org.Suffix}")).Should().Equal("b");
        // The role name is not a search field for invitations
        (await InvitationNamesAsync(i.Org, "searchTerm=Alpha%20role")).Should().BeEmpty();
    }

    [Fact]
    public async Task Invitations_UnknownFields_Ignored()
    {
        var i = await SeedInvitationsAsync();

        (await InvitationNamesAsync(i.Org, "sortBy=TokenHash&filters[TokenHash]=x&filters[Email]=nobody"))
            .Should().Equal("c", "a", "b");
    }

    [Fact]
    public async Task Invitations_PagingAcrossTies_NoOverlap()
    {
        var i = await SeedInvitationsAsync();

        var pages = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            pages.AddRange(await InvitationNamesAsync(i.Org, $"sortBy=roleName&pageSize=1&pageNumber={page}"));
        }

        pages.Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    [Fact]
    public async Task Invitations_OtherTenant_NotListed()
    {
        var i = await SeedInvitationsAsync();
        var other = await SeedOrganizationAsync();
        var otherRole = await SeedRoleAsync(other.TenantId, "Alpha role");
        await SeedInvitationAsync(other, "a", otherRole, InvitationStatus.Pending, DateTime.UtcNow.AddDays(-1));

        var expired = await InvitationsAsync(i.Org, "filters[status]=Expired");

        expired.Should().ContainSingle();
    }

    // ── Join links ──────────────────────────────────────────

    private sealed record JoinLinkOrg(Organization Org, Guid Open, Guid Expired, Guid UsedUp, Guid Revoked);

    /// <summary>In order: Open (Beta), Expired (Alpha), UsedUp (Alpha, 2 of 2), Revoked (Beta).</summary>
    private async Task<JoinLinkOrg> SeedJoinLinksAsync()
    {
        var org = await SeedOrganizationAsync();
        var alpha = await SeedRoleAsync(org.TenantId, "Alpha role");
        var beta = await SeedRoleAsync(org.TenantId, "Beta role");
        var open = await SeedJoinLinkAsync(org, beta, JoinLinkStatus.Active, expiresAt: null, maxUses: null, useCount: 1);
        var expired = await SeedJoinLinkAsync(org, alpha, JoinLinkStatus.Active, DateTime.UtcNow.AddDays(-1), maxUses: null, useCount: 0);
        var usedUp = await SeedJoinLinkAsync(org, alpha, JoinLinkStatus.Active, expiresAt: null, maxUses: 2, useCount: 2);
        var revoked = await SeedJoinLinkAsync(org, beta, JoinLinkStatus.Revoked, expiresAt: null, maxUses: null, useCount: 0);
        return new JoinLinkOrg(org, open, expired, usedUp, revoked);
    }

    private async Task<Guid> SeedJoinLinkAsync(
        Organization org, Guid roleId, JoinLinkStatus status, DateTime? expiresAt, int? maxUses, int useCount)
    {
        await using var context = Fixture.CreateAuthDbContext();
        var link = new JoinLink
        {
            TenantId = org.TenantId,
            RoleId = roleId,
            CreatedByUserId = org.AdminId,
            TokenHash = Guid.NewGuid().ToString("N"),
            Status = status,
            ExpiresAt = expiresAt,
            MaxUses = maxUses,
            UseCount = useCount,
            CreatedAt = NextCreatedAt()
        };
        context.JoinLinks.Add(link);
        await context.SaveChangesAsync();
        return link.Id;
    }

    private async Task<List<Guid>> JoinLinkIdsAsync(Organization org, string query)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/join-links?{query}", org.AdminToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Items(await DataAsync(response)).Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    [Fact]
    public async Task JoinLinks_NoSort_NewestFirst()
    {
        var j = await SeedJoinLinksAsync();

        (await JoinLinkIdsAsync(j.Org, "")).Should().Equal(j.Revoked, j.UsedUp, j.Expired, j.Open);
    }

    [Theory]
    [InlineData("Expired")]
    [InlineData("2")]
    public async Task JoinLinks_FilterByExpired_UsesTheShownStatus(string status)
    {
        var j = await SeedJoinLinksAsync();

        (await JoinLinkIdsAsync(j.Org, $"filters[status]={status}")).Should().Equal(j.Expired);
    }

    [Fact]
    public async Task JoinLinks_FilterByUsedUpAndActive()
    {
        var j = await SeedJoinLinksAsync();

        (await JoinLinkIdsAsync(j.Org, "filters[status]=usedup")).Should().Equal(j.UsedUp);
        (await JoinLinkIdsAsync(j.Org, "filters[status]=Active")).Should().Equal(j.Open);
        (await JoinLinkIdsAsync(j.Org, "multiValueFilters[status]=Revoked&multiValueFilters[status]=Expired"))
            .Should().Equal(j.Revoked, j.Expired);
    }

    [Fact]
    public async Task JoinLinks_SortByStatusUseCountRoleNameAndDates()
    {
        var j = await SeedJoinLinksAsync();

        // Active (0) Open, Revoked (1), Expired (2), UsedUp (3)
        (await JoinLinkIdsAsync(j.Org, "sortBy=status")).Should().Equal(j.Open, j.Revoked, j.Expired, j.UsedUp);
        (await JoinLinkIdsAsync(j.Org, "sortBy=useCount%20desc")).First().Should().Be(j.UsedUp);
        (await JoinLinkIdsAsync(j.Org, "sortBy=roleName%20desc,createdAt")).Should().Equal(j.Open, j.Revoked, j.Expired, j.UsedUp);
        (await JoinLinkIdsAsync(j.Org, "sortBy=createdAt")).Should().Equal(j.Open, j.Expired, j.UsedUp, j.Revoked);
        (await JoinLinkIdsAsync(j.Org, "sortBy=expiresAt&filters[status]=Expired")).Should().Equal(j.Expired);
    }

    [Fact]
    public async Task JoinLinks_SearchRoleNameAndFilterRoleId()
    {
        var j = await SeedJoinLinksAsync();
        var betaRoleId = await RoleIdAsync(j.Org, "Beta role");

        (await JoinLinkIdsAsync(j.Org, "searchTerm=ALP&sortBy=createdAt")).Should().Equal(j.Expired, j.UsedUp);
        (await JoinLinkIdsAsync(j.Org, $"filters[roleId]={betaRoleId}&sortBy=createdAt")).Should().Equal(j.Open, j.Revoked);
    }

    [Fact]
    public async Task JoinLinks_UnknownFieldsIgnoredAndOtherTenantNotListed()
    {
        var j = await SeedJoinLinksAsync();
        var other = await SeedOrganizationAsync();
        var otherRole = await SeedRoleAsync(other.TenantId, "Alpha role");
        await SeedJoinLinkAsync(other, otherRole, JoinLinkStatus.Active, DateTime.UtcNow.AddDays(-1), maxUses: null, useCount: 0);

        (await JoinLinkIdsAsync(j.Org, "sortBy=TokenHash&filters[MaxUses]=2")).Should().Equal(j.Revoked, j.UsedUp, j.Expired, j.Open);
        (await JoinLinkIdsAsync(j.Org, "filters[status]=Expired")).Should().Equal(j.Expired);
    }

    // ── Helpers ─────────────────────────────────────────────

    private DateTime _createdAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Rows seeded directly skip the audit interceptor, so each gets an explicit, increasing CreatedAt.
    /// </summary>
    private DateTime NextCreatedAt() => _createdAt = _createdAt.AddMinutes(1);

    private static List<JsonElement> Items(JsonElement data) => data.GetProperty("items").EnumerateArray().ToList();

    private async Task<Guid> RoleIdAsync(Organization org, string name)
    {
        await using var context = Fixture.CreateAuthDbContext();
        return await context.Roles.Where(r => r.TenantId == org.TenantId && r.Name == name).Select(r => r.Id).SingleAsync();
    }
}
