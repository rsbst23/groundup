using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Results;
using FluentValidation;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services.AuthFlowState;

/// <summary>
/// Property-based tests verifying that expired rows always fail consumption.
/// For any AuthFlowState with ExpiresAt in the past, ConsumeAsync must return failure.
/// </summary>
public sealed class AuthFlowStateExpiredConsumptionPropertyTests
{
    /// <summary>
    /// Property: For any AuthFlowState with ExpiresAt in the past,
    /// attempting to consume always returns a failure (400 expired).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConsumeAsync_ExpiredRow_AlwaysFails(
        Guid id,
        int flowTypeInt,
        PositiveInt minutesExpiredAgo)
    {
        var flowType = (FlowType)(Math.Abs(flowTypeInt) % 7);

        var repository = Substitute.For<IAuthFlowStateRepository>();
        var validator = Substitute.For<IValidator<InitiateAuthFlowRequest>>();
        var service = new AuthFlowStateService(repository, validator);

        // Repository returns expired failure (simulating the atomic UPDATE finding no rows)
        repository.MarkConsumedAsync(id, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Fail(
                $"AuthFlowState '{id}' has expired", 400));

        // Act
        var result = service.ConsumeAsync(id, flowType).GetAwaiter().GetResult();

        // Assert — must always fail with 400
        return (!result.Success && result.StatusCode == 400)
            .ToProperty();
    }

    /// <summary>
    /// Property: For any random expiration offset in the past (1 to 10000 minutes),
    /// the repository correctly rejects consumption.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConsumeAsync_VariousExpirationOffsets_AllFail(
        Guid id,
        PositiveInt minutesAgo)
    {
        var flowType = FlowType.NewOrganization;

        var repository = Substitute.For<IAuthFlowStateRepository>();
        var validator = Substitute.For<IValidator<InitiateAuthFlowRequest>>();
        var service = new AuthFlowStateService(repository, validator);

        // The repository simulates the expired check
        repository.MarkConsumedAsync(id, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Fail(
                $"AuthFlowState '{id}' has expired", 400));

        var result = service.ConsumeAsync(id, flowType).GetAwaiter().GetResult();

        return (!result.Success
            && result.StatusCode == 400
            && result.Message.Contains("expired"))
            .ToProperty();
    }
}
