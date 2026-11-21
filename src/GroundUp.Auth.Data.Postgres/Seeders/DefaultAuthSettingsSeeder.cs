using System.Globalization;
using GroundUp.Auth.Core;
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

        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.application.default-tenant-slug",
            DataType: SettingDataType.String,
            DefaultValue: "",
            DisplayName: "Default Tenant Slug",
            Description: "When set, new users with zero memberships auto-join this tenant on first login/sign-up. Leave empty to disable simple sign-up.",
            Category: "Application",
            GroupKey: "auth.application",
            GroupDisplayName: "Auth Application",
            AllowedLevelNames: ["system"]),
            cancellationToken);

        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.application.signup-creates-org",
            DataType: SettingDataType.Bool,
            DefaultValue: "false",
            DisplayName: "Sign-Up Creates Organization",
            Description: "When true, the sign-up flow collects an organization name and creates a new organization. When false, sign-up joins the default tenant.",
            Category: "Application",
            GroupKey: "auth.application",
            GroupDisplayName: "Auth Application",
            AllowedLevelNames: ["system"]),
            cancellationToken);

        // Session timeouts. System administrators set the System value; a tenant may only
        // shorten it (enforced by SessionPolicyWriteValidator and again at resolution time).
        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: SessionSettingKeys.TokenLifetimeMinutes,
            DataType: SettingDataType.Int,
            DefaultValue: SessionSettingKeys.TokenLifetimeDefault.ToString(CultureInfo.InvariantCulture),
            DisplayName: "Token Lifetime (minutes)",
            Description: "How long a sign-in token is valid. A user who makes no request for this long must sign in again. A tenant value cannot be longer than the System value.",
            Category: "Sessions",
            GroupKey: "auth.session",
            GroupDisplayName: "Security > Sessions",
            AllowedLevelNames: ["system", "tenant"],
            MinValue: SessionSettingKeys.TokenLifetimeMin.ToString(CultureInfo.InvariantCulture),
            MaxValue: SessionSettingKeys.TokenLifetimeMax.ToString(CultureInfo.InvariantCulture)),
            cancellationToken);

        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: SessionSettingKeys.AbsoluteLifetimeMinutes,
            DataType: SettingDataType.Int,
            DefaultValue: SessionSettingKeys.AbsoluteLifetimeDefault.ToString(CultureInfo.InvariantCulture),
            DisplayName: "Absolute Session Lifetime (minutes)",
            Description: "The longest a session can last from the user's last real sign-in, however active they are. Must be at least the token lifetime. A tenant value cannot be longer than the System value.",
            Category: "Sessions",
            GroupKey: "auth.session",
            GroupDisplayName: "Security > Sessions",
            AllowedLevelNames: ["system", "tenant"],
            MinValue: SessionSettingKeys.AbsoluteLifetimeMin.ToString(CultureInfo.InvariantCulture),
            MaxValue: SessionSettingKeys.AbsoluteLifetimeMax.ToString(CultureInfo.InvariantCulture)),
            cancellationToken);

        _logger.LogInformation("Auth settings definitions seeded successfully");
    }
}
