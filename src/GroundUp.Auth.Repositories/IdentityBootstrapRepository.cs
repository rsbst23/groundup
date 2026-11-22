using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Repositories;

/// <summary>
/// <see cref="IIdentityBootstrapRepository"/> over EF Core.
/// <para>
/// SYSTEM BYPASS: every query uses <c>DbContext.Set&lt;T&gt;()</c> without a tenant filter, on purpose
/// (see the interface). Database-agnostic: unique violations are recognized through
/// <see cref="IUniqueConstraintDetector"/>, registered by the provider project.
/// </para>
/// </summary>
public sealed class IdentityBootstrapRepository : IIdentityBootstrapRepository
{
    private readonly DbContext _context;
    private readonly IUniqueConstraintDetector _uniqueConstraintDetector;

    /// <summary>
    /// Initializes a new instance of <see cref="IdentityBootstrapRepository"/>.
    /// </summary>
    /// <param name="context">The auth database context.</param>
    /// <param name="uniqueConstraintDetector">Recognizes unique-constraint violations for the provider.</param>
    public IdentityBootstrapRepository(DbContext context, IUniqueConstraintDetector uniqueConstraintDetector)
    {
        _context = context;
        _uniqueConstraintDetector = uniqueConstraintDetector;
    }

    /// <inheritdoc />
    public async Task<Guid?> FindRoleIdByNameAsync(string roleName, CancellationToken cancellationToken = default)
        => await _context.Set<Role>().AsNoTracking()
            .Where(r => r.Name == roleName)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> RoleExistsAsync(string roleName, CancellationToken cancellationToken = default)
        => _context.Set<Role>().AsNoTracking().AnyAsync(r => r.Name == roleName, cancellationToken);

    /// <inheritdoc />
    public async Task<UserDto?> FindUserByEmailAsync(string email, CancellationToken cancellationToken = default)
        => await _context.Set<User>().AsNoTracking()
            .Where(u => u.Email == email)
            .Select(u => new UserDto(u.Id, u.ExternalUserId, u.Email, u.DisplayName, u.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _context.Set<Tenant>().AsNoTracking().AnyAsync(t => t.Id == tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> AnyUserHasRoleAsync(string roleName, CancellationToken cancellationToken = default)
        => _context.Set<UserRole>().AsNoTracking().AnyAsync(ur => ur.Role.Name == roleName, cancellationToken);

    /// <inheritdoc />
    public async Task<Guid?> GetFirstUserIdWithRoleAsync(string roleName, CancellationToken cancellationToken = default)
        => await _context.Set<UserRole>().AsNoTracking()
            .Where(ur => ur.Role.Name == roleName)
            .OrderBy(ur => ur.UserId)
            .Select(ur => (Guid?)ur.UserId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<Guid>> EnsureSuperAdminAsync(
        SuperAdminRecord record, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // User (by email)
            var userId = await _context.Set<User>()
                .Where(u => u.Email == record.Email)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (userId is null)
            {
                var user = new User
                {
                    Email = record.Email,
                    DisplayName = record.DisplayName,
                    ExternalUserId = record.ExternalUserId,
                    IsActive = true
                };
                _context.Set<User>().Add(user);
                await _context.SaveChangesAsync(cancellationToken);
                userId = user.Id;
            }

            // Membership
            var hasMembership = await _context.Set<UserTenant>()
                .AnyAsync(ut => ut.UserId == userId && ut.TenantId == record.TenantId, cancellationToken);
            if (!hasMembership)
            {
                _context.Set<UserTenant>().Add(new UserTenant
                {
                    UserId = userId.Value,
                    TenantId = record.TenantId,
                    ExternalUserId = record.ExternalUserId,
                    IsActive = true
                });
                await _context.SaveChangesAsync(cancellationToken);
            }

            // Role assignment
            var hasRole = await _context.Set<UserRole>()
                .AnyAsync(ur => ur.UserId == userId && ur.RoleId == record.RoleId, cancellationToken);
            if (!hasRole)
            {
                _context.Set<UserRole>().Add(new UserRole
                {
                    UserId = userId.Value,
                    RoleId = record.RoleId,
                    TenantId = record.TenantId
                });
                await _context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return OperationResult<Guid>.Ok(userId.Value);
        }
        catch (DbUpdateException ex) when (_uniqueConstraintDetector.IsUniqueConstraintViolation(ex))
        {
            // The transaction rolls back when disposed; detach the rejected rows so the context stays usable
            _context.ChangeTracker.Clear();
            return OperationResult<Guid>.Fail(
                "A super admin user already exists with conflicting attributes.", 409, ErrorCodes.Conflict);
        }
    }
}
