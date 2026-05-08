using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
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
}
