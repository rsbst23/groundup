using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;
using GroundUp.Data.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Data.Postgres.Seeders;

/// <summary>
/// Seeds all built-in permission definitions on application startup.
/// Permissions are global (not tenant-scoped) and identified by their unique <c>Key</c>.
/// Idempotent — only creates permissions that don't already exist.
/// </summary>
public sealed class DefaultPermissionSeeder : IDataSeeder
{
    private readonly AuthDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultPermissionSeeder"/>.
    /// </summary>
    /// <param name="dbContext">The auth database context.</param>
    public DefaultPermissionSeeder(AuthDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingKeys = await _dbContext.Permissions
            .AsNoTracking()
            .Select(p => p.Key)
            .ToListAsync(cancellationToken);

        var existingKeySet = new HashSet<string>(existingKeys, StringComparer.OrdinalIgnoreCase);

        var toAdd = PermissionKeysAuth.All
            .Where(p => !existingKeySet.Contains(p.Key))
            .Select(p => new Permission
            {
                Key = p.Key,
                Name = p.Name,
                Description = p.Description,
                Module = p.Module
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            _dbContext.Permissions.AddRange(toAdd);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
