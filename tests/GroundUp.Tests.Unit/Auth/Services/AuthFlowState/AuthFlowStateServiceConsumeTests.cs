using FluentAssertions;
using FluentValidation;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services.AuthFlowState;

public sealed class AuthFlowStateServiceConsumeTests
{
    private readonly IAuthFlowStateRepository _repository;
    private readonly AuthFlowStateService _sut;

    public AuthFlowStateServiceConsumeTests()
    {
        _repository = Substitute.For<IAuthFlowStateRepository>();
        var validator = Substitute.For<IValidator<InitiateAuthFlowRequest>>();
        _sut = new AuthFlowStateService(_repository, validator);
    }

    [Fact]
    public async Task ConsumeAsync_HappyPath_ReturnsConsumedDto()
    {
        // Arrange
        var id = Guid.NewGuid();
        var consumedDto = CreateDto(id, FlowType.NewOrganization, FlowStatus.Consumed);
        _repository.MarkConsumedAsync(id, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(consumedDto));

        // Act
        var result = await _sut.ConsumeAsync(id, FlowType.NewOrganization);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(consumedDto);
        result.Data!.Status.Should().Be(FlowStatus.Consumed);
    }

    [Fact]
    public async Task ConsumeAsync_FlowTypeMismatch_ReturnsFailure400()
    {
        // Arrange
        var id = Guid.NewGuid();
        var consumedDto = CreateDto(id, FlowType.Invitation, FlowStatus.Consumed);
        _repository.MarkConsumedAsync(id, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(consumedDto));

        // Act
        var result = await _sut.ConsumeAsync(id, FlowType.NewOrganization);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("FlowType mismatch");
    }

    [Fact]
    public async Task ConsumeAsync_NotFoundFromRepo_SurfacesUnchanged()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.MarkConsumedAsync(id, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.NotFound("Not found"));

        // Act
        var result = await _sut.ConsumeAsync(id, FlowType.NewOrganization);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ConsumeAsync_ConflictFromRepo_SurfacesUnchanged()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.MarkConsumedAsync(id, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Fail("Already consumed", 409));

        // Act
        var result = await _sut.ConsumeAsync(id, FlowType.NewOrganization);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    private static AuthFlowStateDto CreateDto(Guid id, FlowType flowType, FlowStatus status) =>
        new(
            Id: id,
            FlowType: flowType,
            Status: status,
            TenantId: null,
            InvitationId: null,
            JoinLinkId: null,
            Realm: null,
            ReturnUrl: null,
            StateToken: "test-state-token",
            CodeVerifier: "test-code-verifier-with-enough-chars-1234567",
            RedirectUri: "https://example.com/auth/callback",
            OrganizationName: null,
            Nonce: "test-nonce",
            CreatedByIp: "127.0.0.1",
            CreatedByUserAgent: "TestAgent/1.0",
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            ConsumedAt: status == FlowStatus.Consumed ? DateTime.UtcNow : null,
            TerminatedAt: null,
            FailureReason: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null);
}
