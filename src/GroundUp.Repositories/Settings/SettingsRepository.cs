using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Data.Abstractions;
using GroundUp.Repositories.Mappers;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Repositories.Settings;

/// <summary>
/// <see cref="ISettingsRepository"/> over the application's DbContext.
/// </summary>
public sealed class SettingsRepository : ISettingsRepository
{
    private readonly DbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsRepository"/> class.
    /// </summary>
    /// <param name="context">The application's DbContext (contains the settings entities).</param>
    public SettingsRepository(DbContext context)
    {
        _context = context;
    }

    private DbSet<SettingLevel> Levels => _context.Set<SettingLevel>();

    /// <inheritdoc />
    public async Task<Guid?> GetLevelIdByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await Levels
            .AsNoTracking()
            .Where(l => l.Name == name)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> GetLevelNameByIdAsync(Guid levelId, CancellationToken cancellationToken = default)
    {
        return await Levels
            .AsNoTracking()
            .Where(l => l.Id == levelId)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SettingLevelDto> EnsureLevelAsync(
        string name, Guid? parentId, int displayOrder, CancellationToken cancellationToken = default)
    {
        var existing = await Levels
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Name == name, cancellationToken);

        if (existing is not null)
        {
            return SettingLevelMapper.ToDto(existing);
        }

        var level = new SettingLevel
        {
            Name = name,
            ParentId = parentId,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };

        Levels.Add(level);
        await _context.SaveChangesAsync(cancellationToken);

        return SettingLevelMapper.ToDto(level);
    }
}
