using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Repositories.Mappers;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
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
    public Task<OperationResult<List<UserRoleDto>>> DeactivateMembershipAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        SetMembershipStatusAsync(userId, tenantId, MembershipStatus.Inactive, removeRoles: true,
            "A conflict occurred while leaving the tenant.", cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// SYSTEM BYPASS: Targets the given tenant explicitly rather than the current tenant
    /// context, because sign-in callbacks run before any tenant is selected.
    /// </remarks>
    public async Task<OperationResult> ReactivateMembershipAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = await SetMembershipStatusAsync(userId, tenantId, MembershipStatus.Active, removeRoles: false,
            "A conflict occurred while rejoining the tenant.", cancellationToken);
        return result.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Message, result.StatusCode, result.ErrorCode);
    }

    /// <inheritdoc />
    /// <remarks>SYSTEM BYPASS: targets <paramref name="tenantId"/> explicitly.</remarks>
    public async Task<OperationResult> MarkPendingAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = await SetMembershipStatusAsync(userId, tenantId, MembershipStatus.Pending, removeRoles: false,
            "A conflict occurred while inviting the former member.", cancellationToken);
        return result.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Message, result.StatusCode, result.ErrorCode);
    }

    /// <inheritdoc />
    /// <remarks>SYSTEM BYPASS: keeps the membership's own tenant instead of the current tenant context.</remarks>
    public async Task<OperationResult<UserTenantDto>> AddForTenantAsync(
        UserTenantDto membership,
        CancellationToken cancellationToken = default)
    {
        var entity = new UserTenant
        {
            UserId = membership.UserId,
            TenantId = membership.TenantId,
            ExternalUserId = membership.ExternalUserId,
            Status = membership.Status
        };
        Context.Set<UserTenant>().Add(entity);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The expected cause is a concurrent insert for the same user and tenant; the caller re-reads.
            Context.Entry(entity).State = EntityState.Detached;
            return OperationResult<UserTenantDto>.Fail(
                "The membership already exists.", 409, GroundUp.Core.ErrorCodes.Conflict);
        }

        return OperationResult<UserTenantDto>.Ok(AuthUserTenantMapper.ToDto(entity), statusCode: 201);
    }

    /// <inheritdoc />
    /// <remarks>SYSTEM BYPASS: targets <paramref name="tenantId"/> explicitly.</remarks>
    public async Task<OperationResult<PaginatedData<TenantMemberDto>>> GetMembersAsync(
        Guid tenantId,
        FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        var options = new ListQueryOptions<UserTenant>
        {
            Fields = new ListFieldMap<UserTenant>()
                .Sort("email", ut => ut.User.Email)
                .Sort("displayName", ut => ut.User.DisplayName)
                .SortAndFilter("status", ut => ut.Status)
                .Search(ut => ut.User.Email, ut => ut.User.DisplayName),
            DefaultOrder = q => q.OrderBy(ut => ut.User.Email),
            TieBreaker = ut => ut.UserId
        };

        var paged = await Context.Set<UserTenant>()
            .AsNoTracking()
            .Where(ut => ut.TenantId == tenantId
                && (ut.Status == MembershipStatus.Active || ut.Status == MembershipStatus.Pending))
            .ToPagedListAsync(
                filterParams,
                ut => new { ut.UserId, ut.User.Email, ut.User.DisplayName, ut.Status },
                options,
                cancellationToken);
        var page = paged.Items;

        var userIds = page.Select(m => m.UserId).ToList();
        var assignments = await Context.Set<UserRole>()
            .AsNoTracking()
            .Where(ur => ur.TenantId == tenantId && userIds.Contains(ur.UserId))
            .Select(ur => new { ur.UserId, ur.RoleId, RoleName = ur.Role.Name, ur.Source })
            .ToListAsync(cancellationToken);

        var items = page
            .Select(m => new TenantMemberDto(
                m.UserId,
                m.Email,
                m.DisplayName,
                m.Status,
                assignments
                    .Where(a => a.UserId == m.UserId)
                    .GroupBy(a => new { a.RoleId, a.RoleName })
                    .OrderBy(g => g.Key.RoleName)
                    .Select(g => new MemberRoleDto(g.Key.RoleId, g.Key.RoleName, g.Select(a => a.Source).OrderBy(s => s).ToList()))
                    .ToList()))
            .ToList();

        return OperationResult<PaginatedData<TenantMemberDto>>.Ok(new PaginatedData<TenantMemberDto>
        {
            Items = items,
            PageNumber = paged.PageNumber,
            PageSize = paged.PageSize,
            TotalRecords = paged.TotalRecords
        });
    }

    private async Task<OperationResult<List<UserRoleDto>>> SetMembershipStatusAsync(
        Guid userId,
        Guid tenantId,
        MembershipStatus target,
        bool removeRoles,
        string conflictMessage,
        CancellationToken cancellationToken)
    {
        var entity = await Context.Set<UserTenant>()
            .FirstOrDefaultAsync(ut => ut.UserId == userId && ut.TenantId == tenantId, cancellationToken);

        if (entity is null)
            return OperationResult<List<UserRoleDto>>.NotFound();

        if (entity.Status == target)
            return OperationResult<List<UserRoleDto>>.Ok([]);

        // A pending invitation exists only while its membership is Pending. A membership activated
        // another way (default-tenant sign-in, join link) closes it as Accepted in the same save, so
        // the link cannot be used afterwards (GU-68 Requirement 9.4).
        var closedInvitations = new List<Invitation>();
        if (entity.Status == MembershipStatus.Pending && target == MembershipStatus.Active)
        {
            closedInvitations = await Context.Set<Invitation>()
                .Where(i => i.UserId == userId && i.TenantId == tenantId && i.Status == InvitationStatus.Pending)
                .ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var invitation in closedInvitations)
            {
                invitation.Status = InvitationStatus.Accepted;
                invitation.AcceptedByUserId = userId;
                invitation.AcceptedAt = now;
            }
        }

        entity.Status = target;

        // Leaving also removes the user's role assignments in that tenant (every source), in the same
        // save, so a later rejoin starts clean and a former TenantAdmin does not silently regain admin
        // rights. The removed rows are returned so the caller can publish the role-removed events.
        List<UserRole> removedRoles = [];
        if (removeRoles)
        {
            removedRoles = await Context.Set<UserRole>()
                .Include(ur => ur.Role)
                .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
                .ToListAsync(cancellationToken);
            Context.Set<UserRole>().RemoveRange(removedRoles);
        }

        var removed = removedRoles
            .Select(ur => new UserRoleDto(ur.Id, ur.UserId, ur.RoleId, ur.TenantId, ur.Role.Name, ur.Source))
            .ToList();

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult<List<UserRoleDto>>.Ok(removed);
        }
        catch (DbUpdateException)
        {
            DiscardRejectedChanges(entity);
            foreach (var role in removedRoles)
            {
                // Restore the tracked state so a retry on this context is not poisoned
                Context.Entry(role).State = EntityState.Unchanged;
            }

            foreach (var invitation in closedInvitations)
            {
                var entry = Context.Entry(invitation);
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }

            return OperationResult<List<UserRoleDto>>.Fail(
                conflictMessage,
                409,
                GroundUp.Core.ErrorCodes.Conflict);
        }
    }
}