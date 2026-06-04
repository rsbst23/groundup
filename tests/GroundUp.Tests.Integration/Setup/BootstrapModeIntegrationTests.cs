using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// Integration tests for <see cref="GroundUp.Api.Middleware.BootstrapModeMiddleware"/>.
/// Verifies redirect behavior in setup mode, passthrough after setup complete,
/// and JSON 503 responses for API clients.
/// </summary>
[Collection("BootstrapModeApi")]
public sealed class BootstrapModeIntegrationTests : IAsyncLifetime
{
    private readonly BootstrapModeApiFactory _factory;
    private HttpClient _client = null!;

    public BootstrapModeIntegrationTests(BootstrapModeApiFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            // Don't follow redirects so we can assert on 302 responses
            AllowAutoRedirect = false
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    #region Setup Mode — Redirect Behavior (Req 7.4)

    [Fact]
    public async Task Request_NonAllowedPath_InSetupMode_Returns302RedirectToSetup()
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = false;

        // Act
        var response = await _client.GetAsync("/api/settings/levels");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/setup");
    }

    [Fact]
    public async Task Request_RootPath_InSetupMode_Returns302RedirectToSetup()
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = false;

        // Act
        var response = await _client.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/setup");
    }

    [Fact]
    public async Task Request_ArbitraryApiPath_InSetupMode_Returns302RedirectToSetup()
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = false;

        // Act
        var response = await _client.GetAsync("/api/customers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/setup");
    }

    #endregion

    #region Setup Mode — Allowed Paths Pass Through (Req 7.3)

    [Theory]
    [InlineData("/setup")]
    [InlineData("/setup/status")]
    [InlineData("/setup/app-identity")]
    public async Task Request_AllowedPath_InSetupMode_PassesThrough(string path)
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = false;

        // Act
        var response = await _client.GetAsync(path);

        // Assert — should NOT be a redirect; the actual status depends on the endpoint
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        response.StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/ready")]
    public async Task Request_HealthPath_InSetupMode_PassesThroughMiddleware(string path)
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = false;

        // Act
        var response = await _client.GetAsync(path);

        // Assert — should NOT be a redirect (302) which would indicate the middleware blocked it.
        // The health endpoint may return 503 (Unhealthy) due to infrastructure checks failing
        // in the test environment, but that's the health check itself responding — not the
        // bootstrap middleware blocking the request.
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect,
            "health endpoints should pass through the bootstrap middleware without redirect");
    }

    [Theory]
    [InlineData("/_framework/blazor.js")]
    [InlineData("/css/site.css")]
    [InlineData("/js/app.js")]
    [InlineData("/images/logo.png")]
    [InlineData("/lib/jquery/jquery.min.js")]
    public async Task Request_StaticAssetPath_InSetupMode_PassesThrough(string path)
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = false;

        // Act
        var response = await _client.GetAsync(path);

        // Assert — should NOT be a redirect (may be 404 since files don't exist, but not 302)
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        response.StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Request_SetupPathCaseInsensitive_InSetupMode_PassesThrough()
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = false;

        // Act
        var response = await _client.GetAsync("/Setup/Status");

        // Assert — case-insensitive match should pass through
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        response.StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable);
    }

    #endregion

    #region Post-Setup — Passthrough (Req 7.2)

    [Fact]
    public async Task Request_AnyPath_AfterSetupComplete_PassesThrough()
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = true;

        // Act
        var response = await _client.GetAsync("/api/settings/levels");

        // Assert — should NOT be a redirect; passes through to normal pipeline
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        response.StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Request_RootPath_AfterSetupComplete_PassesThrough()
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = true;

        // Act
        var response = await _client.GetAsync("/");

        // Assert — should NOT be a redirect
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Request_NonExistentPath_AfterSetupComplete_Returns404NotRedirect()
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = true;

        // Act
        var response = await _client.GetAsync("/api/nonexistent");

        // Assert — should be 404 (not found), not a redirect
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region JSON 503 Response (Req 7.6)

    [Fact]
    public async Task Request_NonAllowedPath_WithJsonAcceptHeader_InSetupMode_Returns503()
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = false;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings/levels");
        request.Headers.Add("Accept", "application/json");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<SetupRequiredResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("setup_required");
        body.Message.Should().Contain("setup");
    }

    [Fact]
    public async Task Request_NonAllowedPath_WithMixedAcceptHeader_InSetupMode_Returns503()
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = false;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        request.Headers.Add("Accept", "text/html, application/json, */*");

        // Act
        var response = await _client.SendAsync(request);

        // Assert — should still return 503 because application/json is in the Accept list
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<SetupRequiredResponse>();
        body!.Code.Should().Be("setup_required");
    }

    [Fact]
    public async Task Request_NonAllowedPath_WithHtmlAcceptHeader_InSetupMode_Returns302()
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = false;
        var request = new HttpRequestMessage(HttpMethod.Get, "/some-page");
        request.Headers.Add("Accept", "text/html");

        // Act
        var response = await _client.SendAsync(request);

        // Assert — browser-like client gets a redirect
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/setup");
    }

    [Fact]
    public async Task Request_NonAllowedPath_WithNoAcceptHeader_InSetupMode_Returns302()
    {
        // Arrange
        BootstrapModeApiFactory.IsSetupComplete = false;
        var request = new HttpRequestMessage(HttpMethod.Get, "/some-page");
        // No Accept header set

        // Act
        var response = await _client.SendAsync(request);

        // Assert — no Accept header means not a JSON client, gets redirect
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/setup");
    }

    #endregion

    #region Segment-Aware Path Matching

    [Fact]
    public async Task Request_SetupEvilPath_InSetupMode_IsNotAllowed()
    {
        // Arrange — /setup-evil should NOT match /setup (segment-aware matching)
        BootstrapModeApiFactory.IsSetupComplete = false;

        // Act
        var response = await _client.GetAsync("/setup-evil");

        // Assert — should be redirected because /setup-evil is not /setup or /setup/*
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.ServiceUnavailable);
    }

    #endregion

    /// <summary>
    /// DTO for deserializing the 503 JSON response body.
    /// </summary>
    private sealed record SetupRequiredResponse(string Code, string Message);
}
