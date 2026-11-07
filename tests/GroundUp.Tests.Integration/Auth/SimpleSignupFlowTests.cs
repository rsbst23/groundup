using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for the SimpleSignup flow (FlowType.SimpleSignup) end-to-end.
/// Validates the full pipeline: callback → code exchange → user creation →
/// default-tenant lookup → membership creation → token issuance.
///
/// The SimpleSignup flow is initiated via <c>POST /auth/signup</c> with no
/// <c>organizationName</c> body, and completes when the user is auto-joined to
/// the configured <c>auth.application.default-tenant-slug</c> tenant.
/// </summary>
[Collection("AuthFlow")]
public sealed class SimpleSignupFlowTests : AuthFlowTestBase
{
    public SimpleSignupFlowTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // 1. Happy Path — new user joins default tenant
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_SimpleSignup_HappyPath_CreatesUserAndMembershipInDefaultTenant()
    {
        // Arrange — seed a default tenant and configure the setting that points to it
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantSlug = $"default-{suffix}";
        var tenantId = await SeedTenantAsync($"Default Org {suffix}", tenantSlug);
        await SeedDefaultTenantSlugSettingAsync(tenantSlug);

        var sub = $"kc-simple-{suffix}";
        var email = $"simple-{suffix}@example.com";
        var displayName = "Simple Signup User";
        var nonce = AuthFlowTestFixture.GenerateNonce();
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        var flowState = await CreatePendingFlowStateAsync(
            FlowType.SimpleSignup,
            stateToken: stateToken,
            nonce: nonce);

        SetupFullSuccessfulCallback(sub, nonce, email, displayName);

        var request = CreateCallbackRequest("auth-code-simple", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — HTTP 200 with token
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("token", out var tokenElement).Should().BeTrue(
            "a successful SimpleSignup should return a token in the response body");
        var tokenValue = tokenElement.GetString();
        tokenValue.Should().NotBeNullOrEmpty();

        // Assert — auth cookie is set
        var authCookie = ExtractAuthCookie(response);
        authCookie.Should().NotBeNullOrEmpty("auth cookie must be written on successful SimpleSignup");

        // Assert — user was created with the correct external sub
        var user = await GetUserByExternalIdAsync(sub);
        user.Should().NotBeNull();
        user!.Email.Should().Be(email);
        user.DisplayName.Should().Be(displayName);

        // Assert — user has an active membership in the default tenant
        var memberships = await GetMembershipsForUserAsync(user.Id);
        memberships.Should().ContainSingle(m => m.TenantId == tenantId,
            "user should have exactly one active membership in the default tenant");

        // Assert — JWT token contains tid scoped to the default tenant
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenValue);
        var tidClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tid");
        tidClaim.Should().NotBeNull("the token must contain a tid claim");
        tidClaim!.Value.Should().Be(tenantId.ToString(),
            "the tid must be scoped to the default tenant");
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. Idempotent — existing member skips membership creation
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_SimpleSignup_ExistingMember_SkipsMembershipCreation_ReturnsToken()
    {
        // Arrange — seed user who is already a member of the default tenant
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantSlug = $"default-idem-{suffix}";
        var tenantId = await SeedTenantAsync($"Default Idem Org {suffix}", tenantSlug);
        await SeedDefaultTenantSlugSettingAsync(tenantSlug);

        var sub = $"kc-idem-{suffix}";
        var email = $"idem-{suffix}@example.com";
        var userId = await SeedUserAsync(sub, email);
        await SeedMembershipAsync(userId, tenantId); // Already a member

        var nonce = AuthFlowTestFixture.GenerateNonce();
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        var flowState = await CreatePendingFlowStateAsync(
            FlowType.SimpleSignup,
            stateToken: stateToken,
            nonce: nonce);

        SetupFullSuccessfulCallback(sub, nonce, email);

        var request = CreateCallbackRequest("auth-code-idem", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — still succeeds (idempotent)
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("token", out var tokenElement).Should().BeTrue();
        tokenElement.GetString().Should().NotBeNullOrEmpty();

        // Assert — still only one membership (no duplicate created)
        var memberships = await GetMembershipsForUserAsync(userId);
        memberships.Should().ContainSingle(m => m.TenantId == tenantId,
            "no duplicate membership should be created for an existing member");

        // Assert — JWT contains the correct tid
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenElement.GetString()!);
        jwt.Claims.FirstOrDefault(c => c.Type == "tid")?.Value
            .Should().Be(tenantId.ToString());
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3. No default tenant configured → 400
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_SimpleSignup_NoDefaultTenantConfigured_ReturnsError()
    {
        // Arrange — do NOT seed a default-tenant-slug setting for this test
        // (the AuthFlowTestScopeChainProvider only resolves system-level settings,
        //  so if the setting value is missing the handler will fail gracefully)
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sub = $"kc-nodefault-{suffix}";
        var email = $"nodefault-{suffix}@example.com";

        var nonce = AuthFlowTestFixture.GenerateNonce();
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        var flowState = await CreatePendingFlowStateAsync(
            FlowType.SimpleSignup,
            stateToken: stateToken,
            nonce: nonce);

        // Code exchange and userinfo succeed — failure happens at the "read setting" step
        SetupFullSuccessfulCallback(sub, nonce, email);

        var request = CreateCallbackRequest("auth-code-nodefault", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — not a 200; some error status indicating no default tenant
        response.IsSuccessStatusCode.Should().BeFalse(
            "SimpleSignup must fail when no default-tenant-slug setting is configured");
    }

    // ────────────────────────────────────────────────────────────────────────
    // 4. Code exchange failure → non-200 response
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_SimpleSignup_CodeExchangeFailure_ReturnsError()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        await CreatePendingFlowStateAsync(
            FlowType.SimpleSignup,
            stateToken: stateToken);

        SetupFailedCodeExchange();

        var request = CreateCallbackRequest("invalid-code-simple", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — not a success response
        response.IsSuccessStatusCode.Should().BeFalse(
            "a code exchange failure must return an error response");
    }

    // ────────────────────────────────────────────────────────────────────────
    // 5. Signup endpoint routing — POST /auth/signup with no body → SimpleSignup
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signup_NullOrganizationName_Returns200WithRedirectUrl()
    {
        // Arrange — POST /auth/signup with no body (simulates client-side simple signup)
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/signup");
        // No body → organizationName is null → FlowType.SimpleSignup

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 with redirectUrl pointing to Keycloak
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("redirectUrl", out var redirectUrlElement).Should().BeTrue(
            "POST /auth/signup with no organizationName should return a redirectUrl");
        redirectUrlElement.GetString().Should().NotBeNullOrEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 6. Signup endpoint routing — POST /auth/signup with organizationName → NewOrganization
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signup_WithOrganizationName_Returns200WithRedirectUrl()
    {
        // Arrange — POST /auth/signup with an organizationName body
        var requestBody = new StringContent(
            """{"organizationName":"Acme Corp"}""",
            System.Text.Encoding.UTF8,
            "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/signup")
        {
            Content = requestBody
        };

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 with redirectUrl (NewOrganization flow initiated)
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("redirectUrl", out var redirectUrlElement).Should().BeTrue(
            "POST /auth/signup with organizationName should return a redirectUrl for NewOrganization flow");
        redirectUrlElement.GetString().Should().NotBeNullOrEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helper: seed the auth.application.default-tenant-slug setting in the
    // database so the SimpleSignupFlowHandler can resolve the default tenant.
    // Uses the same SampleDbContext + settings infrastructure as the fixture.
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds the <c>auth.application.default-tenant-slug</c> setting in the database
    /// so the <see cref="SimpleSignupFlowHandler"/> can resolve the default tenant.
    /// </summary>
    private async Task SeedDefaultTenantSlugSettingAsync(string tenantSlug)
    {
        await using var dbContext = Fixture.CreateSampleDbContext();

        // Ensure a System level exists (may already exist from domain setting seeding)
        var systemLevel = await dbContext.Set<GroundUp.Core.Entities.Settings.SettingLevel>()
            .FirstOrDefaultAsync(l => l.Name == "System");

        if (systemLevel is null)
        {
            systemLevel = new GroundUp.Core.Entities.Settings.SettingLevel
            {
                Name = "System",
                ParentId = null,
                DisplayOrder = 0,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Set<GroundUp.Core.Entities.Settings.SettingLevel>().Add(systemLevel);
            await dbContext.SaveChangesAsync();
        }

        // Ensure the definition exists
        const string settingKey = "auth.application.default-tenant-slug";
        var definition = await dbContext.Set<GroundUp.Core.Entities.Settings.SettingDefinition>()
            .FirstOrDefaultAsync(d => d.Key == settingKey);

        if (definition is null)
        {
            definition = new GroundUp.Core.Entities.Settings.SettingDefinition
            {
                Key = settingKey,
                DataType = GroundUp.Core.Enums.SettingDataType.String,
                DefaultValue = "",
                DisplayName = "Default Tenant Slug",
                Description = "Slug of the default tenant users are auto-joined to on SimpleSignup",
                DisplayOrder = 0,
                IsVisible = true,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Set<GroundUp.Core.Entities.Settings.SettingDefinition>().Add(definition);

            var defLevel = new GroundUp.Core.Entities.Settings.SettingDefinitionLevel
            {
                SettingDefinitionId = definition.Id,
                SettingLevelId = systemLevel.Id
            };
            dbContext.Set<GroundUp.Core.Entities.Settings.SettingDefinitionLevel>().Add(defLevel);
            await dbContext.SaveChangesAsync();
        }

        // Upsert the value at system level (no scope = system-wide)
        var existingValue = await dbContext.Set<GroundUp.Core.Entities.Settings.SettingValue>()
            .FirstOrDefaultAsync(v =>
                v.SettingDefinitionId == definition.Id &&
                v.LevelId == systemLevel.Id &&
                v.ScopeId == null);

        if (existingValue is null)
        {
            var settingValue = new GroundUp.Core.Entities.Settings.SettingValue
            {
                SettingDefinitionId = definition.Id,
                LevelId = systemLevel.Id,
                ScopeId = null,
                Value = tenantSlug,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Set<GroundUp.Core.Entities.Settings.SettingValue>().Add(settingValue);
        }
        else
        {
            existingValue.Value = tenantSlug;
            dbContext.Set<GroundUp.Core.Entities.Settings.SettingValue>().Update(existingValue);
        }

        await dbContext.SaveChangesAsync();
    }
}
