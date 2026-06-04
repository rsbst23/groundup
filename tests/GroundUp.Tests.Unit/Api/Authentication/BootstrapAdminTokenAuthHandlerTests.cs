using System.Text.Encodings.Web;
using GroundUp.Api.Authentication;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Api.Authentication;

/// <summary>
/// Unit tests for <see cref="BootstrapAdminTokenAuthenticationHandler"/>.
/// Validates token authentication, setup-complete rejection, and claim assignment.
/// Requirements: 8.1–8.9
/// </summary>
public sealed class BootstrapAdminTokenAuthHandlerTests
{
    private const string ValidToken = "a-valid-bootstrap-token-that-is-long-enough";

    private readonly IBootstrapStateService _bootstrapService = Substitute.For<IBootstrapStateService>();

    private async Task<AuthenticateResult> AuthenticateAsync(
        string? authorizationHeader,
        bool setupComplete = false,
        string? configuredToken = ValidToken)
    {
        // Build configuration
        var configData = new Dictionary<string, string?>();
        if (configuredToken is not null)
        {
            configData["GroundUp:BootstrapAdminToken"] = configuredToken;
        }
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Setup bootstrap state
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(setupComplete);

        // Build service provider
        var services = new ServiceCollection();
        services.AddSingleton<IBootstrapStateService>(_bootstrapService);
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        // Create HttpContext
        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        if (authorizationHeader is not null)
        {
            context.Request.Headers.Authorization = authorizationHeader;
        }

        // Create the handler
        var optionsMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Get(BootstrapAdminTokenAuthenticationHandler.SchemeName)
            .Returns(new AuthenticationSchemeOptions());

        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        var handler = new BootstrapAdminTokenAuthenticationHandler(
            optionsMonitor,
            loggerFactory,
            UrlEncoder.Default,
            configuration);

        // Initialize the handler with a scheme
        var scheme = new AuthenticationScheme(
            BootstrapAdminTokenAuthenticationHandler.SchemeName,
            displayName: null,
            handlerType: typeof(BootstrapAdminTokenAuthenticationHandler));

        await handler.InitializeAsync(scheme, context);

        return await handler.AuthenticateAsync();
    }

    #region Setup Complete — Rejection

    [Fact]
    public async Task HandleAuthenticateAsync_SetupComplete_FailsWithSetupCompleteMessage()
    {
        // Act
        var result = await AuthenticateAsync(
            authorizationHeader: $"Bearer {ValidToken}",
            setupComplete: true);

        // Assert
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Contains("Setup is already complete", result.Failure.Message);
    }

    #endregion

    #region Missing Authorization Header

    [Fact]
    public async Task HandleAuthenticateAsync_MissingAuthHeader_FailsWithMissingHeaderMessage()
    {
        // Act
        var result = await AuthenticateAsync(
            authorizationHeader: null,
            setupComplete: false);

        // Assert
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Contains("Missing Authorization header", result.Failure.Message);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_EmptyAuthHeader_FailsWithMissingHeaderMessage()
    {
        // Act
        var result = await AuthenticateAsync(
            authorizationHeader: "",
            setupComplete: false);

        // Assert
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Contains("Missing Authorization header", result.Failure.Message);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_NonBearerScheme_FailsWithMissingHeaderMessage()
    {
        // Act — "Basic" scheme should not be accepted
        var result = await AuthenticateAsync(
            authorizationHeader: "Basic dXNlcjpwYXNz",
            setupComplete: false);

        // Assert
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Contains("Missing Authorization header", result.Failure.Message);
    }

    #endregion

    #region Invalid Token

    [Fact]
    public async Task HandleAuthenticateAsync_InvalidToken_FailsWithInvalidTokenMessage()
    {
        // Act
        var result = await AuthenticateAsync(
            authorizationHeader: "Bearer wrong-token-value",
            setupComplete: false);

        // Assert
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Contains("Invalid bootstrap admin token", result.Failure.Message);
    }

    #endregion

    #region Valid Token — Success

    [Fact]
    public async Task HandleAuthenticateAsync_ValidToken_SetupIncomplete_Succeeds()
    {
        // Act
        var result = await AuthenticateAsync(
            authorizationHeader: $"Bearer {ValidToken}",
            setupComplete: false);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Principal);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ValidToken_HasBootstrapAdminClaim()
    {
        // Act
        var result = await AuthenticateAsync(
            authorizationHeader: $"Bearer {ValidToken}",
            setupComplete: false);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Principal);

        var claim = result.Principal.FindFirst(BootstrapAdminTokenAuthenticationHandler.ClaimType);
        Assert.NotNull(claim);
        Assert.Equal("true", claim.Value);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ValidToken_IdentityAuthenticationType_IsScheme()
    {
        // Act
        var result = await AuthenticateAsync(
            authorizationHeader: $"Bearer {ValidToken}",
            setupComplete: false);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Principal?.Identity);
        Assert.Equal(BootstrapAdminTokenAuthenticationHandler.SchemeName,
            result.Principal.Identity.AuthenticationType);
    }

    #endregion

    #region Token Not Configured

    [Fact]
    public async Task HandleAuthenticateAsync_TokenNotConfigured_FailsWithNotConfiguredMessage()
    {
        // Act
        var result = await AuthenticateAsync(
            authorizationHeader: $"Bearer {ValidToken}",
            setupComplete: false,
            configuredToken: null);

        // Assert
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Contains("not configured", result.Failure.Message);
    }

    #endregion
}
