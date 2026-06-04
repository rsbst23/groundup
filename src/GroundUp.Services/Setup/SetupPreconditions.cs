namespace GroundUp.Services.Setup;

using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;

/// <summary>
/// Shared precondition checker used by all wizard steps to enforce ordering.
/// Returns null if the precondition is satisfied, or an error message naming
/// the missing prerequisite step if not.
/// </summary>
internal static class SetupPreconditions
{
    /// <summary>
    /// Checks that the app-identity step has been completed.
    /// Returns null if satisfied, or an error message if not.
    /// </summary>
    public static async Task<string?> CheckAppIdentityAsync(
        ISettingsService settings, CancellationToken ct)
    {
        var nameResult = await settings.GetAsync<string>("app.identity.name", ct);
        if (!nameResult.Success || string.IsNullOrEmpty(nameResult.Data))
            return "App identity step must be completed first.";

        // auth.application.default-domain may be empty string (host-only cookie) — just check it exists
        var domainResult = await settings.GetAsync<string>("auth.application.default-domain", ct);
        if (!domainResult.Success)
            return "App identity step must be completed first.";

        return null;
    }

    /// <summary>
    /// Checks that the identity-provider step has been completed.
    /// Returns null if satisfied, or an error message if not.
    /// </summary>
    public static async Task<string?> CheckIdentityProviderAsync(
        ISettingsService settings, CancellationToken ct)
    {
        var urlResult = await settings.GetAsync<string>("auth.keycloak.public-base-url", ct);
        if (!urlResult.Success || string.IsNullOrEmpty(urlResult.Data))
            return "Identity provider configuration must be completed first.";

        var realmResult = await settings.GetAsync<string>("auth.keycloak.shared-realm-name", ct);
        if (!realmResult.Success || string.IsNullOrEmpty(realmResult.Data))
            return "Identity provider configuration must be completed first.";

        return null;
    }

    /// <summary>
    /// Checks that the keycloak-bootstrap step has been completed.
    /// Returns null if satisfied, or an error message if not.
    /// </summary>
    public static async Task<string?> CheckKeycloakBootstrapAsync(
        ISettingsService settings, CancellationToken ct)
    {
        var clientIdResult = await settings.GetAsync<string>("auth.keycloak.admin-client-id", ct);
        if (!clientIdResult.Success || string.IsNullOrEmpty(clientIdResult.Data))
            return "Keycloak admin client must be provisioned first.";

        var secretResult = await settings.GetAsync<string>("auth.keycloak.admin-client-secret", ct);
        if (!secretResult.Success || string.IsNullOrEmpty(secretResult.Data))
            return "Keycloak admin client must be provisioned first.";

        return null;
    }
}
