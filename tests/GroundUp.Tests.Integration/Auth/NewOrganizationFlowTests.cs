using System.Net;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for the NewOrganization flow end-to-end:
/// browser → callback → code exchange → tenant creation → user creation → token issuance.
/// Exercises the full middleware pipeline via the Sample app WebApplicationFactory
/// with a real Postgres database and mocked Keycloak responses.
/// </summary>
[Collection("AuthFlow")]
public sealed class NewOrganizationFlowTests : AuthFlowTestBase
{
    public NewOrganizationFlowTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // 1. Happy Path — NewOrganization creates tenant, user, membership, role
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_NewOrganization_HappyPath_CreatesFullTenantAndUser()
    {
        // Arrange
        var sub = $"kc-user-{Guid.NewGuid():N}";
        var nonce = AuthFlowTestFixture.GenerateNonce();
        var stateToken = AuthFlowTestFixture.GenerateStateToken();
        var email = "founder@acme-test.com";
        var displayName = "Founder User";
        const string organizationName = "Acme Corp";
        const string expectedSlug = "acme-corp";

        var flowState = await CreatePendingFlowStateAsync(
            FlowType.NewOrganization,
            stateToken: stateToken,
            nonce: nonce,
            organizationName: organizationName);

        SetupFullSuccessfulCallback(sub, nonce, email, displayName);

        var request = CreateCallbackRequest("auth-code-happy", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — HTTP response is a redirect
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var redirectUrl = ParseRedirectResponse(response);
        redirectUrl.Should().NotBeNullOrEmpty();

        // Assert — Tenant exists with derived slug
        var tenant = await GetTenantBySlugAsync(expectedSlug);
        tenant.Should().NotBeNull();
        tenant!.Name.Should().Be(organizationName);
        tenant.TenantType.Should().Be(TenantType.Standard);
        tenant.IsActive.Should().BeTrue();

        // Assert — User exists matched by external sub
        var user = await GetUserByExternalIdAsync(sub);
        user.Should().NotBeNull();
        user!.Email.Should().Be(email);
        user.DisplayName.Should().Be(displayName);

        // Assert — UserTenant membership links user to new tenant
        var memberships = await GetMembershipsForUserAsync(user.Id);
        memberships.Should().ContainSingle(m => m.TenantId == tenant.Id);

        // Assert — TenantAdmin role exists in new tenant (IsSystem=true)
        await using var context = Fixture.CreateAuthDbContext();
        var tenantAdminRole = await context.Roles
            .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Name == AuthRoleNames.TenantAdmin);
        tenantAdminRole.Should().NotBeNull();
        tenantAdminRole!.IsSystem.Should().BeTrue();

        // Assert — User has TenantAdmin role assigned
        var userRole = await context.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == user.Id && ur.RoleId == tenantAdminRole.Id && ur.TenantId == tenant.Id);
        userRole.Should().NotBeNull();

        // Assert — Auth cookie is set in the response
        var authCookie = ExtractAuthCookie(response);
        authCookie.Should().NotBeNullOrEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. Code Exchange Failure
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_NewOrganization_CodeExchangeFailure_ReturnsErrorAndMarksFlowFailed()
    {
        // Arrange
        var stateToken = AuthFlowTestFixture.GenerateStateToken();
        var nonce = AuthFlowTestFixture.GenerateNonce();

        var flowState = await CreatePendingFlowStateAsync(
            FlowType.NewOrganization,
            stateToken: stateToken,
            nonce: nonce,
            organizationName: "CodeFail Corp");

        SetupFailedCodeExchange();

        var request = CreateCallbackRequest("invalid-code", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — Error response (not a redirect)
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        response.IsSuccessStatusCode.Should().BeFalse();

        // Assert — AuthFlowState was consumed by the dispatcher (state transitions to Consumed
        // before the handler runs; MarkFailedAsync cannot transition from Consumed → Failed)
        var updatedState = await GetFlowStateByStateTokenAsync(stateToken);
        updatedState.Should().NotBeNull();
        updatedState!.Status.Should().Be(FlowStatus.Consumed);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3. Nonce Mismatch
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_NewOrganization_NonceMismatch_ReturnsError()
    {
        // Arrange
        var sub = $"kc-user-{Guid.NewGuid():N}";
        var storedNonce = AuthFlowTestFixture.GenerateNonce();
        var wrongNonce = AuthFlowTestFixture.GenerateNonce(); // Different nonce in the id_token
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        var flowState = await CreatePendingFlowStateAsync(
            FlowType.NewOrganization,
            stateToken: stateToken,
            nonce: storedNonce, // Stored nonce
            organizationName: "Nonce Corp");

        // Setup code exchange to return id_token with WRONG nonce
        SetupSuccessfulCodeExchange(sub, wrongNonce); // id_token has wrongNonce
        SetupSuccessfulUserInfo(sub);

        var request = CreateCallbackRequest("auth-code-nonce", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — Error response (nonce validation fails)
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        response.IsSuccessStatusCode.Should().BeFalse();

        // Assert — Flow was consumed by the dispatcher (nonce failure happens after consumption)
        var updatedState = await GetFlowStateByStateTokenAsync(stateToken);
        updatedState.Should().NotBeNull();
        updatedState!.Status.Should().Be(FlowStatus.Consumed);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 4. Slug Collision — Disambiguated Slug
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_NewOrganization_SlugCollision_CreatesDisambiguatedSlug()
    {
        // Arrange — seed a tenant that already occupies the "acme-corp" slug
        await SeedTenantAsync("Existing Acme", "acme-corp");

        var sub = $"kc-user-{Guid.NewGuid():N}";
        var nonce = AuthFlowTestFixture.GenerateNonce();
        var stateToken = AuthFlowTestFixture.GenerateStateToken();
        var email = "founder@acme-collision.com";
        var displayName = "Collision User";
        const string organizationName = "Acme Corp";

        var flowState = await CreatePendingFlowStateAsync(
            FlowType.NewOrganization,
            stateToken: stateToken,
            nonce: nonce,
            organizationName: organizationName);

        SetupFullSuccessfulCallback(sub, nonce, email, displayName);

        var request = CreateCallbackRequest("auth-code-collision", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — Redirect (success)
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // Assert — New tenant gets a disambiguated slug (e.g., "acme-corp-2")
        var disambiguatedTenant = await GetTenantBySlugAsync("acme-corp-2");
        disambiguatedTenant.Should().NotBeNull();
        disambiguatedTenant!.Name.Should().Be(organizationName);

        // Assert — Auth cookie is set
        var authCookie = ExtractAuthCookie(response);
        authCookie.Should().NotBeNullOrEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 5. Replay Attack — Consumed State Returns 410 Gone
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_NewOrganization_ReplayAttack_Returns410Gone()
    {
        // Arrange — create an already-consumed AuthFlowState
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        var flowState = await CreateConsumedFlowStateAsync(
            FlowType.NewOrganization,
            stateToken: stateToken);

        var request = CreateCallbackRequest("auth-code-replay", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 410 Gone
        response.StatusCode.Should().Be(HttpStatusCode.Gone);
    }
}
