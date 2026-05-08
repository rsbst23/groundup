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
/// Repository implementation for UserTenant junction entities. Extends BaseTenantRepository
/// with automatic tenant isolation and provides methods for querying user memberships
/// within the current tenant and across all tenants (system bypass).
/// </summary>
public sealed class UserTenantRepository : BaseTenantRepository<UserTenant, UserTenantDto>, IUserTenantRepository
{
    /// <summary>
    /// Initializes a new instance of <see cref="UserTenantRepository"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    /// <param name="tenantContext">Provides the current tenant identity for automatic filtering.</param>
    public UserTenantRepository(DbContext context, ITenantContext tenantContext)
        : base(context, tenantContext, AuthUserTenantMapper.ToDto, AuthUserTenantMapper.ToEntity)
    {
    }

    /// <inheritdoc />
    public async Task<OperationResult<UserTenantDto>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantContext.TenantId;

        var entity = await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(ut => ut.UserId == userId && ut.TenantId == tenantId, cancellationToken);

        if (entity is null)
            return OperationResult<UserTenantDto>.NotFound();

        return OperationResult<UserTenantDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    /// <remarks>
    /// SYSTEM BYPASS: This method intentionally queries the DbContext directly,
    /// bypassing the tenant-filtered DbSet. This is required for the multi-tenant
    /// selection auth flow where a user needs to see all their memberships to choose
    /// which tenant to operate in.
    /// </remarks>
    public async Task<OperationResult<List<UserTenantDto>>> GetAllMembershipsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var entities = await Context.Set<UserTenant>()
            .AsNoTracking()
            .Where(ut => ut.UserId == userId)
            .ToListAsync(cancellationToken);

        var dtos = entities.Select(AuthUserTenantMapper.ToDto).ToList();
        return OperationResult<List<UserTenantDto>>.Ok(dtos);
    }
}
