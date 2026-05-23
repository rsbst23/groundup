using FluentValidation;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services;

/// <summary>
/// Service for AuthFlowState lifecycle management. Validates input,
/// delegates persistence to the repository, and enforces FlowType matching on consumption.
/// </summary>
public sealed class AuthFlowStateService : IAuthFlowStateService
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    private readonly IAuthFlowStateRepository _repository;
    private readonly IValidator<InitiateAuthFlowRequest> _validator;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthFlowStateService"/>.
    /// </summary>
    /// <param name="repository">The AuthFlowState repository.</param>
    /// <param name="validator">The FluentValidation validator for initiation requests.</param>
    public AuthFlowStateService(
        IAuthFlowStateRepository repository,
        IValidator<InitiateAuthFlowRequest> validator)
    {
        _repository = repository;
        _validator = validator;
    }

    /// <inheritdoc />
    public async Task<OperationResult<AuthFlowStateDto>> InitiateAsync(
        InitiateAuthFlowRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => e.ErrorMessage)
                .ToList();
            return OperationResult<AuthFlowStateDto>.BadRequest(
                string.Join("; ", errors));
        }

        var lifetime = request.Lifetime ?? DefaultLifetime;
        var now = DateTime.UtcNow;

        var dto = new AuthFlowStateDto(
            Id: Guid.Empty,
            FlowType: request.FlowType,
            Status: FlowStatus.Pending,
            TenantId: request.TenantId,
            InvitationId: request.InvitationId,
            JoinLinkId: request.JoinLinkId,
            Realm: request.Realm,
            ReturnUrl: request.ReturnUrl,
            Nonce: request.Nonce,
            CreatedByIp: request.CreatedByIp,
            CreatedByUserAgent: request.CreatedByUserAgent,
            ExpiresAt: now + lifetime,
            ConsumedAt: null,
            TerminatedAt: null,
            FailureReason: null,
            CreatedAt: now,
            UpdatedAt: null);

        return await _repository.AddAsync(dto, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<AuthFlowStateDto>> ConsumeAsync(
        Guid id, FlowType expectedFlowType, CancellationToken cancellationToken = default)
    {
        var result = await _repository.MarkConsumedAsync(id, cancellationToken);

        if (!result.Success)
        {
            return result;
        }

        if (result.Data!.FlowType != expectedFlowType)
        {
            return OperationResult<AuthFlowStateDto>.Fail(
                $"FlowType mismatch: expected '{expectedFlowType}' but found '{result.Data.FlowType}'",
                400);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<OperationResult<AuthFlowStateDto>> MarkFailedAsync(
        Guid id, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult<AuthFlowStateDto>.BadRequest(
                "Failure reason must not be empty.");
        }

        return await _repository.MarkFailedAsync(id, reason, cancellationToken);
    }
}
