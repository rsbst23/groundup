using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Repositories.Mappers;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Repositories;

/// <summary>
/// Repository implementation for UserRole junction entities. Extends BaseTenantRepository
/// with automatic tenant isolation and provides a method for querying all role
/// assignments for a user within the current tenant.
/// <para>
/// The lookups below feed permission checks and token claims, so they return only roles held
/// through an <see cref="MembershipStatus.Active"/> membership: a pending member's roles are
/// stored but have no effect. A user can hold one role from several sources (one row each);
/// the lookups return each role once.
/// </para>
/// </summary>
public sealed class UserRoleRepository : BaseTenantRepository<UserRole, UserRoleDto>, IUserRoleRepository
{
    /// <summary>
    /// Initializes a new instance of <see cref="UserRoleRepository"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    /// <param name="tenantContext">Provides the current tenant identity for automatic filtering.</param>
    public UserRoleRepository(DbContext context, ITenantContext tenantContext)
        : base(context, tenantContext, AuthUserRoleMapper.ToDto, AuthUserRoleMapper.ToEntity)
    {
    }

    /// <inheritdoc />
    public async Task<OperationResult<List<UserRoleDto>>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantContext.TenantId;

        var entities = await HeldThroughActiveMembership(DbSet.AsNoTracking())
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var dtos = entities.Select(AuthUserRoleMapper.ToDto).DistinctBy(r => r.RoleId).ToList();
        return OperationResult<List<UserRoleDto>>.Ok(dtos);
    }

    /// <inheritdoc />
    /// <remarks>
    /// SYSTEM BYPASS: Queries the DbContext directly with an explicit tenant filter,
    /// bypassing the tenant-scoped DbSet. Includes the Role navigation property to
    /// populate RoleName in the DTO projection.
    /// </remarks>
    public async Task<OperationResult<List<UserRoleDto>>> GetByUserIdForTenantAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var dtos = await HeldThroughActiveMembership(Context.Set<UserRole>().AsNoTracking())
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .Select(ur => new UserRoleDto(ur.Id, ur.UserId, ur.RoleId, ur.TenantId, ur.Role.Name, ur.Source))
            .ToListAsync(cancellationToken);

        return OperationResult<List<UserRoleDto>>.Ok(dtos.DistinctBy(r => r.RoleId).ToList());
    }

    /// <inheritdoc />
    /// <remarks>
    /// SYSTEM BYPASS: This method intentionally queries the DbContext directly,
    /// bypassing the tenant-filtered DbSet. This is required for resolving system-level
    /// roles that transcend tenant boundaries. Includes the Role navigation property
    /// to populate RoleName in the DTO projection. System roles live in the System tenant,
    /// where the SuperAdmin bootstrap creates an Active membership.
    /// </remarks>
    public async Task<OperationResult<List<UserRoleDto>>> GetSystemRolesForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var dtos = await HeldThroughActiveMembership(Context.Set<UserRole>().AsNoTracking())
            .Where(ur => ur.UserId == userId && ur.Role.RoleType == RoleType.System)
            .Select(ur => new UserRoleDto(ur.Id, ur.UserId, ur.RoleId, ur.TenantId, ur.Role.Name, ur.Source))
            .ToListAsync(cancellationToken);

        return OperationResult<List<UserRoleDto>>.Ok(dtos.DistinctBy(r => r.RoleId).ToList());
    }

    /// <inheritdoc />
    /// <remarks>
    /// SYSTEM BYPASS: Queries the DbContext directly with an explicit tenant filter,
    /// bypassing the tenant-scoped DbSet. Returns the users who hold the TenantAdmin
    /// role in the specified tenant AND have an Active membership there, used by
    /// the last-admin guard. Departed and pending admins do not count. One entry per user.
    /// </remarks>
    public async Task<OperationResult<List<UserRoleDto>>> GetTenantAdminHoldersAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var dtos = await HeldThroughActiveMembership(Context.Set<UserRole>().AsNoTracking())
            .Where(ur => ur.TenantId == tenantId
                && ur.Role.Name == Auth.Core.AuthRoleNames.TenantAdmin)
            .Select(ur => new UserRoleDto(ur.Id, ur.UserId, ur.RoleId, ur.TenantId, ur.Role.Name, ur.Source))
            .ToListAsync(cancellationToken);

        return OperationResult<List<UserRoleDto>>.Ok(dtos.DistinctBy(r => r.UserId).ToList());
    }

    /// <summary>
    /// Keeps only assignments whose user has an Active membership in the assignment's tenant.
    /// </summary>
    private IQueryable<UserRole> HeldThroughActiveMembership(IQueryable<UserRole> query) =>
        query.Where(ur => Context.Set<UserTenant>().Any(ut =>
            ut.UserId == ur.UserId
            && ut.TenantId == ur.TenantId
            && ut.Status == MembershipStatus.Active));
}
