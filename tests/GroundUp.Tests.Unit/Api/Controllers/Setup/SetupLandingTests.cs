using FluentAssertions;
using GroundUp.Api.Controllers.Setup;
using GroundUp.Core.Abstractions;
using GroundUp.Services.Setup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace GroundUp.Tests.Unit.Api.Controllers.Setup;

/// <summary>
/// Unit tests for <see cref="SetupController.GetSetupLanding"/>.
/// Validates: 200 in setup mode, 404 after complete, Cache-Control: no-store header.
/// Requirements: 15.1
/// </summary>
public sealed class SetupLandingTests
{
    private readonly IBootstrapStateService _bootstrapService = Substitute.For<IBootstrapStateService>();
    private readonly ISetupWizardService _wizardService = Substitute.For<ISetupWizardService>();
    private readonly SetupController _controller;

    public SetupLandingTests()
    {
        _controller = new SetupController(_wizardService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task GetSetupLanding_SetupIncomplete_Returns200()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await _controller.GetSetupLanding(_bootstrapService, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetSetupLanding_SetupIncomplete_ReturnsSetupActiveBody()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await _controller.GetSetupLanding(_bootstrapService, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = okResult.Value;
        body.Should().NotBeNull();

        // Verify the anonymous object has the expected properties
        var codeProperty = body!.GetType().GetProperty("code");
        var messageProperty = body.GetType().GetProperty("message");

        codeProperty.Should().NotBeNull();
        messageProperty.Should().NotBeNull();

        codeProperty!.GetValue(body).Should().Be("setup_active");
        messageProperty!.GetValue(body).Should().Be(
            "Setup mode active. Authenticate with the bootstrap admin token and call GET /setup/status to inspect wizard state.");
    }

    [Fact]
    public async Task GetSetupLanding_SetupComplete_Returns404()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _controller.GetSetupLanding(_bootstrapService, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetSetupLanding_SetupIncomplete_SetsCacheControlNoStore()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        await _controller.GetSetupLanding(_bootstrapService, CancellationToken.None);

        // Assert
        _controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task GetSetupLanding_SetupComplete_SetsCacheControlNoStore()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        await _controller.GetSetupLanding(_bootstrapService, CancellationToken.None);

        // Assert
        // Cache-Control is set before the IsComplete check, so it should always be present
        _controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }
}
