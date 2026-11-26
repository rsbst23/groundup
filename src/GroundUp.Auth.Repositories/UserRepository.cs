using System.Data.Common;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Repositories.Mappers;
using GroundUp.Core.Results;
using GroundUp.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Repositories;

/// <summary>
/// Repository implementation for User entities. Extends BaseRepository with
/// custom lookup methods by external user ID and email address.
/// </summary>
public sealed class UserRepository : BaseRepository<User, UserDto>, IUserRepository
{
    /// <summary>
    /// Initializes a new instance of <see cref="UserRepository"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    public UserRepository(DbContext context)
        : base(context, AuthUserMapper.ToDto, AuthUserMapper.ToEntity)
    {
    }

    /// <inheritdoc />
    public async Task<OperationResult<UserDto>> GetByExternalUserIdAsync(
        string externalUserId,
        CancellationToken cancellationToken = default)
    {
        var entity = await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(u => u.ExternalUserId == externalUserId, cancellationToken);

        if (entity is null)
            return OperationResult<UserDto>.NotFound();

        return OperationResult<UserDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult<UserDto>> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.ToLower();

        var entity = await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (entity is null)
            return OperationResult<UserDto>.NotFound();

        return OperationResult<UserDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult<List<UserDto>>> GetAllByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLower();

        var entities = await DbSet.AsNoTracking()
            .Where(u => u.MergedIntoUserId == null && u.Email.ToLower() == normalizedEmail)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(cancellationToken);

        return OperationResult<List<UserDto>>.Ok(entities.Select(MapToDto).ToList());
    }

    /// <inheritdoc />
    public async Task<OperationResult<List<UserDto>>> GetUnlinkedByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        // lower() matches the IX_AuthUsers_Email_Lower expression index
        var normalizedEmail = email.Trim().ToLower();

        var entities = await DbSet.AsNoTracking()
            .Where(u => u.ExternalUserId == null
                && u.MergedIntoUserId == null
                && u.Email.ToLower() == normalizedEmail)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(cancellationToken);

