using GroundUp.Data.Abstractions;

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

    private readonly ISettingsRepository _settingsRepository;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultSettingLevelSeeder"/>.
    /// </summary>
    /// <param name="settingsRepository">Creates the levels when missing.</param>
    public DefaultSettingLevelSeeder(ISettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
    }

    /// <inheritdoc />
    public int Order => 5;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var system = await _settingsRepository.EnsureLevelAsync(SystemLevelName, null, 0, cancellationToken);
        await _settingsRepository.EnsureLevelAsync(TenantLevelName, system.Id, 1, cancellationToken);
    }
}
