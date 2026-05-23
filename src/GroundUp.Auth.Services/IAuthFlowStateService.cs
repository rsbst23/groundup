using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services;

/// <summary>
/// Service interface for AuthFlowState lifecycle management.
/// Provides initiation, atomic consumption, and failure marking.
/// No flow logic — pure persistence orchestration.
/// </summary>
public interface IAuthFlowStateService
{
    /// <summary>
    /// Creates a new Pending AuthFlowState row and returns it with its server-generated Id.
    /// </summary>
    /// <param name="request">The flow initiation request (validated before persistence).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created AuthFlowState DTO with generated Id and ExpiresAt.</returns>
    Task<OperationResult<AuthFlowStateDto>> InitiateAsync(
        InitiateAuthFlowRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transitions a Pending row to Consumed and verifies the expected FlowType.
    /// </summary>
    /// <param name="id">The AuthFlowState identifier (OAuth state parameter value).</param>
    /// <param name="expectedFlowType">The expected flow type — mismatch returns failure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The consumed AuthFlowState DTO, or a failure result.</returns>
    Task<OperationResult<AuthFlowStateDto>> ConsumeAsync(
        Guid id, FlowType expectedFlowType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a Pending row as Failed with a reason string.
    /// </summary>
    /// <param name="id">The AuthFlowState identifier.</param>
    /// <param name="reason">The failure reason (must not be empty).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The failed AuthFlowState DTO, or a failure result.</returns>
    Task<OperationResult<AuthFlowStateDto>> MarkFailedAsync(
        Guid id, string reason, CancellationToken cancellationToken = default);
}
