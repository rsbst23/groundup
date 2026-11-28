using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Repositories.Mappers;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Repositories;

/// <summary>
/// Repository implementation for Policy entities. Extends BaseTenantRepository with
/// automatic tenant isolation and provides junction management methods for
/// assigning and removing permissions via the PolicyPermission junction table.
/// </summary>
public sealed class PolicyRepository : BaseTenantRepository<Policy, PolicyDto>, IPolicyRepository
{
    /// <summary>
    /// Initializes a new instance of <see cref="PolicyRepository"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    /// <param name="tenantContext">Provides the current tenant identity for automatic filtering.</param>
    public PolicyRepository(DbContext context, ITenantContext tenantContext)
        : base(context, tenantContext, AuthPolicyMapper.ToDto, AuthPolicyMapper.ToEntity)
    {
    }

    /// <inheritdoc />
    public async Task<OperationResult<List<PermissionDto>>> GetPermissionsForPolicyAsync(
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        var policyResult = await GetByIdAsync(policyId, cancellationToken);

        if (!policyResult.Success)
            return OperationResult<List<PermissionDto>>.NotFound();

        var permissions = await Context.Set<PolicyPermission>()
            .AsNoTracking()
            .Where(pp => pp.PolicyId == policyId)
            .Select(pp => pp.Permission)
            .ToListAsync(cancellationToken);

        var dtos = permissions.Select(AuthPermissionMapper.ToDto).ToList();
        return OperationResult<List<PermissionDto>>.Ok(dtos);
    }

    /// <inheritdoc />
    public async Task<OperationResult> AssignPermissionAsync(
        Guid policyId,
        Guid permissionId,
        CancellationToken cancellationToken = default)
    {
        // Verify policy exists and belongs to current tenant (via tenant-scoped GetByIdAsync)
        var policyResult = await GetByIdAsync(policyId, cancellationToken);
        if (!policyResult.Success)
            return OperationResult.NotFound();

        // Verify permission exists (permissions are global — no tenant check needed)
        var permission = await Context.Set<Permission>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == permissionId, cancellationToken);

        if (permission is null)
            return OperationResult.NotFound();

        // Check if already assigned (idempotent)
        var existingAssignment = await Context.Set<PolicyPermission>()
            .AsNoTracking()
            .FirstOrDefaultAsync(pp => pp.PolicyId == policyId && pp.PermissionId == permissionId, cancellationToken);

        if (existingAssignment is not null)
            return OperationResult.Ok();

        // Create new junction record
        var policyPermission = new PolicyPermission
        {
            PolicyId = policyId,
            PermissionId = permissionId
        };

