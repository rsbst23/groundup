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

        var entities = await DbSet.AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var dtos = entities.Select(AuthUserRoleMapper.ToDto).ToList();
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
        var dtos = await Context.Set<UserRole>()
            .AsNoTracking()
            .Include(ur => ur.Role)
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .Select(ur => new UserRoleDto(ur.Id, ur.UserId, ur.RoleId, ur.TenantId, ur.Role.Name))
            .ToListAsync(cancellationToken);

        return OperationResult<List<UserRoleDto>>.Ok(dtos);
    }

    /// <inheritdoc />
    /// <remarks>
    /// SYSTEM BYPASS: This method intentionally queries the DbContext directly,
    /// bypassing the tenant-filtered DbSet. This is required for resolving system-level
    /// roles that transcend tenant boundaries. Includes the Role navigation property
    /// to populate RoleName in the DTO projection.
    /// </remarks>
    public async Task<OperationResult<List<UserRoleDto>>> GetSystemRolesForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var dtos = await Context.Set<UserRole>()
            .AsNoTracking()
            .Include(ur => ur.Role)
            .Where(ur => ur.UserId == userId && ur.Role.RoleType == RoleType.System)
            .Select(ur => new UserRoleDto(ur.Id, ur.UserId, ur.RoleId, ur.TenantId, ur.Role.Name))
            .ToListAsync(cancellationToken);

        return OperationResult<List<UserRoleDto>>.Ok(dtos);
    }
}
