using FluentValidation;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
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
        ILogger<RoleService> logger,
        IEventBus? eventBus = null)
    {
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
        PaginationParams paging,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paging);

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
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalRecords = 0
            });
        }

        var page = await _roleRepository.GetAllAsync(
            new FilterParams
            {
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                SortBy = string.IsNullOrWhiteSpace(paging.SortBy) ? nameof(RoleDto.Name) : paging.SortBy
            },
            cancellationToken);
        if (!page.Success || page.Data is null)
        {
            return OperationResult<PaginatedData<TenantRoleDto>>.Fail(page.Message, page.StatusCode, page.ErrorCode, page.Errors);
        }

        var defaultRoleId = await GetDefaultRoleIdAsync(tenantId, cancellationToken);
        return OperationResult<PaginatedData<TenantRoleDto>>.Ok(new PaginatedData<TenantRoleDto>
        {
            Items = page.Data.Items.Select(r => TenantRoleDto.From(r, r.Id == defaultRoleId)).ToList(),
            PageNumber = page.Data.PageNumber,
            PageSize = page.Data.PageSize,
            TotalRecords = page.Data.TotalRecords
        });
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
        return OperationResult<TenantRoleDto>.Ok(TenantRoleDto.From(role.Data, role.Data.Id == defaultRoleId));
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
        return OperationResult<TenantRoleDto>.Ok(TenantRoleDto.From(updated.Data, updated.Data.Id == defaultRoleId));
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

        return OperationResult<TenantRoleDto?>.Ok(TenantRoleDto.From(role.Data, isDefault: true));
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
        return OperationResult<TenantRoleDto>.Ok(TenantRoleDto.From(existing, isDefault: true));
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

    /// <summary>
    /// Publishes an event after a saved change; a handler failure is logged and does not change the
    /// outcome (as in <c>TenantAccessRevoker</c>).
    /// </summary>
    private async Task PublishSafelyAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        if (_eventBus is null)
        {
            return;
        }

        try
        {
            await _eventBus.PublishAsync(@event, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Publishing {EventType} failed; the change is saved", typeof(TEvent).Name);
        }
    }

    /// <summary>Stops the transaction with a business result; it rolls back.</summary>
    private sealed class AbortException(OperationResult result) : Exception(result.Message)
    {
        public OperationResult Result { get; } = result;
    }
}
