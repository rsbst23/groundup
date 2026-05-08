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
/// Repository implementation for Role entities. Extends BaseTenantRepository with
/// automatic tenant isolation and provides junction management methods for
/// assigning and removing policies via the RolePolicy junction table.
/// </summary>
public sealed class RoleRepository : BaseTenantRepository<Role, RoleDto>, IRoleRepository
{
    /// <summary>
    /// Initializes a new instance of <see cref="RoleRepository"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    /// <param name="tenantContext">Provides the current tenant identity for automatic filtering.</param>
    public RoleRepository(DbContext context, ITenantContext tenantContext)
        : base(context, tenantContext, AuthRoleMapper.ToDto, AuthRoleMapper.ToEntity)
    {
    }

    /// <inheritdoc />
    public async Task<OperationResult<List<PolicyDto>>> GetPoliciesForRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var roleResult = await GetByIdAsync(roleId, cancellationToken);

        if (!roleResult.Success)
            return OperationResult<List<PolicyDto>>.NotFound();

        var policies = await Context.Set<RolePolicy>()
            .AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Policy)
            .ToListAsync(cancellationToken);

        var dtos = policies.Select(AuthPolicyMapper.ToDto).ToList();
        return OperationResult<List<PolicyDto>>.Ok(dtos);
    }

    /// <inheritdoc />
    public async Task<OperationResult> AssignPolicyAsync(
        Guid roleId,
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        // Verify role exists and belongs to current tenant (via tenant-scoped GetByIdAsync)
        var roleResult = await GetByIdAsync(roleId, cancellationToken);
        if (!roleResult.Success)
            return OperationResult.NotFound();

        // Verify policy exists and belongs to the same tenant as the role
        var roleTenantId = roleResult.Data!.TenantId;
        var policy = await Context.Set<Policy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == policyId && p.TenantId == roleTenantId, cancellationToken);

        if (policy is null)
            return OperationResult.NotFound();

        // Check if already assigned (idempotent)
        var existingAssignment = await Context.Set<RolePolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PolicyId == policyId, cancellationToken);

        if (existingAssignment is not null)
            return OperationResult.Ok();

        // Create new junction record
        var rolePolicy = new RolePolicy
        {
            RoleId = roleId,
            PolicyId = policyId
        };

        Context.Set<RolePolicy>().Add(rolePolicy);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult.Ok();
        }
        catch (DbUpdateException)
        {
            return OperationResult.Fail(
                "A conflict occurred while assigning the policy.",
                409,
                ErrorCodes.Conflict);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemovePolicyAsync(
        Guid roleId,
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        // Verify role exists and belongs to current tenant
        var roleResult = await GetByIdAsync(roleId, cancellationToken);
        if (!roleResult.Success)
            return OperationResult.NotFound();

        var rolePolicy = await Context.Set<RolePolicy>()
            .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PolicyId == policyId, cancellationToken);

        if (rolePolicy is null)
            return OperationResult.NotFound();

        Context.Set<RolePolicy>().Remove(rolePolicy);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult.Ok();
        }
        catch (DbUpdateException)
        {
            return OperationResult.Fail(
                "A conflict occurred while removing the policy assignment.",
                409,
                ErrorCodes.Conflict);
        }
    }
}
