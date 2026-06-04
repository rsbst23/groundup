using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Core;
using GroundUp.Core.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Bootstrap;

/// <summary>
/// Provisions the first SuperAdmin user in the auth-module database within a single
/// transaction. Uses explicit existence checks before each insert for idempotent
/// retry support — partial failures from a previous attempt do not cause
/// primary-key or unique-constraint violations on retry.
/// </summary>
public sealed class IdentityBootstrapService : IIdentityBootstrapService
{
    private readonly AuthDbContext _dbContext;
    private readonly ILogger<IdentityBootstrapService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="IdentityBootstrapService"/>.
    /// </summary>
    /// <param name="dbContext">The auth module's EF Core database context.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public IdentityBootstrapService(
        AuthDbContext dbContext,
        ILogger<IdentityBootstrapService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<BootstrapAdminResultDto>> ProvisionFirstSuperAdminAsync(
        ProvisionFirstSuperAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Look up the SuperAdmin role
        var superAdminRole = await _dbContext.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == "SuperAdmin", cancellationToken);

        if (superAdminRole is null)
        {
            _logger.LogError("SuperAdmin role is missing from the database. Verify the auth seeders have run.");
            return OperationResult<BootstrapAdminResultDto>.Fail(
                "SuperAdmin role is missing — verify the auth seeders have run.", 500);
        }

        // 2. Check for existing user with matching email
        var existingUser = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (existingUser is not null)
        {
            if (existingUser.DisplayName == request.DisplayName)
            {
                // Idempotent — same user already exists
                _logger.LogInformation(
                    "SuperAdmin user {Email} already exists with matching attributes. Returning idempotent success.",
                    request.Email);

                return OperationResult<BootstrapAdminResultDto>.Ok(
                    new BootstrapAdminResultDto(existingUser.Id, existingUser.Email, AlreadyExisted: true));
            }

            // Conflict — same email but different display name
            _logger.LogWarning(
                "A super admin user already exists with email {Email} but conflicting display name.",
                request.Email);

            return OperationResult<BootstrapAdminResultDto>.Fail(
                "A super admin user already exists with conflicting attributes.",
                409,
                ErrorCodes.Conflict);
        }

        // 3. Begin a database transaction
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // 4. Explicit existence checks before each insert

            // 4a. User row
            var userId = Guid.NewGuid(); // UuidV7ValueGenerator will handle it on save
            var userExists = await _dbContext.Users
                .AnyAsync(u => u.Email == request.Email, cancellationToken);

            if (!userExists)
            {
                var user = new User
                {
                    Id = userId,
                    Email = request.Email,
                    DisplayName = request.DisplayName,
                    ExternalUserId = request.ExternalUserId,
                    IsActive = true
                };

                _dbContext.Users.Add(user);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Created SuperAdmin user {Email} with ID {UserId}.", request.Email, userId);
            }
            else
            {
                // User was created between our initial check and the transaction — reload the ID
                var existing = await _dbContext.Users
                    .FirstAsync(u => u.Email == request.Email, cancellationToken);
                userId = existing.Id;
            }

            // 4b. UserTenant row
            var userTenantExists = await _dbContext.UserTenants
                .AnyAsync(ut => ut.UserId == userId && ut.TenantId == request.TenantId, cancellationToken);

            if (!userTenantExists)
            {
                var userTenant = new UserTenant
                {
                    UserId = userId,
                    TenantId = request.TenantId,
                    ExternalUserId = request.ExternalUserId,
                    IsActive = true
                };

                _dbContext.UserTenants.Add(userTenant);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Created UserTenant for user {UserId} in tenant {TenantId}.",
                    userId, request.TenantId);
            }

            // 4c. UserRole row (SuperAdmin)
            var userRoleExists = await _dbContext.UserRoles
                .AnyAsync(ur => ur.UserId == userId && ur.RoleId == superAdminRole.Id, cancellationToken);

            if (!userRoleExists)
            {
                var userRole = new UserRole
                {
                    UserId = userId,
                    RoleId = superAdminRole.Id,
                    TenantId = request.TenantId
                };

                _dbContext.UserRoles.Add(userRole);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Assigned SuperAdmin role to user {UserId} in tenant {TenantId}.",
                    userId, request.TenantId);
            }

            // 5. Commit transaction
            await transaction.CommitAsync(cancellationToken);

            // 7. Return success
            return OperationResult<BootstrapAdminResultDto>.Ok(
                new BootstrapAdminResultDto(userId, request.Email, AlreadyExisted: false));
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // 6. Translate unique-constraint violation to Conflict
            _logger.LogWarning(ex,
                "Unique constraint violation while provisioning SuperAdmin user {Email}.",
                request.Email);

            return OperationResult<BootstrapAdminResultDto>.Fail(
                "A super admin user already exists with conflicting attributes.",
                409,
                ErrorCodes.Conflict);
        }
    }

    /// <inheritdoc />
    public async Task<bool> HasSuperAdminAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserRoles
            .AsNoTracking()
            .AnyAsync(ur => ur.Role.Name == "SuperAdmin", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Guid?> GetSuperAdminUserIdAsync(CancellationToken cancellationToken = default)
    {
        var userRole = await _dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => ur.Role.Name == "SuperAdmin")
            .OrderBy(ur => ur.UserId)
            .Select(ur => new { ur.UserId })
            .FirstOrDefaultAsync(cancellationToken);

        return userRole?.UserId;
    }

    /// <inheritdoc />
    public async Task<bool> HasSystemTenantAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Id == AuthRoleNames.SystemTenantId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HasSuperAdminRoleAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Roles
            .AsNoTracking()
            .AnyAsync(r => r.Name == "SuperAdmin", cancellationToken);
    }

    /// <summary>
    /// Determines whether a <see cref="DbUpdateException"/> is caused by a unique constraint violation.
    /// Checks for Postgres error code 23505 (unique_violation).
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // Postgres unique_violation error code
        return ex.InnerException is Npgsql.PostgresException pgEx
            && pgEx.SqlState == "23505";
    }
}
