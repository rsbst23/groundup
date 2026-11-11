using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for <c>POST /auth/organizations</c>: an authenticated user creates an
/// additional organization without an identity-provider round-trip, becomes its TenantAdmin,
/// and keeps their current session.
/// </summary>
[Collection("AuthFlow")]
public sealed class CreateOrganizationTests : AuthFlowTestBase
{
    public CreateOrganizationTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // Happy path
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrganization_AuthenticatedUser_Returns201AndCreatesTenantMembershipAndAdminRole()
    {
        // Arrange — a user who belongs to tenant A
        var (userId, currentTenantId, token, suffix) = await SeedUserInTenantAsync();
        var organizationName = $"Second Org {suffix}";

        // Act
        var response = await Client.SendAsync(CreateOrganizationRequest(token, organizationName));

        // Assert — 201 with the new org's details
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var newTenantId = Guid.Parse(body.GetProperty("id").GetString()!);
        body.GetProperty("name").GetString().Should().Be(organizationName);
        body.GetProperty("slug").GetString().Should().Be($"second-org-{suffix}");
        newTenantId.Should().NotBe(currentTenantId);

        await using var context = Fixture.CreateAuthDbContext();

        // Tenant exists as a standard tenant
        var tenant = await context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == newTenantId);
        tenant.Should().NotBeNull();
        tenant!.Name.Should().Be(organizationName);
        tenant.RealmName.Should().BeNull();

        // The user is an active member of the new tenant and still of the old one
        var memberships = await GetMembershipsForUserAsync(userId);
        memberships.Select(m => m.TenantId).Should().BeEquivalentTo(new[] { currentTenantId, newTenantId });

        // A TenantAdmin system role exists in the new tenant and is assigned to the user
        var adminRole = await context.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == newTenantId && r.Name == AuthRoleNames.TenantAdmin);
        adminRole.Should().NotBeNull();
        adminRole!.IsSystem.Should().BeTrue();

        var assignment = await context.UserRoles.AsNoTracking()
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == adminRole.Id && ur.TenantId == newTenantId);
        assignment.Should().NotBeNull("the creator must be the new organization's TenantAdmin");
    }

    [Fact]
    public async Task CreateOrganization_AuthenticatedUser_DoesNotChangeTheSession()
    {
        // Arrange
        var (_, _, token, suffix) = await SeedUserInTenantAsync();

        // Act
        var response = await Client.SendAsync(CreateOrganizationRequest(token, $"No Switch {suffix}"));

        // Assert — no new auth cookie: the session stays on the current tenant
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        ExtractAuthCookie(response).Should().BeNull();
    }

    [Fact]
    public async Task CreateOrganization_NewOrganization_AppearsInMyTenantsAsNotCurrent()
    {
        // Arrange
        var (_, currentTenantId, token, suffix) = await SeedUserInTenantAsync();
        var createResponse = await Client.SendAsync(CreateOrganizationRequest(token, $"Listed Org {suffix}"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var newTenantId = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        // Act — same token, still scoped to the original tenant
        var listResponse = await Client.SendAsync(CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me/tenants", token));

        // Assert
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenants = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray().ToList();
        tenants.Should().HaveCount(2);

        var created = tenants.Single(t => t.GetProperty("id").GetString() == newTenantId);
        created.GetProperty("current").GetBoolean().Should().BeFalse();

        var original = tenants.Single(t => t.GetProperty("id").GetString() == currentTenantId.ToString());
        original.GetProperty("current").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task CreateOrganization_SameNameTwice_SecondGetsDisambiguatedSlug()
    {
        // Arrange — exercises the slug retry against a real unique index
        var (_, _, token, suffix) = await SeedUserInTenantAsync();
        var organizationName = $"Twin Org {suffix}";

        // Act
        var first = await Client.SendAsync(CreateOrganizationRequest(token, organizationName));
        var second = await Client.SendAsync(CreateOrganizationRequest(token, organizationName));

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstSlug = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement.GetProperty("slug").GetString();
        var secondSlug = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement.GetProperty("slug").GetString();
        firstSlug.Should().Be($"twin-org-{suffix}");
        secondSlug.Should().Be($"twin-org-{suffix}-2");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Rejections
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrganization_Unauthenticated_Returns401()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/organizations")
        {
            Content = JsonBody("Anonymous Org")
        };

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateOrganization_TokenWithoutTenant_Returns401()
    {
        // Arrange — authenticated, but no tenant selected yet (no tid)
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = await SeedUserAsync($"kc-notid-{suffix}", $"notid-{suffix}@example.com");
        var token = Fixture.GenerateTestToken(userId);

        // Act
        var response = await Client.SendAsync(CreateOrganizationRequest(token, $"No Tid Org {suffix}"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetMembershipsForUserAsync(userId)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateOrganization_BlankName_Returns400(string organizationName)
    {
        // Arrange
        var (userId, _, token, _) = await SeedUserInTenantAsync();

        // Act
        var response = await Client.SendAsync(CreateOrganizationRequest(token, organizationName));

        // Assert — rejected, and nothing was created
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetMembershipsForUserAsync(userId)).Should().ContainSingle();
    }

    [Fact]
    public async Task CreateOrganization_NameTooLong_Returns400()
    {
        // Arrange
        var (_, _, token, _) = await SeedUserInTenantAsync();

        // Act
        var response = await Client.SendAsync(CreateOrganizationRequest(token, new string('x', 201)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a user with one active membership and returns a GroundUp token scoped to that tenant.
    /// </summary>
    private async Task<(Guid UserId, Guid TenantId, string Token, string Suffix)> SeedUserInTenantAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"Home Org {suffix}", $"home-org-{suffix}");
        var userId = await SeedUserAsync($"kc-creator-{suffix}", $"creator-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);
        return (userId, tenantId, Fixture.GenerateTestToken(userId, tenantId), suffix);
    }

    /// <summary>
    /// Bearer-authenticated so the POST is not subject to the antiforgery check.
    /// </summary>
    private HttpRequestMessage CreateOrganizationRequest(string token, string organizationName)
    {
        var request = CreateBearerRequest(HttpMethod.Post, "/auth/organizations", token);
        request.Content = JsonBody(organizationName);
        return request;
    }

    private static StringContent JsonBody(string organizationName) =>
        new(JsonSerializer.Serialize(new { organizationName }), Encoding.UTF8, "application/json");
}
