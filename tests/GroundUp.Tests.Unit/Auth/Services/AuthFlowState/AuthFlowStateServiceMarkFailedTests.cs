using FluentAssertions;
using FluentValidation;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services.AuthFlowState;

public sealed class AuthFlowStateServiceMarkFailedTests
{
    private readonly IAuthFlowStateRepository _repository;
    private readonly AuthFlowStateService _sut;

    public AuthFlowStateServiceMarkFailedTests()
    {
        _repository = Substitute.For<IAuthFlowStateRepository>();
        var validator = Substitute.For<IValidator<InitiateAuthFlowRequest>>();
        _sut = new AuthFlowStateService(_repository, validator);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task MarkFailedAsync_EmptyOrWhitespaceReason_ReturnsBadRequest(string? reason)
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var result = await _sut.MarkFailedAsync(id, reason!);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("reason");
    }

    [Fact]
    public async Task MarkFailedAsync_ValidReason_DelegatesToRepoAndSurfacesResult()
    {
        // Arrange
        var id = Guid.NewGuid();
        var reason = "Token exchange failed";
        var failedDto = new AuthFlowStateDto(
            Id: id,
            FlowType: FlowType.NewOrganization,
            Status: FlowStatus.Failed,
            TenantId: null,
            InvitationId: null,
            JoinLinkId: null,
            Realm: null,
            ReturnUrl: null,
            Nonce: "test-nonce",
            CreatedByIp: "127.0.0.1",
            CreatedByUserAgent: "TestAgent/1.0",
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            ConsumedAt: null,
            TerminatedAt: DateTime.UtcNow,
            FailureReason: reason,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow);

        _repository.MarkFailedAsync(id, reason, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(failedDto));

        // Act
        var result = await _sut.MarkFailedAsync(id, reason);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(failedDto);
        result.Data!.Status.Should().Be(FlowStatus.Failed);
        result.Data.FailureReason.Should().Be(reason);
    }

    [Fact]
    public async Task MarkFailedAsync_RepoReturnsConflict_SurfacesUnchanged()
    {
        // Arrange
        var id = Guid.NewGuid();
        var reason = "Some failure";
        _repository.MarkFailedAsync(id, reason, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Fail("Already terminal", 409));

        // Act
        var result = await _sut.MarkFailedAsync(id, reason);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }
}
