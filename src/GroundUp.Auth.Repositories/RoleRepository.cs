using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
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
            DiscardRejectedChanges(rolePolicy);
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
            DiscardRejectedChanges(rolePolicy);
            return OperationResult.Fail(
                "A conflict occurred while removing the policy assignment.",
                409,
                ErrorCodes.Conflict);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// SYSTEM BYPASS: queries the DbContext directly, without the current tenant filter, so the
    /// SuperAdmin role (System tenant) can be found. The caller checks the role's tenant.
    /// </remarks>
    public async Task<OperationResult<RoleDto>> FindByIdAnyTenantAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var role = await Context.Set<Role>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);

        return role is null
            ? OperationResult<RoleDto>.NotFound("Role not found.")
            : OperationResult<RoleDto>.Ok(AuthRoleMapper.ToDto(role));
    }

    /// <inheritdoc />
    /// <remarks>SYSTEM BYPASS: targets <paramref name="tenantId"/> explicitly.</remarks>
    public async Task<OperationResult<RoleDto>> GetByNameAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim().ToLower();
        var role = await Context.Set<Role>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name.ToLower() == normalized, cancellationToken);

        return role is null
            ? OperationResult<RoleDto>.NotFound("Role not found.")
            : OperationResult<RoleDto>.Ok(AuthRoleMapper.ToDto(role));
    }

    /// <inheritdoc />
    /// <remarks>
    /// A role still referenced by a member's grant, an invitation, a join link or a tenant's default
    /// role cannot be deleted (the foreign keys restrict it). That rejection is returned as 409
    /// <c>CONFLICT</c> instead of an exception, so the role service can report it (GU-95).
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
            foreach (var entry in Context.ChangeTracker.Entries<Role>().Where(e => e.Entity.Id == id).ToList())
            {
                DiscardRejectedChanges(entry.Entity);
            }

            return OperationResult.Fail(
                "The role is still in use and cannot be deleted.",
                409,
                ErrorCodes.Conflict);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<RoleUsageDto>> GetUsageAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        // The tenant-filtered read: a role of another tenant is NotFound
        var role = await GetByIdAsync(roleId, cancellationToken);
        if (!role.Success || role.Data is null)
        {
            return OperationResult<RoleUsageDto>.NotFound("Role not found.");
        }

        var tenantId = role.Data.TenantId;
        var holders = await Context.Set<UserRole>()
            .CountAsync(ur => ur.RoleId == roleId && ur.TenantId == tenantId, cancellationToken);
        var pendingInvitations = await Context.Set<Invitation>()
            .CountAsync(i => i.RoleId == roleId
                && (i.Status == InvitationStatus.Pending || i.Status == InvitationStatus.Expired), cancellationToken);
        var activeJoinLinks = await Context.Set<JoinLink>()
            .CountAsync(l => l.RoleId == roleId && l.Status == JoinLinkStatus.Active, cancellationToken);
        var isDefault = await Context.Set<Tenant>()
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Id == tenantId && t.DefaultRoleId == roleId, cancellationToken);

        return OperationResult<RoleUsageDto>.Ok(new RoleUsageDto(holders, pendingInvitations, activeJoinLinks, isDefault));
    }
}
