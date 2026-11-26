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

    /// <inheritdoc />
    /// <remarks>SYSTEM BYPASS: queries the DbContext directly; the role names its own tenant.</remarks>
    public async Task<OperationResult<List<Guid>>> GetActiveHolderIdsAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var userIds = await HeldThroughActiveMembership(Context.Set<UserRole>().AsNoTracking())
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return OperationResult<List<Guid>>.Ok(userIds);
    }

    /// <inheritdoc />
    /// <remarks>SYSTEM BYPASS: targets <paramref name="tenantId"/> explicitly.</remarks>
    public async Task<OperationResult<List<UserRoleDto>>> GetAssignmentsAsync(
        Guid userId,
        Guid roleId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var dtos = await Context.Set<UserRole>()
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.RoleId == roleId && ur.TenantId == tenantId)
            .Select(ur => new UserRoleDto(ur.Id, ur.UserId, ur.RoleId, ur.TenantId, ur.Role.Name, ur.Source))
            .ToListAsync(cancellationToken);

        return OperationResult<List<UserRoleDto>>.Ok(dtos);
    }

    /// <inheritdoc />
    /// <remarks>SYSTEM BYPASS: targets <paramref name="tenantId"/> explicitly.</remarks>
    public async Task<OperationResult<List<UserRoleDto>>> GetAllForTenantAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var dtos = await Context.Set<UserRole>()
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .Select(ur => new UserRoleDto(ur.Id, ur.UserId, ur.RoleId, ur.TenantId, ur.Role.Name, ur.Source))
            .ToListAsync(cancellationToken);

        return OperationResult<List<UserRoleDto>>.Ok(dtos);
    }

    /// <inheritdoc />
    /// <remarks>SYSTEM BYPASS: keeps the assignment's own tenant instead of the current tenant context.</remarks>
    public async Task<OperationResult<UserRoleDto>> AddForTenantAsync(
        UserRoleDto assignment,
        CancellationToken cancellationToken = default)
    {
        var entity = new UserRole
        {
            UserId = assignment.UserId,
            RoleId = assignment.RoleId,
            TenantId = assignment.TenantId,
            Source = assignment.Source
        };
        Context.Set<UserRole>().Add(entity);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Restore the context so a retry on it is not poisoned. The expected cause is a concurrent
            // insert of the same role and source; the caller re-reads.
            Context.Entry(entity).State = EntityState.Detached;
            return OperationResult<UserRoleDto>.Fail(
                "The role assignment already exists.", 409, GroundUp.Core.ErrorCodes.Conflict);
        }

        return OperationResult<UserRoleDto>.Ok(
            new UserRoleDto(entity.Id, entity.UserId, entity.RoleId, entity.TenantId, assignment.RoleName, entity.Source),
            statusCode: 201);
    }

    /// <inheritdoc />
    /// <remarks>SYSTEM BYPASS: targets <paramref name="tenantId"/> explicitly.</remarks>
    public async Task<OperationResult<List<UserRoleDto>>> RemoveAssignmentsAsync(
        Guid userId,
        Guid? roleId,
        Guid tenantId,
        RoleAssignmentSource? source,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Set<UserRole>()
            .Include(ur => ur.Role)
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId);
        if (roleId is not null)
        {
            query = query.Where(ur => ur.RoleId == roleId.Value);
        }

        if (source is not null)
        {
            query = query.Where(ur => ur.Source == source.Value);
        }

        var entities = await query.ToListAsync(cancellationToken);
        if (entities.Count == 0)
        {
            return OperationResult<List<UserRoleDto>>.Ok([]);
        }

        var removed = entities
            .Select(ur => new UserRoleDto(ur.Id, ur.UserId, ur.RoleId, ur.TenantId, ur.Role.Name, ur.Source))
            .ToList();
        Context.Set<UserRole>().RemoveRange(entities);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            foreach (var entity in entities)
            {
                Context.Entry(entity).State = EntityState.Unchanged;
            }

            return OperationResult<List<UserRoleDto>>.Fail(
                "A conflict occurred while removing the role.", 409, GroundUp.Core.ErrorCodes.Conflict);
        }

        return OperationResult<List<UserRoleDto>>.Ok(removed);
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
