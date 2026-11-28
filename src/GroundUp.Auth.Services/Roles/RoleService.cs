using FluentValidation;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Events;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// Default <see cref="IRoleService"/>. Permissions are checked by the authorization proxy; this class
/// checks the current tenant, the input, the built-in role rules, role usage before a delete, and
/// whether the caller may grant the role made the default.
/// </summary>
internal sealed class RoleService : IRoleService
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IRoleRepository _roleRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly RoleGrantPolicy _rolePolicy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreateRoleDto> _createValidator;
    private readonly IValidator<UpdateRoleDto> _updateValidator;
    private readonly IPolicyRepository _policyRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly PermissionHoldGuard _holdGuard;
    private readonly PermissionCacheVersions _cacheVersions;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<RoleService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="RoleService"/>. The event bus is optional: it is present
    /// when the application registered one (<c>AddGroundUpEvents</c>); without it no events are published.
    /// </summary>
    public RoleService(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IRoleRepository roleRepository,
        ITenantRepository tenantRepository,
        RoleGrantPolicy rolePolicy,
        IUnitOfWork unitOfWork,
        IValidator<CreateRoleDto> createValidator,
        IValidator<UpdateRoleDto> updateValidator,
        IPolicyRepository policyRepository,
        IPermissionRepository permissionRepository,
        PermissionHoldGuard holdGuard,
        PermissionCacheVersions cacheVersions,
        ILogger<RoleService> logger,
        IEventBus? eventBus = null)
    {
        _policyRepository = policyRepository;
        _permissionRepository = permissionRepository;
        _holdGuard = holdGuard;
        _cacheVersions = cacheVersions;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _roleRepository = roleRepository;
        _tenantRepository = tenantRepository;
        _rolePolicy = rolePolicy;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<TenantRoleDto>>> ListAsync(
        FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filterParams);

        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<PaginatedData<TenantRoleDto>>.Fail(
                "A tenant must be selected to list roles.", 403, ErrorCodes.TenantRequired);
        }

        if (tenantId == AuthRoleNames.SystemTenantId)
        {
            return OperationResult<PaginatedData<TenantRoleDto>>.Ok(new PaginatedData<TenantRoleDto>
            {
                Items = [],
                PageNumber = filterParams.PageNumber,
                PageSize = filterParams.PageSize,
                TotalRecords = 0
            });
        }

        // One query: the page with isDefault and policyCount (GU-97)
        return await _roleRepository.ListAsync(filterParams, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TenantRoleDto>> GetAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await FindAsync(roleId, "view roles", cancellationToken);
        if (!role.Success)
        {
            return Fail<TenantRoleDto>(role);
        }

        var defaultRoleId = await GetDefaultRoleIdAsync(role.Data!.TenantId, cancellationToken);
        return OperationResult<TenantRoleDto>.Ok(
            TenantRoleDto.From(role.Data, role.Data.Id == defaultRoleId, await CountsAsync(roleId, cancellationToken)));
    }

    /// <inheritdoc />
    public async Task<OperationResult<TenantRoleDto>> CreateAsync(CreateRoleDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = CheckWritableTenant("create roles");
        if (!tenant.Success)
        {
            return Fail<TenantRoleDto>(tenant);
        }

        request = request with { Name = Trim(request.Name), Description = TrimToNull(request.Description) };
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return OperationResult<TenantRoleDto>.BadRequest(
                validation.Errors[0].ErrorMessage, validation.Errors.Select(e => e.ErrorMessage).ToList());
        }

        if (request.RoleType is not (RoleType.Application or RoleType.Workspace))
        {
            return OperationResult<TenantRoleDto>.BadRequest("A role's type must be Application or Workspace.");
        }

        var added = await _roleRepository.AddAsync(
            new RoleDto(Guid.Empty, request.Name, request.Description, request.RoleType, tenant.Data, IsSystem: false),
            cancellationToken);
        if (!added.Success || added.Data is null)
        {
            return added.StatusCode == 409
                ? NameTaken<TenantRoleDto>(request.Name)
                : Fail<TenantRoleDto>(added);
        }

        await PublishSafelyAsync(new EntityCreatedEvent<RoleDto> { Entity = added.Data, TenantId = tenant.Data, UserId = _currentUser.UserId }, cancellationToken);
        _logger.LogInformation("Role {RoleId} created in tenant {TenantId} by {CallerId}", added.Data.Id, tenant.Data, _currentUser.UserId);
        return OperationResult<TenantRoleDto>.Ok(TenantRoleDto.From(added.Data, isDefault: false), "Created", 201);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TenantRoleDto>> UpdateAsync(
        Guid roleId,
        UpdateRoleDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await FindAsync(roleId, "update roles", cancellationToken);
        if (!role.Success)
        {
            return Fail<TenantRoleDto>(role);
        }

        request = request with { Name = Trim(request.Name), Description = TrimToNull(request.Description) };
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return OperationResult<TenantRoleDto>.BadRequest(
                validation.Errors[0].ErrorMessage, validation.Errors.Select(e => e.ErrorMessage).ToList());
        }

        var existing = role.Data!;
        // Ordinal on purpose: code finds TenantAdmin by name, so not even the case may change
        if (existing.IsSystem && !string.Equals(existing.Name, request.Name, StringComparison.Ordinal))
        {
            return OperationResult<TenantRoleDto>.Fail(
                $"The built-in role '{existing.Name}' cannot be renamed. Its description can be changed.",
                409, AuthErrorCodes.RoleIsBuiltIn);
        }

        var updated = await _roleRepository.UpdateAsync(
            roleId, existing with { Name = request.Name, Description = request.Description }, cancellationToken);
        if (!updated.Success || updated.Data is null)
        {
            return updated.StatusCode == 409
                ? NameTaken<TenantRoleDto>(request.Name)
                : Fail<TenantRoleDto>(updated);
        }

        await PublishSafelyAsync(new EntityUpdatedEvent<RoleDto> { Entity = updated.Data, TenantId = existing.TenantId, UserId = _currentUser.UserId }, cancellationToken);
        _logger.LogInformation("Role {RoleId} updated in tenant {TenantId} by {CallerId}", roleId, existing.TenantId, _currentUser.UserId);

        var defaultRoleId = await GetDefaultRoleIdAsync(existing.TenantId, cancellationToken);
        return OperationResult<TenantRoleDto>.Ok(
            TenantRoleDto.From(updated.Data, updated.Data.Id == defaultRoleId, await CountsAsync(roleId, cancellationToken)));
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await FindAsync(roleId, "delete roles", cancellationToken);
        if (!role.Success)
        {
            return OperationResult.Fail(role.Message, role.StatusCode, role.ErrorCode);
        }

        var existing = role.Data!;
        if (existing.IsSystem)
        {
            return OperationResult.Fail($"The built-in role '{existing.Name}' cannot be deleted.", 409, AuthErrorCodes.RoleIsBuiltIn);
        }

        OperationResult transaction;
        try
        {
            // The usage check and the delete run in one transaction, and the foreign keys back the
            // check up: a grant committed in between makes the delete fail instead of being lost
            transaction = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var usage = await _roleRepository.GetUsageAsync(roleId, ct);
                if (!usage.Success || usage.Data is null)
                {
                    throw new AbortException(OperationResult.Fail(usage.Message, usage.StatusCode, usage.ErrorCode));
                }

                if (!usage.Data.IsUnused)
                {
                    throw new AbortException(OperationResult.Fail(
                        $"The role '{existing.Name}' is in use and cannot be deleted.",
                        409, AuthErrorCodes.RoleInUse, UsageReasons(usage.Data)));
                }

                var deleted = await _roleRepository.DeleteAsync(roleId, ct);
                if (!deleted.Success)
                {
                    throw new AbortException(deleted.StatusCode == 409
                        ? OperationResult.Fail(
                            $"The role '{existing.Name}' was given to a member or used while it was being deleted. Try again.",
                            409, AuthErrorCodes.RoleInUse)
                        : deleted);
                }
            }, cancellationToken);
        }
        catch (AbortException abort)
        {
            return abort.Result;
        }

        if (!transaction.Success)
        {
            return transaction;
        }

        await PublishSafelyAsync(new EntityDeletedEvent<RoleDto> { EntityId = roleId, TenantId = existing.TenantId, UserId = _currentUser.UserId }, cancellationToken);
        _logger.LogInformation("Role {RoleId} deleted from tenant {TenantId} by {CallerId}", roleId, existing.TenantId, _currentUser.UserId);
        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public async Task<OperationResult<TenantRoleDto?>> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<TenantRoleDto?>.Fail("A tenant must be selected to view roles.", 403, ErrorCodes.TenantRequired);
        }

        if (tenantId == AuthRoleNames.SystemTenantId)
        {
            return OperationResult<TenantRoleDto?>.Ok(null);
        }

        var defaultRoleId = await _tenantRepository.GetDefaultRoleIdAsync(tenantId, cancellationToken);
        if (!defaultRoleId.Success)
        {
            return OperationResult<TenantRoleDto?>.Fail(defaultRoleId.Message, defaultRoleId.StatusCode, defaultRoleId.ErrorCode);
        }

        if (defaultRoleId.Data is not { } roleId)
        {
            return OperationResult<TenantRoleDto?>.Ok(null);
        }

        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (!role.Success || role.Data is null)
        {
            // Only possible with hand-edited data: the foreign key keeps the role, the tenant filter hides it
            _logger.LogWarning("The default role {RoleId} of tenant {TenantId} is not one of its roles", roleId, tenantId);
            return OperationResult<TenantRoleDto?>.Ok(null);
        }

        return OperationResult<TenantRoleDto?>.Ok(
            TenantRoleDto.From(role.Data, isDefault: true, await CountsAsync(role.Data.Id, cancellationToken)));
    }

    /// <inheritdoc />
    public async Task<OperationResult<TenantRoleDto>> SetDefaultAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await FindAsync(roleId, "change the default role", cancellationToken);
        if (!role.Success)
        {
            return Fail<TenantRoleDto>(role);
        }

        var existing = role.Data!;
        if (existing.IsSystem)
        {
            return OperationResult<TenantRoleDto>.Fail(
                $"The built-in role '{existing.Name}' cannot be the default role.", 409, AuthErrorCodes.RoleIsBuiltIn);
        }

        // Making a role the default grants it to future members, so the caller must be allowed to grant it
        var allowed = await _rolePolicy.CheckAsync(
            _currentUser.UserId, Guid.Empty, existing, RoleChange.Assign, RoleAssignmentSource.DefaultRole, cancellationToken);
        if (!allowed.Success)
        {
            return OperationResult<TenantRoleDto>.Fail(allowed.Message, allowed.StatusCode, allowed.ErrorCode, allowed.Errors);
        }

        var set = await _tenantRepository.SetDefaultRoleIdAsync(existing.TenantId, roleId, cancellationToken);
        if (!set.Success)
        {
            return OperationResult<TenantRoleDto>.Fail(set.Message, set.StatusCode, set.ErrorCode, set.Errors);
        }

        await PublishSafelyAsync(new EntityUpdatedEvent<RoleDto> { Entity = existing, TenantId = existing.TenantId, UserId = _currentUser.UserId }, cancellationToken);
        _logger.LogInformation("Role {RoleId} made the default role of tenant {TenantId} by {CallerId}", roleId, existing.TenantId, _currentUser.UserId);
        return OperationResult<TenantRoleDto>.Ok(
            TenantRoleDto.From(existing, isDefault: true, await CountsAsync(existing.Id, cancellationToken)));
    }

    /// <inheritdoc />
    public async Task<OperationResult> ClearDefaultAsync(CancellationToken cancellationToken = default)
    {
        var tenant = CheckWritableTenant("change the default role");
        if (!tenant.Success)
        {
            return OperationResult.Fail(tenant.Message, tenant.StatusCode, tenant.ErrorCode);
        }

        var tenantId = tenant.Data;
        var current = await _tenantRepository.GetDefaultRoleIdAsync(tenantId, cancellationToken);
        if (!current.Success)
        {
            return OperationResult.Fail(current.Message, current.StatusCode, current.ErrorCode);
        }

        if (current.Data is not { } previousRoleId)
        {
            return OperationResult.Ok();
        }

        var cleared = await _tenantRepository.SetDefaultRoleIdAsync(tenantId, null, cancellationToken);
        if (!cleared.Success)
        {
            return cleared;
        }

        var previous = await _roleRepository.GetByIdAsync(previousRoleId, cancellationToken);
        if (previous.Success && previous.Data is not null)
        {
            await PublishSafelyAsync(new EntityUpdatedEvent<RoleDto> { Entity = previous.Data, TenantId = tenantId, UserId = _currentUser.UserId }, cancellationToken);
        }

        _logger.LogInformation("Default role of tenant {TenantId} cleared by {CallerId}", tenantId, _currentUser.UserId);
        return OperationResult.Ok();
    }

    // ── Policies (GU-96) ──────────────────────────────────────────

    /// <inheritdoc />
    public async Task<OperationResult<List<PolicyDto>>> GetPoliciesAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await FindAsync(roleId, "view roles", cancellationToken);
        if (!role.Success)
        {
            return Fail<List<PolicyDto>>(role);
        }

        if (role.Data!.IsSystem)
        {
            return OperationResult<List<PolicyDto>>.Ok([]);
        }

        var policies = await _roleRepository.GetPoliciesForRoleAsync(roleId, cancellationToken);
        return policies.Success && policies.Data is not null
            ? OperationResult<List<PolicyDto>>.Ok(policies.Data.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList())
            : policies;
    }

    /// <inheritdoc />
    public async Task<OperationResult<RolePolicyDto>> AttachPolicyAsync(Guid roleId, Guid policyId, CancellationToken cancellationToken = default)
    {
        var role = await FindAsync(roleId, "change roles", cancellationToken);
        if (!role.Success)
        {
            return Fail<RolePolicyDto>(role);
        }

        if (role.Data!.IsSystem)
        {
            return BuiltInPolicies<RolePolicyDto>(role.Data);
        }

        // The tenant-filtered read: another tenant's policy is 404
        var policy = await _policyRepository.GetDetailAsync(policyId, cancellationToken);
        if (!policy.Success || policy.Data is null)
        {
            return OperationResult<RolePolicyDto>.NotFound("Policy not found.");
        }

        var held = await _holdGuard.CheckHoldsAsync(_currentUser.UserId, policy.Data.Permissions.Select(p => p.Key), cancellationToken);
        if (!held.Success)
        {
            return OperationResult<RolePolicyDto>.Fail(held.Message, held.StatusCode, held.ErrorCode, held.Errors);
        }

        var attached = await _roleRepository.AssignPolicyAsync(roleId, policyId, cancellationToken);
        if (!attached.Success || attached.Data is null)
        {
            return attached;
        }

        if (attached.StatusCode == 201)
        {
            var tenantId = role.Data.TenantId;
            _cacheVersions.Bump(tenantId);
            await PublishSafelyAsync(new EntityCreatedEvent<RolePolicyDto> { Entity = attached.Data, TenantId = tenantId, UserId = _currentUser.UserId }, cancellationToken);
            _logger.LogInformation(
                "Policy {PolicyId} attached to role {RoleId} in tenant {TenantId} by {CallerId}", policyId, roleId, tenantId, _currentUser.UserId);
        }

        return attached;
    }

    /// <inheritdoc />
    public async Task<OperationResult> DetachPolicyAsync(Guid roleId, Guid policyId, CancellationToken cancellationToken = default)
    {
        var role = await FindAsync(roleId, "change roles", cancellationToken);
        if (!role.Success)
        {
            return OperationResult.Fail(role.Message, role.StatusCode, role.ErrorCode);
        }

        if (role.Data!.IsSystem)
        {
            var refused = BuiltInPolicies<RolePolicyDto>(role.Data);
            return OperationResult.Fail(refused.Message, refused.StatusCode, refused.ErrorCode);
        }

        var removed = await _roleRepository.RemovePolicyAsync(roleId, policyId, cancellationToken);
        if (!removed.Success || removed.Data is null)
        {
            return OperationResult.Fail(removed.Message, removed.StatusCode, removed.ErrorCode);
        }

        var tenantId = role.Data.TenantId;
        _cacheVersions.Bump(tenantId);
        await PublishSafelyAsync(new EntityDeletedEvent<RolePolicyDto> { EntityId = removed.Data.Id, TenantId = tenantId, UserId = _currentUser.UserId }, cancellationToken);
        _logger.LogInformation(
            "Policy {PolicyId} detached from role {RoleId} in tenant {TenantId} by {CallerId}", policyId, roleId, tenantId, _currentUser.UserId);
        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public async Task<OperationResult<RolePermissionsDto>> GetPermissionsAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await FindAsync(roleId, "view roles", cancellationToken);
        if (!role.Success)
        {
            return Fail<RolePermissionsDto>(role);
        }

        if (role.Data!.IsSystem)
        {
            return OperationResult<RolePermissionsDto>.Ok(new RolePermissionsDto(roleId, AllPermissions: true, []));
        }

        var grants = await _roleRepository.GetPermissionGrantsAsync(roleId, cancellationToken);
        if (!grants.Success || grants.Data is null)
        {
            return OperationResult<RolePermissionsDto>.Fail(grants.Message, grants.StatusCode, grants.ErrorCode);
        }

        var keys = grants.Data.Select(g => g.PermissionKey).Distinct(StringComparer.Ordinal).ToList();
        var catalogue = await _permissionRepository.GetByKeysAsync(keys, cancellationToken);
        var byKey = (catalogue.Data ?? []).ToDictionary(p => p.Key, StringComparer.Ordinal);

        var permissions = grants.Data
            .GroupBy(g => g.PermissionKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var known = byKey.GetValueOrDefault(g.Key);
                return new RolePermissionDto(
                    g.Key,
                    known?.Name ?? g.Key,
                    known?.Module ?? string.Empty,
                    g.OrderBy(x => x.PolicyName, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new EntityRefDto(x.PolicyId, x.PolicyName))
                        .ToList());
            })
            .ToList();

        return OperationResult<RolePermissionsDto>.Ok(new RolePermissionsDto(roleId, AllPermissions: false, permissions));
    }

    private static OperationResult<T> BuiltInPolicies<T>(RoleDto role)
        => OperationResult<T>.Fail(
            $"The built-in role '{role.Name}' passes every permission check; its policies cannot be changed.",
            409, AuthErrorCodes.RoleIsBuiltIn);

    // ── Helpers ───────────────────────────────────────────────────

    /// <summary>
    /// The current tenant for a change that names no role. The System tenant's roles are not
    /// managed through this API.
    /// </summary>
    private OperationResult<Guid> CheckWritableTenant(string action)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<Guid>.Fail($"A tenant must be selected to {action}.", 403, ErrorCodes.TenantRequired);
        }

        return tenantId == AuthRoleNames.SystemTenantId
            ? OperationResult<Guid>.Forbidden("The System tenant's roles are not managed through this API.")
            : OperationResult<Guid>.Ok(tenantId);
    }

    /// <summary>
    /// A role of the current tenant (the tenant-filtered read). Roles of the System tenant are 404.
    /// </summary>
    private async Task<OperationResult<RoleDto>> FindAsync(Guid roleId, string action, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<RoleDto>.Fail($"A tenant must be selected to {action}.", 403, ErrorCodes.TenantRequired);
        }

        if (tenantId == AuthRoleNames.SystemTenantId)
        {
            return OperationResult<RoleDto>.NotFound("Role not found.");
        }

        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        return role.Success && role.Data is not null && role.Data.TenantId == tenantId
            ? role
            : OperationResult<RoleDto>.NotFound("Role not found.");
    }

    /// <summary>The tenant's default role id; <c>null</c> when it has none or it cannot be read.</summary>
    /// <summary>The role's counts; null (zeros) when they cannot be read (they are informational).</summary>
    private async Task<RoleCountsDto?> CountsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var result = await _roleRepository.GetCountsAsync(roleId, cancellationToken);
        return result.Success ? result.Data : null;
    }

    private async Task<Guid?> GetDefaultRoleIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await _tenantRepository.GetDefaultRoleIdAsync(tenantId, cancellationToken);
        return result.Success ? result.Data : null;
    }

    private static List<string> UsageReasons(RoleUsageDto usage)
    {
        var reasons = new List<string>();
        if (usage.Holders > 0)
        {
            reasons.Add($"Held by {Count(usage.Holders, "member", "members")}.");
        }

        if (usage.PendingInvitations > 0)
        {
            reasons.Add($"Used by {Count(usage.PendingInvitations, "pending or expired invitation", "pending or expired invitations")}.");
        }

        if (usage.ActiveJoinLinks > 0)
        {
            reasons.Add($"Used by {Count(usage.ActiveJoinLinks, "active join link", "active join links")}.");
        }

        if (usage.IsTenantDefault)
        {
            reasons.Add("It is the organization's default role for new members.");
        }

        return reasons;
    }

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    private static string Trim(string? value) => (value ?? string.Empty).Trim();

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OperationResult<T> NameTaken<T>(string name)
        => OperationResult<T>.Fail($"A role named '{name}' already exists in this organization.", 409, AuthErrorCodes.RoleNameTaken);

    private static OperationResult<T> Fail<T>(OperationResult<RoleDto> source)
        => OperationResult<T>.Fail(source.Message, source.StatusCode, source.ErrorCode, source.Errors);

    private static OperationResult<T> Fail<T>(OperationResult<Guid> source)
        => OperationResult<T>.Fail(source.Message, source.StatusCode, source.ErrorCode, source.Errors);

    private Task PublishSafelyAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IEvent
        => SafeEventPublisher.PublishAsync(_eventBus, _logger, @event, cancellationToken);

    /// <summary>Stops the transaction with a business result; it rolls back.</summary>
    private sealed class AbortException(OperationResult result) : Exception(result.Message)
    {
        public OperationResult Result { get; } = result;
    }
}
