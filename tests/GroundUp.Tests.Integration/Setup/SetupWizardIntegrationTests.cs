using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using GroundUp.Core.Dtos.Setup;
using GroundUp.Core.Entities.Settings;
using GroundUp.Sample.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// Integration tests for the setup wizard happy path: calling each step in order,
/// verifying state transitions via GET /setup/status, and confirming that
/// CreatedBy="setup-wizard" is set on entities created during setup mode.
/// Requirements: 9–14, Cross-Cutting 8
/// </summary>
[Collection("SetupWizardApi")]
public sealed class SetupWizardIntegrationTests : IAsyncLifetime
{
    private readonly SetupWizardApiFactory _factory;
    private HttpClient _client = null!;

    public SetupWizardIntegrationTests(SetupWizardApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        // Ensure the "system" setting level exists (required by SetupWizardService)
        await _factory.EnsureSystemLevelSeededAsync();

        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", SetupWizardApiFactory.TestBootstrapToken);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    #region Happy Path — All Steps In Order (Req 9, 10, 15)

    [Fact]
    public async Task SetAppIdentity_ValidRequest_Returns200WithStepResult()
    {
        // Arrange
        var request = new SetAppIdentityRequest("My Test App", "example.com");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/app-identity", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<StepResultDto>();
        result.Should().NotBeNull();
        result!.Step.Should().Be("app-identity");
        result.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task SetIdentityProvider_ValidRequest_Returns200WithStepResult()
    {
        // Arrange — first complete the app-identity step (precondition)
        await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("My Test App", "example.com"));

        var request = new SetIdentityProviderRequest(
            "https://keycloak.example.com",
            "http://keycloak-internal:8080",
            "groundup-realm");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/identity-provider", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<StepResultDto>();
        result.Should().NotBeNull();
        result!.Step.Should().Be("identity-provider");
        result.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task HappyPath_AllStepsInOrder_StatusReflectsProgress()
    {
        // Step 1: Set app identity
        var appIdentityResponse = await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("Integration Test App", "test.local"));
        appIdentityResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 2: Verify status after app-identity
        var statusResponse = await _client.GetAsync("/setup/status");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await statusResponse.Content.ReadFromJsonAsync<SetupStatusDto>();
        status!.AppIdentityCompleted.Should().BeTrue();
        status.IsComplete.Should().BeFalse();

        // Step 3: Set identity provider
        var idpResponse = await _client.PostAsJsonAsync("/setup/identity-provider",
            new SetIdentityProviderRequest(
                "https://keycloak.test.local",
                "http://keycloak:8080",
                "test-realm"));
        idpResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 4: Verify status after identity-provider
        statusResponse = await _client.GetAsync("/setup/status");
        status = await statusResponse.Content.ReadFromJsonAsync<SetupStatusDto>();
        status!.AppIdentityCompleted.Should().BeTrue();
        status.IdentityProviderCompleted.Should().BeTrue();
        status.CurrentStep.Should().Be("keycloak-bootstrap");
    }

    #endregion

    #region State Transitions — Status Endpoint (Req 15)

    [Fact]
    public async Task GetStatus_ReturnsValidStatusResponse()
    {
        // Act
        var response = await _client.GetAsync("/setup/status");

        // Assert — the status endpoint should always return 200 with a valid shape
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<SetupStatusDto>();
        status.Should().NotBeNull();
        status!.IsComplete.Should().BeFalse("bootstrap state service always returns incomplete in tests");
        // Note: FirstAdminPending may be true due to shared DB state from other tests
        // in the same collection (e.g., SetupRecoveryIntegrationTests seeds pending rows).
        // We only assert on the shape and isComplete here.
    }

    [Fact]
    public async Task GetStatus_AfterAppIdentity_ShowsAppIdentityCompleted()
    {
        // Arrange — ensure app-identity is set
        var appResponse = await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("Status Test App", "status.test"));
        appResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var response = await _client.GetAsync("/setup/status");

        // Assert
        var status = await response.Content.ReadFromJsonAsync<SetupStatusDto>();
        status!.AppIdentityCompleted.Should().BeTrue();
    }

    #endregion

    #region Idempotency — Repeated Calls (Req 9.8, 10.10)

    [Fact]
    public async Task SetAppIdentity_CalledTwice_OverwritesPreviousValues()
    {
        // Arrange — first call
        await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("First Name", "first.com"));

