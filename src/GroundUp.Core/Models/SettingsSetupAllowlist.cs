namespace GroundUp.Core.Models;

/// <summary>
/// The setting keys the setup wizard writes at the System level before any user or permission
/// exists. While setup is incomplete, the settings authorizer allows a write to these keys
/// without a signed-in user. After setup completes, the list no longer applies.
/// </summary>
public static class SettingsSetupAllowlist
{
    /// <summary>The allowed keys.</summary>
    public static readonly IReadOnlyList<string> Keys =
    [
        "app.identity.name",
        "auth.application.default-domain",
        "auth.keycloak.public-base-url",
        "auth.keycloak.internal-base-url",
        "auth.keycloak.shared-realm-name",
        "auth.keycloak.admin-client-id",
        "auth.keycloak.admin-client-secret"
    ];

    /// <summary>
    /// Returns true when <paramref name="key"/> is on the allowlist (case-sensitive, like setting keys).
    /// </summary>
    /// <param name="key">The setting key.</param>
    public static bool Contains(string key) => Keys.Contains(key, StringComparer.Ordinal);
}
