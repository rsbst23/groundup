using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core.Enums;

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
