using GroundUp.Auth.Core;
using GroundUp.Core.Entities.Settings;
using GroundUp.Data.Abstractions;
using GroundUp.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Sample.Dev;

/// <summary>
/// Development only (GU-90): points <c>auth.invitations.accept-url</c> at the sample web app's accept
/// page (<see cref="AcceptUrl"/>) at the System level, so invitations can be sent without a manual
/// settings step. Only when the System level has no value or an empty one; any other value is kept.
/// <para>
/// Writes through <see cref="SampleDbContext"/>: the settings service is behind the permission proxy,
/// and seeders run without a signed-in user. Runs after the auth module defines the setting.
/// </para>
/// </summary>
public sealed class DevSettingsSeeder : IDataSeeder
{
    /// <summary>The sample web app's accept page on the Vite dev server's default port.</summary>
    public const string AcceptUrl = "http://localhost:5173/invite/{token}";

    private readonly SampleDbContext _dbContext;
    private readonly ILogger<DevSettingsSeeder> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DevSettingsSeeder"/>.
    /// </summary>
    public DevSettingsSeeder(SampleDbContext dbContext, ILogger<DevSettingsSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>After the auth settings (30) and the sample's settings levels (100).</remarks>
    public int Order => 110;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var definition = await _dbContext.Set<SettingDefinition>()
            .FirstOrDefaultAsync(d => d.Key == InvitationSettingKeys.AcceptUrl, cancellationToken);
        var systemLevel = await _dbContext.Set<SettingLevel>()
            .Where(l => l.ParentId == null)
            .OrderBy(l => l.Name == "System" ? 0 : 1)
            .ThenBy(l => l.DisplayOrder)
            .FirstOrDefaultAsync(cancellationToken);
        if (definition is null || systemLevel is null)
        {
            _logger.LogWarning(
                "The {Setting} setting or the System level does not exist yet; the dev accept URL was not seeded",
                InvitationSettingKeys.AcceptUrl);
            return;
        }

        var value = await _dbContext.Set<SettingValue>()
            .FirstOrDefaultAsync(
                v => v.SettingDefinitionId == definition.Id && v.LevelId == systemLevel.Id && v.ScopeId == null,
                cancellationToken);
        if (value is not null && !string.IsNullOrWhiteSpace(value.Value))
        {
            return;
        }

        if (value is null)
        {
            _dbContext.Set<SettingValue>().Add(new SettingValue
            {
                SettingDefinitionId = definition.Id,
                LevelId = systemLevel.Id,
                ScopeId = null,
                Value = AcceptUrl
            });
        }
        else
        {
            value.Value = AcceptUrl;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Development: {Setting} set to {AcceptUrl}", InvitationSettingKeys.AcceptUrl, AcceptUrl);
    }
}
