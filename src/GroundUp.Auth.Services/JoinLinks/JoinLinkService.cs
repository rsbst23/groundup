using FluentValidation;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Roles;
using GroundUp.Auth.Services.Token;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.JoinLinks;

/// <summary>
/// Default <see cref="IJoinLinkService"/>. The permission is checked by the authorization proxy;
/// this class checks the tenant kind and the role-grant rules.
/// </summary>
internal sealed class JoinLinkService : IJoinLinkService
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantRepository _tenantRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IJoinLinkRepository _joinLinkRepository;
    private readonly RoleGrantPolicy _rolePolicy;
    private readonly IValidator<CreateJoinLinkDto> _validator;
    private readonly ILogger<JoinLinkService> _logger;

    public JoinLinkService(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        ITenantRepository tenantRepository,
        IRoleRepository roleRepository,
        IJoinLinkRepository joinLinkRepository,
        RoleGrantPolicy rolePolicy,
        IValidator<CreateJoinLinkDto> validator,
        ILogger<JoinLinkService> logger)
    {
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _tenantRepository = tenantRepository;
        _roleRepository = roleRepository;
        _joinLinkRepository = joinLinkRepository;
        _rolePolicy = rolePolicy;
        _validator = validator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<CreatedJoinLinkDto>> CreateAsync(CreateJoinLinkDto request, CancellationToken cancellationToken = default)
    {
        var tenant = await GetCurrentStandardTenantAsync(cancellationToken);
        if (!tenant.Success)
        {
            return Fail<CreatedJoinLinkDto>(tenant.Message, tenant.StatusCode, tenant.ErrorCode);
        }

        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return OperationResult<CreatedJoinLinkDto>.BadRequest(
                validation.Errors[0].ErrorMessage, validation.Errors.Select(e => e.ErrorMessage).ToList());
        }

        var role = await GetGrantableRoleAsync(request.RoleId, tenant.Data!.Id, cancellationToken);
        if (!role.Success)
        {
            return Fail<CreatedJoinLinkDto>(role.Message, role.StatusCode, role.ErrorCode);
        }

        var token = OpaqueToken.Create();
        var added = await _joinLinkRepository.AddAsync(
            new JoinLinkDto(Guid.Empty, tenant.Data.Id, role.Data!.Id, role.Data.Name, _currentUser.UserId,
                request.ExpiresAt?.ToUniversalTime(), request.MaxUses, 0, JoinLinkStatus.Active, null, default),
            OpaqueToken.Hash(token), cancellationToken);
        if (!added.Success || added.Data is null)
        {
            return Fail<CreatedJoinLinkDto>(added.Message, added.StatusCode, added.ErrorCode);
        }

        _logger.LogInformation(
            "Join link {JoinLinkId} created in tenant {TenantId} by {CallerId}", added.Data.Id, tenant.Data.Id, _currentUser.UserId);
        return OperationResult<CreatedJoinLinkDto>.Ok(new CreatedJoinLinkDto(added.Data, token), statusCode: 201);
    }

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<JoinLinkDto>>> ListAsync(FilterParams filterParams, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        return tenantId == Guid.Empty
            ? OperationResult<PaginatedData<JoinLinkDto>>.Fail("A tenant must be selected to list join links.", 403, ErrorCodes.TenantRequired)
            : await _joinLinkRepository.ListAsync(tenantId, filterParams, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<CreatedJoinLinkDto>> RegenerateAsync(Guid joinLinkId, CancellationToken cancellationToken = default)
    {
        var tenant = await GetCurrentStandardTenantAsync(cancellationToken);
        if (!tenant.Success)
        {
            return Fail<CreatedJoinLinkDto>(tenant.Message, tenant.StatusCode, tenant.ErrorCode);
        }

        var existing = await _joinLinkRepository.GetByIdAsync(tenant.Data!.Id, joinLinkId, cancellationToken);
        if (!existing.Success || existing.Data is null)
        {
            return OperationResult<CreatedJoinLinkDto>.NotFound("Join link not found.");
        }

        // Regenerating re-issues the grant, so the caller must be allowed to grant the role
        var role = await GetGrantableRoleAsync(existing.Data.RoleId, tenant.Data.Id, cancellationToken);
        if (!role.Success)
        {
            return Fail<CreatedJoinLinkDto>(role.Message, role.StatusCode, role.ErrorCode);
        }

        var token = OpaqueToken.Create();
        var reissued = await _joinLinkRepository.ReissueAsync(
            tenant.Data.Id, joinLinkId, OpaqueToken.Hash(token), _currentUser.UserId, cancellationToken);
        if (!reissued.Success)
        {
            return Fail<CreatedJoinLinkDto>(reissued.Message, reissued.StatusCode, reissued.ErrorCode);
        }

        var link = (await _joinLinkRepository.GetByIdAsync(tenant.Data.Id, joinLinkId, cancellationToken)).Data!;
        _logger.LogInformation("Join link {JoinLinkId} regenerated by {CallerId}", joinLinkId, _currentUser.UserId);
        return OperationResult<CreatedJoinLinkDto>.Ok(new CreatedJoinLinkDto(link, token));
    }

    /// <inheritdoc />
    public async Task<OperationResult> RevokeAsync(Guid joinLinkId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult.Fail("A tenant must be selected to revoke join links.", 403, ErrorCodes.TenantRequired);
        }

        var revoked = await _joinLinkRepository.RevokeAsync(tenantId, joinLinkId, _currentUser.UserId, cancellationToken);
        if (revoked.Success)
        {
            _logger.LogInformation("Join link {JoinLinkId} revoked by {CallerId}", joinLinkId, _currentUser.UserId);
        }

        return revoked;
    }

    /// <summary>The current tenant, refused for enterprise tenants until GU-69.</summary>
    private async Task<OperationResult<TenantDto>> GetCurrentStandardTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<TenantDto>.Fail("A tenant must be selected to manage join links.", 403, ErrorCodes.TenantRequired);
        }

        var tenants = await _tenantRepository.GetByIdsBypassFilterAsync([tenantId], cancellationToken);
        var tenant = tenants.Success ? tenants.Data?.FirstOrDefault() : null;
        if (tenant is null)
        {
            return OperationResult<TenantDto>.NotFound("Organization not found.");
        }

        return string.IsNullOrEmpty(tenant.RealmName)
            ? OperationResult<TenantDto>.Ok(tenant)
            : OperationResult<TenantDto>.Fail(
                "Join links are not available for enterprise organizations yet.", 422, AuthErrorCodes.EnterpriseNotSupported);
    }

    private async Task<OperationResult<RoleDto>> GetGrantableRoleAsync(Guid roleId, Guid tenantId, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.FindByIdAnyTenantAsync(roleId, cancellationToken);
        if (!role.Success || role.Data is null || role.Data.TenantId != tenantId)
        {
            return OperationResult<RoleDto>.NotFound("Role not found.");
        }

        var check = await _rolePolicy.CheckAsync(
            _currentUser.UserId, Guid.Empty, role.Data, RoleChange.Assign, RoleAssignmentSource.JoinLink, cancellationToken);
        return check.Success
            ? OperationResult<RoleDto>.Ok(role.Data)
            : OperationResult<RoleDto>.Fail(check.Message, check.StatusCode, check.ErrorCode);
    }

    private static OperationResult<T> Fail<T>(string message, int statusCode, string? errorCode)
        => OperationResult<T>.Fail(message, statusCode, errorCode);
}
