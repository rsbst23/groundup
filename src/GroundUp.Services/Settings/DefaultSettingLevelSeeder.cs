using GroundUp.Core.Entities.Settings;
using GroundUp.Data.Abstractions;
using GroundUp.Data.Postgres;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Services.Settings;

/// <summary>
/// Ensures the two setting levels every GroundUp module relies on, "System" (root) and "Tenant"
/// (child of System), exist before any module seeds its setting definitions. Applications may
/// add deeper levels (for example Case, Group, User) in their own seeders and may seed these two
/// themselves: the seeder only creates a level that does not exist, matched by name.
/// <para>
/// Order = 5, ahead of every module seeder that defines settings (they look levels up by name).
/// </para>
/// </summary>
public sealed class DefaultSettingLevelSeeder : IDataSeeder
{
    /// <summary>The name of the root level.</summary>
    public const string SystemLevelName = "System";

    /// <summary>The name of the tenant level.</summary>
    public const string TenantLevelName = "Tenant";

    private readonly GroundUpDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultSettingLevelSeeder"/>.
    /// </summary>
    /// <param name="dbContext">The EF Core database context.</param>
    public DefaultSettingLevelSeeder(GroundUpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public int Order => 5;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var system = await EnsureLevelAsync(SystemLevelName, null, 0, cancellationToken);
        await EnsureLevelAsync(TenantLevelName, system.Id, 1, cancellationToken);
    }

    private async Task<SettingLevel> EnsureLevelAsync(
        string name,
        Guid? parentId,
        int displayOrder,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Set<SettingLevel>()
            .FirstOrDefaultAsync(l => l.Name == name, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var level = new SettingLevel
        {
            Name = name,
            ParentId = parentId,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<SettingLevel>().Add(level);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return level;
    }
}
