using FluentValidation;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Events;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Policies;

/// <summary>
/// Default <see cref="IPolicyService"/>. Permissions are checked by the authorization proxy; this
/// class checks the current tenant, the input, the permission keys, the escalation guard and policy
/// usage, bumps the tenant's permission cache version after changes, and publishes events.
/// </summary>
internal sealed class PolicyService : IPolicyService
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IPolicyRepository _policyRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly PermissionHoldGuard _holdGuard;
    private readonly PermissionCacheVersions _cacheVersions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreatePolicyDto> _createValidator;
    private readonly IValidator<UpdatePolicyDto> _updateValidator;
    private readonly IValidator<SetPolicyPermissionsDto> _setPermissionsValidator;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<PolicyService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="PolicyService"/>. The event bus is optional: without
    /// one no events are published (the cache version is bumped either way).
    /// </summary>
    public PolicyService(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IPolicyRepository policyRepository,
        IPermissionRepository permissionRepository,
        PermissionHoldGuard holdGuard,
        PermissionCacheVersions cacheVersions,
        IUnitOfWork unitOfWork,
        IValidator<CreatePolicyDto> createValidator,
        IValidator<UpdatePolicyDto> updateValidator,
        IValidator<SetPolicyPermissionsDto> setPermissionsValidator,
        ILogger<PolicyService> logger,
        IEventBus? eventBus = null)
    {
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _policyRepository = policyRepository;
        _permissionRepository = permissionRepository;
        _holdGuard = holdGuard;
        _cacheVersions = cacheVersions;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _setPermissionsValidator = setPermissionsValidator;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<PermissionDto>>> ListCatalogAsync(
        FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filterParams);
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return OperationResult<PaginatedData<PermissionDto>>.Fail(
                "A tenant must be selected to view permissions.", 403, ErrorCodes.TenantRequired);
        }

        return await _permissionRepository.ListCatalogAsync(filterParams, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<List<string>>> GetModulesAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return OperationResult<List<string>>.Fail(
                "A tenant must be selected to view permissions.", 403, ErrorCodes.TenantRequired);
        }

        return await _permissionRepository.ListModulesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<TenantPolicyDto>>> ListAsync(
        FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filterParams);

        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<PaginatedData<TenantPolicyDto>>.Fail(
                "A tenant must be selected to list policies.", 403, ErrorCodes.TenantRequired);
        }

        if (tenantId == AuthRoleNames.SystemTenantId)
        {
            return OperationResult<PaginatedData<TenantPolicyDto>>.Ok(new PaginatedData<TenantPolicyDto>
            {
                Items = [],
                PageNumber = filterParams.PageNumber,
                PageSize = filterParams.PageSize,
                TotalRecords = 0
            });
        }

        return await _policyRepository.ListAsync(filterParams, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<PolicyDetailDto>> GetAsync(Guid policyId, CancellationToken cancellationToken = default)
        => FindDetailAsync(policyId, "view policies", cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<PolicyDetailDto>> CreateAsync(CreatePolicyDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = CheckWritableTenant("create policies");
        if (!tenant.Success)
        {
            return OperationResult<PolicyDetailDto>.Fail(tenant.Message, tenant.StatusCode, tenant.ErrorCode);
        }

        request = request with { Name = Trim(request.Name), Description = TrimToNull(request.Description) };
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return OperationResult<PolicyDetailDto>.BadRequest(
                validation.Errors[0].ErrorMessage, validation.Errors.Select(e => e.ErrorMessage).ToList());
        }

        var permissions = await ResolveKeysAsync(request.PermissionKeys, cancellationToken);
        if (!permissions.Success)
        {
            return OperationResult<PolicyDetailDto>.Fail(permissions.Message, permissions.StatusCode, permissions.ErrorCode, permissions.Errors);
        }

        var held = await _holdGuard.CheckHoldsAsync(_currentUser.UserId, permissions.Data!.Select(p => p.Key), cancellationToken);
        if (!held.Success)
        {
            return OperationResult<PolicyDetailDto>.Fail(held.Message, held.StatusCode, held.ErrorCode, held.Errors);
        }

        PolicyDto? created = null;
        PolicyPermissionChangesDto? links = null;
        OperationResult transaction;
        try
        {
            transaction = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var added = await _policyRepository.AddAsync(
                    new PolicyDto(Guid.Empty, request.Name, request.Description, tenant.Data), ct);
                if (!added.Success || added.Data is null)
                {
                    throw new AbortException(added.StatusCode == 409
                        ? NameTaken(request.Name)
                        : OperationResult.Fail(added.Message, added.StatusCode, added.ErrorCode));
                }

                created = added.Data;
                if (permissions.Data.Count == 0)
                {
                    return;
                }

                var replaced = await _policyRepository.ReplacePermissionsAsync(created.Id, permissions.Data.Select(p => p.Id).ToList(), ct);
                if (!replaced.Success || replaced.Data is null)
                {
                    throw new AbortException(OperationResult.Fail(replaced.Message, replaced.StatusCode, replaced.ErrorCode));
                }

                links = replaced.Data;
            }, cancellationToken);
        }
        catch (AbortException abort)
        {
            return OperationResult<PolicyDetailDto>.Fail(abort.Result.Message, abort.Result.StatusCode, abort.Result.ErrorCode, abort.Result.Errors);
        }

        if (!transaction.Success || created is null)
        {
            return transaction.StatusCode == 409
                ? FailFrom<PolicyDetailDto>(NameTaken(request.Name))
                : OperationResult<PolicyDetailDto>.Fail(transaction.Message, transaction.StatusCode, transaction.ErrorCode);
        }

        // A new policy is used by no role yet, so nobody's permissions changed; no cache bump needed
        await PublishAsync(new EntityCreatedEvent<PolicyDto> { Entity = created, TenantId = tenant.Data, UserId = _currentUser.UserId }, cancellationToken);
        await PublishLinkEventsAsync(links, tenant.Data, cancellationToken);
        _logger.LogInformation("Policy {PolicyId} created in tenant {TenantId} by {CallerId}", created.Id, tenant.Data, _currentUser.UserId);

        var detail = await _policyRepository.GetDetailAsync(created.Id, cancellationToken);
        return detail.Success && detail.Data is not null
            ? OperationResult<PolicyDetailDto>.Ok(detail.Data, "Created", 201)
            : OperationResult<PolicyDetailDto>.Fail(detail.Message, detail.StatusCode, detail.ErrorCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<PolicyDetailDto>> UpdateAsync(
        Guid policyId,
        UpdatePolicyDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await FindDetailAsync(policyId, "update policies", cancellationToken);
        if (!existing.Success)
        {
            return existing;
        }

        request = request with { Name = Trim(request.Name), Description = TrimToNull(request.Description) };
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return OperationResult<PolicyDetailDto>.BadRequest(
                validation.Errors[0].ErrorMessage, validation.Errors.Select(e => e.ErrorMessage).ToList());
        }

        var tenantId = _tenantContext.TenantId;
        var updated = await _policyRepository.UpdateAsync(
            policyId, new PolicyDto(policyId, request.Name, request.Description, tenantId), cancellationToken);
        if (!updated.Success || updated.Data is null)
        {
            return updated.StatusCode == 409
                ? FailFrom<PolicyDetailDto>(NameTaken(request.Name))
                : OperationResult<PolicyDetailDto>.Fail(updated.Message, updated.StatusCode, updated.ErrorCode);
        }

        await PublishAsync(new EntityUpdatedEvent<PolicyDto> { Entity = updated.Data, TenantId = tenantId, UserId = _currentUser.UserId }, cancellationToken);
        _logger.LogInformation("Policy {PolicyId} updated in tenant {TenantId} by {CallerId}", policyId, tenantId, _currentUser.UserId);
        return OperationResult<PolicyDetailDto>.Ok(existing.Data! with { Name = updated.Data.Name, Description = updated.Data.Description });
    }

    /// <inheritdoc />
    public async Task<OperationResult<PolicyDetailDto>> SetPermissionsAsync(
        Guid policyId,
        SetPolicyPermissionsDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await FindDetailAsync(policyId, "change policies", cancellationToken);
        if (!existing.Success)
        {
            return existing;
        }

        var validation = await _setPermissionsValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return OperationResult<PolicyDetailDto>.BadRequest(
                validation.Errors[0].ErrorMessage, validation.Errors.Select(e => e.ErrorMessage).ToList());
        }

        var permissions = await ResolveKeysAsync(request.PermissionKeys, cancellationToken);
        if (!permissions.Success)
        {
            return OperationResult<PolicyDetailDto>.Fail(permissions.Message, permissions.StatusCode, permissions.ErrorCode, permissions.Errors);
        }

        // Only what is added grants anything new; keeping or removing a key is never an escalation
        var current = existing.Data!.Permissions.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var addedKeys = permissions.Data!.Select(p => p.Key).Where(k => !current.Contains(k));
        var held = await _holdGuard.CheckHoldsAsync(_currentUser.UserId, addedKeys, cancellationToken);
        if (!held.Success)
        {
            return OperationResult<PolicyDetailDto>.Fail(held.Message, held.StatusCode, held.ErrorCode, held.Errors);
        }

        var tenantId = _tenantContext.TenantId;
        var replaced = await _policyRepository.ReplacePermissionsAsync(policyId, permissions.Data.Select(p => p.Id).ToList(), cancellationToken);
        if (!replaced.Success || replaced.Data is null)
        {
            return OperationResult<PolicyDetailDto>.Fail(replaced.Message, replaced.StatusCode, replaced.ErrorCode);
        }

        if (replaced.Data.HasChanges)
        {
            _cacheVersions.Bump(tenantId);
            await PublishLinkEventsAsync(replaced.Data, tenantId, cancellationToken);
            _logger.LogInformation(
                "Permissions of policy {PolicyId} in tenant {TenantId} changed by {CallerId}: {Added} added, {Removed} removed",
                policyId, tenantId, _currentUser.UserId, replaced.Data.Added.Count, replaced.Data.Removed.Count);
        }

        return OperationResult<PolicyDetailDto>.Ok(existing.Data with { Permissions = permissions.Data });
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        var existing = await FindDetailAsync(policyId, "delete policies", cancellationToken);
        if (!existing.Success)
        {
            return OperationResult.Fail(existing.Message, existing.StatusCode, existing.ErrorCode);
        }

        var policy = existing.Data!;
        if (policy.Roles.Count > 0)
        {
            return OperationResult.Fail(
                $"The policy '{policy.Name}' is used by roles and cannot be deleted. Detach it from them first.",
                409, AuthErrorCodes.PolicyInUse,
                policy.Roles.Select(r => $"Used by role '{r.Name}'.").ToList());
        }

        var deleted = await _policyRepository.DeleteAsync(policyId, cancellationToken);
        if (!deleted.Success)
        {
            return deleted.StatusCode == 409
                ? OperationResult.Fail(
                    $"A role was given the policy '{policy.Name}' while it was being deleted. Try again.",
                    409, AuthErrorCodes.PolicyInUse)
                : deleted;
        }

        var tenantId = _tenantContext.TenantId;
        _cacheVersions.Bump(tenantId);
        await PublishAsync(new EntityDeletedEvent<PolicyDto> { EntityId = policyId, TenantId = tenantId, UserId = _currentUser.UserId }, cancellationToken);
        _logger.LogInformation("Policy {PolicyId} deleted from tenant {TenantId} by {CallerId}", policyId, tenantId, _currentUser.UserId);
        return OperationResult.Ok();
    }

    // ── Helpers ───────────────────────────────────────────────────

    private OperationResult<Guid> CheckWritableTenant(string action)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<Guid>.Fail($"A tenant must be selected to {action}.", 403, ErrorCodes.TenantRequired);
        }

        return tenantId == AuthRoleNames.SystemTenantId
            ? OperationResult<Guid>.Forbidden("The System tenant's policies are not managed through this API.")
            : OperationResult<Guid>.Ok(tenantId);
    }

    /// <summary>A policy of the current tenant. The System tenant's policies are 404.</summary>
    private async Task<OperationResult<PolicyDetailDto>> FindDetailAsync(Guid policyId, string action, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<PolicyDetailDto>.Fail($"A tenant must be selected to {action}.", 403, ErrorCodes.TenantRequired);
        }

        if (tenantId == AuthRoleNames.SystemTenantId)
        {
            return OperationResult<PolicyDetailDto>.NotFound("Policy not found.");
        }

        var detail = await _policyRepository.GetDetailAsync(policyId, cancellationToken);
        return detail.Success && detail.Data is not null
            ? detail
            : OperationResult<PolicyDetailDto>.NotFound("Policy not found.");
    }

    /// <summary>
    /// The catalogue permissions for <paramref name="keys"/> (trimmed, duplicates ignored), or 400
    /// listing the unknown and system-only keys.
    /// </summary>
    private async Task<OperationResult<List<PermissionDto>>> ResolveKeysAsync(IEnumerable<string>? keys, CancellationToken cancellationToken)
    {
        var wanted = (keys ?? []).Select(k => k.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return OperationResult<List<PermissionDto>>.Ok([]);
        }

        var found = await _permissionRepository.GetByKeysAsync(wanted, cancellationToken);
        if (!found.Success || found.Data is null)
        {
            return OperationResult<List<PermissionDto>>.Fail(found.Message, found.StatusCode, found.ErrorCode);
        }

        var known = found.Data.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var errors = wanted
            .Where(k => !known.Contains(k))
            .Order(StringComparer.Ordinal)
            .Select(k => $"Unknown permission '{k}'.")
            .Concat(wanted
                .Where(k => known.Contains(k) && PermissionKeysAuth.SystemOnly.Contains(k))
                .Order(StringComparer.Ordinal)
                .Select(k => $"The permission '{k}' is reserved for the system and cannot be added to a policy."))
            .ToList();

        return errors.Count == 0
            ? OperationResult<List<PermissionDto>>.Ok(found.Data.OrderBy(p => p.Key, StringComparer.Ordinal).ToList())
            : OperationResult<List<PermissionDto>>.BadRequest(errors[0], errors);
    }

    private async Task PublishLinkEventsAsync(PolicyPermissionChangesDto? changes, Guid tenantId, CancellationToken cancellationToken)
    {
        if (changes is null)
        {
            return;
        }

        foreach (var link in changes.Added)
        {
            await PublishAsync(new EntityCreatedEvent<PolicyPermissionDto> { Entity = link, TenantId = tenantId, UserId = _currentUser.UserId }, cancellationToken);
        }

        foreach (var link in changes.Removed)
        {
            await PublishAsync(new EntityDeletedEvent<PolicyPermissionDto> { EntityId = link.Id, TenantId = tenantId, UserId = _currentUser.UserId }, cancellationToken);
        }
    }

    private Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IEvent
        => SafeEventPublisher.PublishAsync(_eventBus, _logger, @event, cancellationToken);

    private static OperationResult NameTaken(string name)
        => OperationResult.Fail($"A policy named '{name}' already exists in this organization.", 409, AuthErrorCodes.PolicyNameTaken);

    private static OperationResult<T> FailFrom<T>(OperationResult source)
        => OperationResult<T>.Fail(source.Message, source.StatusCode, source.ErrorCode, source.Errors);

    private static string Trim(string? value) => (value ?? string.Empty).Trim();

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Stops the transaction with a business result; it rolls back.</summary>
    private sealed class AbortException(OperationResult result) : Exception(result.Message)
    {
        public OperationResult Result { get; } = result;
    }
}
