using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Repositories.Mappers;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
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
            return OperationResult.Fail(
                "A conflict occurred while removing the permission assignment.",
                409,
                ErrorCodes.Conflict);
        }
    }
}
