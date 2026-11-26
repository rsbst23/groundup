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

    /// <inheritdoc />
    /// <remarks>
    /// SYSTEM BYPASS: Targets the given tenant explicitly rather than the current tenant
    /// context, so a user can leave a tenant other than the one their session is scoped to.
    /// </remarks>
    public Task<OperationResult> DeactivateMembershipAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        SetMembershipActiveAsync(userId, tenantId, isActive: false, removeRoles: true,
            "A conflict occurred while leaving the tenant.", cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// SYSTEM BYPASS: Targets the given tenant explicitly rather than the current tenant
    /// context, because sign-in callbacks run before any tenant is selected.
    /// </remarks>
    public Task<OperationResult> ReactivateMembershipAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        SetMembershipActiveAsync(userId, tenantId, isActive: true, removeRoles: false,
            "A conflict occurred while rejoining the tenant.", cancellationToken);

    private async Task<OperationResult> SetMembershipActiveAsync(
        Guid userId,
        Guid tenantId,
        bool isActive,
        bool removeRoles,
        string conflictMessage,
        CancellationToken cancellationToken)
    {
        var entity = await Context.Set<UserTenant>()
            .FirstOrDefaultAsync(ut => ut.UserId == userId && ut.TenantId == tenantId, cancellationToken);

        if (entity is null)
            return OperationResult.NotFound();

        var target = isActive ? MembershipStatus.Active : MembershipStatus.Inactive;
        if (entity.Status == target)
            return OperationResult.Ok();

        entity.Status = target;

        // Leaving also removes the user's role assignments in that tenant, in the same save, so a
        // later rejoin starts clean and a former TenantAdmin does not silently regain admin rights.
        List<UserRole> removedRoles = [];
        if (removeRoles)
        {
            removedRoles = await Context.Set<UserRole>()
                .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
                .ToListAsync(cancellationToken);
            Context.Set<UserRole>().RemoveRange(removedRoles);
        }

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult.Ok();
        }
        catch (DbUpdateException)
        {
            DiscardRejectedChanges(entity);
            foreach (var role in removedRoles)
            {
                // Restore the tracked state so a retry on this context is not poisoned
                Context.Entry(role).State = EntityState.Unchanged;
            }

            return OperationResult.Fail(
                conflictMessage,
                409,
                GroundUp.Core.ErrorCodes.Conflict);
        }
    }
}
