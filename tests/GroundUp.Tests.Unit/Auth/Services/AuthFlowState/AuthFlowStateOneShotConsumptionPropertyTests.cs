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
/// Property-based tests verifying one-shot consumption semantics:
/// for any valid AuthFlowState, consuming twice sequentially yields exactly one success.
/// </summary>
public sealed class AuthFlowStateOneShotConsumptionPropertyTests
{
    /// <summary>
    /// Property: For any valid AuthFlowState, consuming the same ID twice sequentially
    /// always yields exactly one success and one failure (conflict).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConsumeAsync_TwiceSequentially_ExactlyOneSucceeds(
        Guid id,
        int flowTypeInt)
    {
        var flowType = (FlowType)(Math.Abs(flowTypeInt) % 7);

        var repository = Substitute.For<IAuthFlowStateRepository>();
        var validator = Substitute.For<IValidator<InitiateAuthFlowRequest>>();
        var service = new AuthFlowStateService(repository, validator);

        var consumedDto = new AuthFlowStateDto(
            Id: id,
            FlowType: flowType,
            Status: FlowStatus.Consumed,
            TenantId: null,
            InvitationId: null,
            JoinLinkId: null,
            Realm: null,
            ReturnUrl: null,
            Nonce: "nonce",
            CreatedByIp: null,
            CreatedByUserAgent: null,
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            ConsumedAt: DateTime.UtcNow,
            TerminatedAt: DateTime.UtcNow,
            FailureReason: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow);

        // First call succeeds, second returns conflict
        var callCount = 0;
        repository.MarkConsumedAsync(id, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var count = Interlocked.Increment(ref callCount);
                return count == 1
                    ? Task.FromResult(OperationResult<AuthFlowStateDto>.Ok(consumedDto))
                    : Task.FromResult(OperationResult<AuthFlowStateDto>.Fail(
                        "Already consumed", 409));
            });

        var result1 = service.ConsumeAsync(id, flowType).GetAwaiter().GetResult();
        var result2 = service.ConsumeAsync(id, flowType).GetAwaiter().GetResult();

        var exactlyOneSuccess = result1.Success != result2.Success
            || (result1.Success && !result2.Success);

        return (result1.Success && !result2.Success && result2.StatusCode == 409)
            .ToProperty();
    }
}
