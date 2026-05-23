using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Enums;
using GroundUp.Data.Abstractions;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Data.Postgres.Seeders;

/// <summary>
/// Seeds auth module system settings on application startup.
/// Uses <see cref="ISettingsService.EnsureDefinitionAsync"/> for idempotent creation.
/// Order = 30 (runs after core settings infrastructure is seeded).
/// </summary>
public sealed class DefaultAuthSettingsSeeder : IDataSeeder
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<DefaultAuthSettingsSeeder> _logger;

    /// <inheritdoc />
    public int Order => 30;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultAuthSettingsSeeder"/>.
    /// </summary>
    /// <param name="settingsService">The settings service for idempotent definition creation.</param>
    /// <param name="logger">Logger for seeding progress.</param>
    public DefaultAuthSettingsSeeder(
        ISettingsService settingsService,
        ILogger<DefaultAuthSettingsSeeder> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Seeding auth settings definitions...");

        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.application.default-domain",
            DataType: SettingDataType.String,
            DefaultValue: "",
            DisplayName: "Default Domain",
            Description: "The default domain for auth cookies. Empty = host-only cookie.",
            Category: "Application",
            GroupKey: "auth.application",
            GroupDisplayName: "Auth Application",
            AllowedLevelNames: ["system"]),
            cancellationToken);

        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.keycloak.shared-realm-name",
            DataType: SettingDataType.String,
            DefaultValue: "groundup",
            DisplayName: "Shared Realm Name",
            Description: "The Keycloak realm name used for shared (non-enterprise) authentication.",
            Category: "Keycloak",
            GroupKey: "auth.keycloak",
            GroupDisplayName: "Auth Keycloak",
            AllowedLevelNames: ["system"]),
            cancellationToken);

        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.keycloak.public-base-url",
            DataType: SettingDataType.String,
            DefaultValue: "http://localhost:8080",
            DisplayName: "Keycloak Public Base URL",
            Description: "The public-facing base URL for Keycloak (used in browser redirects).",
            Category: "Keycloak",
            GroupKey: "auth.keycloak",
            GroupDisplayName: "Auth Keycloak",
            AllowedLevelNames: ["system"]),
            cancellationToken);

        _logger.LogInformation("Auth settings definitions seeded successfully");
    }
}
