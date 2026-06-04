using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using GroundUp.Core.Dtos.Setup;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// Integration tests for wizard step ordering enforcement.
/// Verifies: skipping steps returns 412 PreconditionFailed;
/// repeating completed steps returns idempotent success.
/// Requirements: 16.1–16.5
/// </summary>
[Collection("SetupWizardApi")]
public sealed class SetupStepOrderingTests : IAsyncLifetime
{
    private readonly SetupWizardApiFactory _factory;
    private HttpClient _client = null!;

    public SetupStepOrderingTests(SetupWizardApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
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

    #region Skip Steps → 412 (Req 16.1)

    [Fact]
    public async Task SetIdentityProvider_BeforeAppIdentity_Returns412()
    {
        // Arrange — skip app-identity step entirely
        var request = new SetIdentityProviderRequest(
            "https://keycloak.example.com",
            "http://keycloak-internal:8080",
            "test-realm");

        // Act
        var response = await _client.PostAsJsonAsync("/setup/identity-provider", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("precondition_step_missing");
        body.Message.Should().Contain("App identity");
    }

    #endregion

    #region Skip Steps → 412 (Req 16.4)

    [Fact]
    public async Task CompleteSetup_BeforeAllStepsDone_Returns412()
    {
        // Arrange — only complete app-identity, skip subsequent steps
        await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("Ordering Test App", "ordering.test"));

        // Act — try to complete setup without all steps
        var response = await _client.PostAsync("/setup/complete", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("precondition_step_missing");
        body.Message.Should().Contain("Identity provider");
    }

    [Fact]
    public async Task CompleteSetup_WithNoStepsDone_Returns412()
    {
        // Act — try to complete setup without all steps done
        // Note: other tests in this collection may have completed some steps,
        // so we just verify 412 with precondition_step_missing code.
        var response = await _client.PostAsync("/setup/complete", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("precondition_step_missing");
    }

    #endregion

    #region Repeat Steps → Idempotent (Req 9.8, 10.10)

    [Fact]
    public async Task SetAppIdentity_RepeatedWithSameData_ReturnsIdempotentSuccess()
    {
        // Arrange
        var request = new SetAppIdentityRequest("Idempotent App", "idempotent.test");

        // Act — call twice with the same data
        var response1 = await _client.PostAsJsonAsync("/setup/app-identity", request);
        var response2 = await _client.PostAsJsonAsync("/setup/app-identity", request);

        // Assert — both succeed
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var result1 = await response1.Content.ReadFromJsonAsync<StepResultDto>();
        var result2 = await response2.Content.ReadFromJsonAsync<StepResultDto>();

        result1!.Step.Should().Be("app-identity");
        result1.Completed.Should().BeTrue();
        result2!.Step.Should().Be("app-identity");
        result2.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task SetIdentityProvider_RepeatedWithSameData_ReturnsIdempotentSuccess()
    {
        // Arrange — complete precondition
        await _client.PostAsJsonAsync("/setup/app-identity",
            new SetAppIdentityRequest("IDP Repeat App", "idp-repeat.test"));

        var request = new SetIdentityProviderRequest(
            "https://keycloak.repeat.test",
            "http://keycloak-repeat:8080",
            "repeat-realm");

        // Act — call twice with the same data
        var response1 = await _client.PostAsJsonAsync("/setup/identity-provider", request);
        var response2 = await _client.PostAsJsonAsync("/setup/identity-provider", request);

        // Assert — both succeed
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var result1 = await response1.Content.ReadFromJsonAsync<StepResultDto>();
        var result2 = await response2.Content.ReadFromJsonAsync<StepResultDto>();

        result1!.Step.Should().Be("identity-provider");
        result1.Completed.Should().BeTrue();
        result2!.Step.Should().Be("identity-provider");
        result2.Completed.Should().BeTrue();
    }

    #endregion

    #region Response DTO

    private sealed record ErrorResponse(string Code, string Message);

    #endregion
}