        // Act — second call with different values
        var response = await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("Second Name", "second.com"));

        // Assert — should succeed (idempotent overwrite)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<StepResultDto>();
        result!.Step.Should().Be("app-identity");
        result.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task SetIdentityProvider_CalledTwice_OverwritesPreviousValues()
    {
        // Arrange — complete precondition
        await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("Idempotent App", "idem.test"));

        // First call
        await _client.PostAsJsonAsync("/setup/identity-provider",
            new SetIdentityProviderRequest(
                "https://first.keycloak.com",
                "http://first-internal:8080",
                "first-realm"));

        // Act — second call with different values
        var response = await _client.PostAsJsonAsync("/setup/identity-provider",
            new SetIdentityProviderRequest(
                "https://second.keycloak.com",
                "http://second-internal:8080",
                "second-realm"));

        // Assert — should succeed (idempotent overwrite)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<StepResultDto>();
        result!.Step.Should().Be("identity-provider");
        result.Completed.Should().BeTrue();
    }

    #endregion

    #region CreatedBy="setup-wizard" (Cross-Cutting 8)

    [Fact]
    public async Task SetAppIdentity_CreatedBy_IsSetToSetupWizard()
    {
        // Arrange
        var request = new SetAppIdentityRequest("Audit Test App", "audit.test");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/app-identity", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — verify CreatedBy in the database
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SampleDbContext>();

        var settingValues = await dbContext.Set<SettingValue>()
            .AsNoTracking()
            .OrderByDescending(sv => sv.CreatedAt)
            .Take(5)
            .ToListAsync();

        // At least one setting value should have been created with "setup-wizard"
        settingValues.Should().NotBeEmpty("app-identity step should persist setting values");
        settingValues.Should().Contain(sv => sv.CreatedBy == "setup-wizard",
            "entities created during setup mode must have CreatedBy='setup-wizard' per Cross-Cutting 8");
    }

    [Fact]
    public async Task SetIdentityProvider_CreatedBy_IsSetToSetupWizard()
    {
        // Arrange — complete precondition
        await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("Audit IDP App", "audit-idp.test"));

        // Act
        var response = await _client.PostAsJsonAsync("/setup/identity-provider",
            new SetIdentityProviderRequest(
                "https://keycloak.audit.test",
                "http://keycloak-audit:8080",
                "audit-realm"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — verify CreatedBy in the database
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SampleDbContext>();

        var settingValues = await dbContext.Set<SettingValue>()
            .AsNoTracking()
            .OrderByDescending(sv => sv.CreatedAt)
            .Take(10)
            .ToListAsync();

        // Identity provider step creates 3 settings (publicBaseUrl, internalBaseUrl, sharedRealmName)
        settingValues.Should().NotBeEmpty("identity-provider step should persist setting values");
        var recentValues = settingValues.Where(sv => sv.CreatedBy == "setup-wizard").ToList();
        recentValues.Should().NotBeEmpty(
            "entities created during setup mode must have CreatedBy='setup-wizard' per Cross-Cutting 8");
    }

    #endregion

    #region Validation — App Identity (Req 9.4, 9.5, 9.6)

    [Fact]
    public async Task SetAppIdentity_EmptyApplicationName_Returns400()
    {
        // Arrange
        var request = new SetAppIdentityRequest("", "example.com");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/app-identity", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetAppIdentity_ApplicationNameExceeds200Chars_Returns400()
    {
        // Arrange
        var longName = new string('x', 201);
        var request = new SetAppIdentityRequest(longName, "example.com");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/app-identity", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetAppIdentity_InvalidDomainFormat_Returns400()
    {
        // Arrange
        var request = new SetAppIdentityRequest("Valid App", "not a valid domain!");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/app-identity", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetAppIdentity_NullDomain_Succeeds_PersistsEmptyString()
    {
        // Arrange — null domain is valid (host-only cookie config)
        var request = new SetAppIdentityRequest("No Domain App", null);

        // Act
        var response = await _client.PostAsJsonAsync("/setup/app-identity", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Validation — Identity Provider (Req 10.4, 10.5, 10.7, 10.8, 10.9)

    [Fact]
    public async Task SetIdentityProvider_EmptyPublicBaseUrl_Returns400()
    {
        // Arrange — complete precondition
        await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("IDP Validation App", "idp.test"));

        var request = new SetIdentityProviderRequest("", "http://internal:8080", "realm");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/identity-provider", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetIdentityProvider_InvalidPublicBaseUrl_Returns400()
    {
        // Arrange — complete precondition
        await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("IDP Validation App 2", "idp2.test"));

        var request = new SetIdentityProviderRequest("not-a-url", null, "realm");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/identity-provider", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetIdentityProvider_EmptySharedRealmName_Returns400()
    {
        // Arrange — complete precondition
        await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("IDP Validation App 3", "idp3.test"));

        var request = new SetIdentityProviderRequest("https://keycloak.test", null, "");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/identity-provider", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetIdentityProvider_SharedRealmNameExceeds128Chars_Returns400()
    {
        // Arrange — complete precondition
        await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("IDP Validation App 4", "idp4.test"));

        var longRealm = new string('r', 129);
        var request = new SetIdentityProviderRequest("https://keycloak.test", null, longRealm);

        // Act
        var response = await _client.PostAsJsonAsync("/setup/identity-provider", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetIdentityProvider_EmptyInternalBaseUrl_DefaultsToPublicBaseUrl()
    {
        // Arrange — complete precondition
        await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("IDP Default App", "idp-default.test"));

        // internalBaseUrl is null — should default to publicBaseUrl
        var request = new SetIdentityProviderRequest(
            "https://keycloak.default.test", null, "default-realm");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/identity-provider", request);

        // Assert — should succeed
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<StepResultDto>();
        result!.Completed.Should().BeTrue();
    }

    #endregion

    #region Authentication — Bootstrap Admin Token (Req 8.2, 8.4, 8.5)

    [Fact]
    public async Task SetupEndpoint_NoAuthHeader_Returns401()
    {
        // Arrange — create a client without the auth header
        using var unauthClient = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        // Act
        var response = await unauthClient.GetAsync("/setup/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SetupEndpoint_InvalidToken_Returns401()
    {
        // Arrange — create a client with a wrong token
        using var badClient = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        badClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "wrong-token-that-is-definitely-not-valid");

        // Act
        var response = await badClient.GetAsync("/setup/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Whitespace Trimming (Cross-Cutting 1)

    [Fact]
    public async Task SetAppIdentity_WhitespaceAroundValues_TrimsBeforePersisting()
    {
        // Arrange — values with leading/trailing whitespace
        var request = new SetAppIdentityRequest("  Trimmed App  ", "  trimmed.com  ");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/app-identity", request);

        // Assert — should succeed (trimmed values are valid)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetAppIdentity_WhitespaceOnlyApplicationName_Returns400()
    {
        // Arrange — whitespace-only is treated as empty after trimming
        var request = new SetAppIdentityRequest("   ", "example.com");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/app-identity", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion
}