        return OperationResult<List<UserDto>>.Ok(entities.Select(MapToDto).ToList());
    }

    /// <inheritdoc />
    /// <remarks>
    /// A conditional update (<c>WHERE ExternalUserId IS NULL</c>), so of two concurrent claims of
    /// the same pending user only one links it. Runs inside the caller's transaction when there is
    /// one, otherwise in its own, so the user and its memberships change together.
    /// </remarks>
    public async Task<OperationResult<UserDto>> LinkIdentityAsync(
        Guid userId,
        string externalUserId,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var ownTransaction = Context.Database.CurrentTransaction is null
            ? await Context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var now = DateTime.UtcNow;
            var linked = await DbSet
                .Where(u => u.Id == userId && u.ExternalUserId == null && u.MergedIntoUserId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.ExternalUserId, externalUserId)
                    // The pending user's display name is a placeholder (the email) until now
                    .SetProperty(u => u.DisplayName, u => displayName != null && u.DisplayName == u.Email ? displayName : u.DisplayName)
                    .SetProperty(u => u.UpdatedAt, now),
                    cancellationToken);

            if (linked == 0)
            {
                if (ownTransaction is not null)
                {
                    await ownTransaction.RollbackAsync(cancellationToken);
                }

                return OperationResult<UserDto>.Fail(
                    "The user is already linked to an identity.", 409, GroundUp.Core.ErrorCodes.Conflict);
            }

            await Context.Set<UserTenant>()
                .Where(ut => ut.UserId == userId && ut.ExternalUserId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(ut => ut.ExternalUserId, externalUserId)
                    .SetProperty(ut => ut.UpdatedAt, now),
                    cancellationToken);

            if (ownTransaction is not null)
            {
                await ownTransaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbException)
        {
            // The identity is already linked to another user (unique ExternalUserId)
            if (ownTransaction is not null)
            {
                await ownTransaction.RollbackAsync(cancellationToken);
            }

            return OperationResult<UserDto>.Fail(
                "The identity is already linked to another user.", 409, GroundUp.Core.ErrorCodes.Conflict);
        }
        finally
        {
            if (ownTransaction is not null)
            {
                await ownTransaction.DisposeAsync();
            }
        }

        var user = await DbSet.AsNoTracking().FirstAsync(u => u.Id == userId, cancellationToken);
        return OperationResult<UserDto>.Ok(MapToDto(user));
    }

    /// <inheritdoc />
    public async Task<OperationResult<List<Guid>>> MergePendingUserAsync(
        Guid fromUserId,
        Guid toUserId,
        CancellationToken cancellationToken = default)
    {
        var from = await DbSet.FirstOrDefaultAsync(u => u.Id == fromUserId, cancellationToken);
        var to = await DbSet.AsNoTracking().FirstOrDefaultAsync(u => u.Id == toUserId, cancellationToken);
        if (from is null || to is null)
        {
            return OperationResult<List<Guid>>.NotFound("User not found.");
        }

        if (from.ExternalUserId is not null || from.MergedIntoUserId is not null)
        {
            return OperationResult<List<Guid>>.Fail(
                "Only a pending user that was never linked or merged can be merged.", 409, GroundUp.Core.ErrorCodes.Conflict);
        }

        var fromMemberships = await Context.Set<UserTenant>().Where(ut => ut.UserId == fromUserId).ToListAsync(cancellationToken);
        var toMemberships = await Context.Set<UserTenant>().Where(ut => ut.UserId == toUserId).ToListAsync(cancellationToken);
        var fromRoles = await Context.Set<UserRole>().Where(ur => ur.UserId == fromUserId).ToListAsync(cancellationToken);
        var toRoleKeys = (await Context.Set<UserRole>().AsNoTracking()
                .Where(ur => ur.UserId == toUserId)
                .Select(ur => new { ur.RoleId, ur.TenantId, ur.Source })
                .ToListAsync(cancellationToken))
            .Select(k => (k.RoleId, k.TenantId, k.Source))
            .ToHashSet();

        var affectedTenants = new List<Guid>();
        foreach (var membership in fromMemberships)
        {
            var target = toMemberships.FirstOrDefault(m => m.TenantId == membership.TenantId);
            var rolesInTenant = fromRoles.Where(r => r.TenantId == membership.TenantId).ToList();

            if (target is { Status: MembershipStatus.Active })
            {
                // Already a member: the pending membership and its roles are dropped
                Context.Set<UserRole>().RemoveRange(rolesInTenant);
                Context.Set<UserTenant>().Remove(membership);
                continue;
            }

            affectedTenants.Add(membership.TenantId);
            if (target is null)
            {
                membership.UserId = toUserId;
                membership.ExternalUserId = to.ExternalUserId;
            }
            else
            {
                if (membership.Status == MembershipStatus.Pending)
                {
                    target.Status = MembershipStatus.Pending;
                }

                Context.Set<UserTenant>().Remove(membership);
            }

            foreach (var role in rolesInTenant)
            {
                if (toRoleKeys.Add((role.RoleId, role.TenantId, role.Source)))
                {
                    role.UserId = toUserId;
                }
                else
                {
                    Context.Set<UserRole>().Remove(role);
                }
            }
        }

        // Roles without a membership row (should not exist) go with the user's record
        foreach (var orphan in fromRoles.Where(r => fromMemberships.All(m => m.TenantId != r.TenantId)))
        {
            Context.Set<UserRole>().Remove(orphan);
        }

        from.MergedIntoUserId = toUserId;
        from.IsActive = false;

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            Context.ChangeTracker.Clear();
            return OperationResult<List<Guid>>.Fail(
                "A conflict occurred while merging the users.", 409, GroundUp.Core.ErrorCodes.Conflict);
        }

        return OperationResult<List<Guid>>.Ok(affectedTenants);
    }
}