        Context.Set<PolicyPermission>().Add(policyPermission);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult.Ok();
        }
        catch (DbUpdateException)
        {
            DiscardRejectedChanges(policyPermission);
            return OperationResult.Fail(
                "A conflict occurred while assigning the permission.",
                409,
                ErrorCodes.Conflict);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemovePermissionAsync(
        Guid policyId,
        Guid permissionId,
        CancellationToken cancellationToken = default)
    {
        // Verify policy exists and belongs to current tenant
        var policyResult = await GetByIdAsync(policyId, cancellationToken);
        if (!policyResult.Success)
            return OperationResult.NotFound();

        var policyPermission = await Context.Set<PolicyPermission>()
            .FirstOrDefaultAsync(pp => pp.PolicyId == policyId && pp.PermissionId == permissionId, cancellationToken);

        if (policyPermission is null)
            return OperationResult.NotFound();

        Context.Set<PolicyPermission>().Remove(policyPermission);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult.Ok();
        }
        catch (DbUpdateException)
        {
            DiscardRejectedChanges(policyPermission);
            return OperationResult.Fail(
                "A conflict occurred while removing the permission assignment.",
                409,
                ErrorCodes.Conflict);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<TenantPolicyDto>>> ListAsync(
        PaginationParams paging,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paging);
        var tenantId = TenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<PaginatedData<TenantPolicyDto>>.Fail(TenantRequiredMessage, 403, ErrorCodes.TenantRequired);
        }

        var query = DbSet.AsNoTracking().Where(p => p.TenantId == tenantId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((paging.PageNumber - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                Keys = p.PolicyPermissions.Select(pp => pp.Permission.Key).OrderBy(k => k).ToList(),
                // Only the tenant's own roles count (links to another tenant's role exist only in hand-edited data)
                RoleCount = p.RolePolicies.Count(rp => rp.Role.TenantId == tenantId)
            })
            .ToListAsync(cancellationToken);

        return OperationResult<PaginatedData<TenantPolicyDto>>.Ok(new PaginatedData<TenantPolicyDto>
        {
            Items = rows.Select(r => new TenantPolicyDto(r.Id, r.Name, r.Description, r.Keys, r.RoleCount)).ToList(),
            PageNumber = paging.PageNumber,
            PageSize = paging.PageSize,
            TotalRecords = total
        });
    }

    /// <inheritdoc />
    public async Task<OperationResult<PolicyDetailDto>> GetDetailAsync(
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<PolicyDetailDto>.Fail(TenantRequiredMessage, 403, ErrorCodes.TenantRequired);
        }

        var policy = await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == policyId && p.TenantId == tenantId, cancellationToken);
        if (policy is null)
        {
            return OperationResult<PolicyDetailDto>.NotFound("Policy not found.");
        }

        var permissions = await Context.Set<PolicyPermission>().AsNoTracking()
            .Where(pp => pp.PolicyId == policyId)
            .Select(pp => pp.Permission)
            .OrderBy(p => p.Key)
            .ToListAsync(cancellationToken);

        var roles = await Context.Set<RolePolicy>().AsNoTracking()
            .Where(rp => rp.PolicyId == policyId && rp.Role.TenantId == tenantId)
            .Select(rp => new { rp.Role.Id, rp.Role.Name })
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        return OperationResult<PolicyDetailDto>.Ok(new PolicyDetailDto(
            policy.Id,
            policy.Name,
            policy.Description,
            permissions.Select(AuthPermissionMapper.ToDto).ToList(),
            roles.Select(r => new EntityRefDto(r.Id, r.Name)).ToList()));
    }

    /// <inheritdoc />
    public async Task<OperationResult<PolicyPermissionChangesDto>> ReplacePermissionsAsync(
        Guid policyId,
        IReadOnlyCollection<Guid> permissionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissionIds);

        var policyResult = await GetByIdAsync(policyId, cancellationToken);
        if (!policyResult.Success)
        {
            return OperationResult<PolicyPermissionChangesDto>.Fail(policyResult.Message, policyResult.StatusCode, policyResult.ErrorCode);
        }

        var links = Context.Set<PolicyPermission>();
        var existing = await links.Where(pp => pp.PolicyId == policyId).ToListAsync(cancellationToken);
        var target = permissionIds.ToHashSet();
        var existingIds = existing.Select(pp => pp.PermissionId).ToHashSet();

        var toRemove = existing.Where(pp => !target.Contains(pp.PermissionId)).ToList();
        var toAdd = target
            .Where(id => !existingIds.Contains(id))
            .Select(id => new PolicyPermission { PolicyId = policyId, PermissionId = id })
            .ToList();

        if (toRemove.Count == 0 && toAdd.Count == 0)
        {
            return OperationResult<PolicyPermissionChangesDto>.Ok(new PolicyPermissionChangesDto([], []));
        }

        links.RemoveRange(toRemove);
        links.AddRange(toAdd);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            foreach (var link in toAdd.Concat(toRemove))
            {
                DiscardRejectedChanges(link);
            }

            return OperationResult<PolicyPermissionChangesDto>.Fail(
                "The policy's permissions were changed at the same time. Try again.",
                409,
                ErrorCodes.Conflict);
        }

        return OperationResult<PolicyPermissionChangesDto>.Ok(new PolicyPermissionChangesDto(
            toAdd.Select(ToLinkDto).ToList(),
            toRemove.Select(ToLinkDto).ToList()));
    }

    /// <inheritdoc />
    /// <remarks>
    /// A policy still used by a role cannot be deleted (the foreign key restricts it, GU-96). That
    /// rejection is returned as 409 <c>CONFLICT</c> instead of an exception. The policy's permission
    /// links are deleted with it.
    /// </remarks>
    public override async Task<OperationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.DeleteAsync(id, cancellationToken);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in Context.ChangeTracker.Entries<Policy>().Where(e => e.Entity.Id == id).ToList())
            {
                DiscardRejectedChanges(entry.Entity);
            }

            return OperationResult.Fail(
                "The policy is still used by a role and cannot be deleted.",
                409,
                ErrorCodes.Conflict);
        }
    }

    private const string TenantRequiredMessage = "A current tenant is required for this operation.";

    private static PolicyPermissionDto ToLinkDto(PolicyPermission link) => new(link.Id, link.PolicyId, link.PermissionId);
}
