using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Keycloak;

/// <summary>
/// Builds deep-link URLs to the Keycloak admin console for realm, client, user,
/// and identity provider management pages.
/// </summary>
/// <remarks>
/// All URLs follow the pattern: <c>{PublicBaseUrl}/admin/{realmName}/console/#/{subPath}</c>.
/// When the <c>realmName</c> parameter is null, defaults to
/// <see cref="KeycloakOptions.SharedRealmName"/>.
/// Registered as a singleton via DI.
/// </remarks>
public sealed class KeycloakAdminLinkBuilder
{
    private readonly IOptionsMonitor<KeycloakOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeycloakAdminLinkBuilder"/> class.
    /// </summary>
    /// <param name="options">The options monitor providing current Keycloak configuration.</param>
    public KeycloakAdminLinkBuilder(IOptionsMonitor<KeycloakOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Builds the URL to the realm overview (console root) for the specified realm.
    /// </summary>
    /// <param name="realmName">The realm name, or null to use the shared realm.</param>
    /// <returns>The admin console URL for the realm overview.</returns>
    public string RealmOverview(string? realmName = null)
    {
        return BuildUrl(realmName, string.Empty);
    }

    /// <summary>
    /// Builds the URL to the client list page for the specified realm.
    /// </summary>
    /// <param name="realmName">The realm name, or null to use the shared realm.</param>
    /// <returns>The admin console URL for the client list.</returns>
    public string ClientList(string? realmName = null)
    {
        return BuildUrl(realmName, "clients");
    }

    /// <summary>
    /// Builds the URL to the client detail page for a specific client.
    /// </summary>
    /// <param name="clientId">The client identifier to link to.</param>
    /// <param name="realmName">The realm name, or null to use the shared realm.</param>
    /// <returns>The admin console URL for the client detail page.</returns>
    public string ClientDetail(string clientId, string? realmName = null)
    {
        return BuildUrl(realmName, $"clients/{clientId}");
    }

    /// <summary>
    /// Builds the URL to the user list page for the specified realm.
    /// </summary>
    /// <param name="realmName">The realm name, or null to use the shared realm.</param>
    /// <returns>The admin console URL for the user list.</returns>
    public string UserList(string? realmName = null)
    {
        return BuildUrl(realmName, "users");
    }

    /// <summary>
    /// Builds the URL to the user detail page for a specific user.
    /// </summary>
    /// <param name="userId">The user identifier to link to.</param>
    /// <param name="realmName">The realm name, or null to use the shared realm.</param>
    /// <returns>The admin console URL for the user detail page.</returns>
    public string UserDetail(string userId, string? realmName = null)
    {
        return BuildUrl(realmName, $"users/{userId}");
    }

    /// <summary>
    /// Builds the URL to the identity providers configuration page for the specified realm.
    /// </summary>
    /// <param name="realmName">The realm name, or null to use the shared realm.</param>
    /// <returns>The admin console URL for the identity provider configuration page.</returns>
    public string IdentityProviderConfig(string? realmName = null)
    {
        return BuildUrl(realmName, "identity-providers");
    }

    /// <summary>
    /// Constructs the full admin console URL with the specified realm and sub-path.
    /// </summary>
    private string BuildUrl(string? realmName, string subPath)
    {
        var opts = _options.CurrentValue;
        var effectiveRealm = realmName ?? opts.SharedRealmName;
        var baseUrl = opts.PublicBaseUrl.TrimEnd('/');

        if (string.IsNullOrEmpty(subPath))
        {
            return $"{baseUrl}/admin/{effectiveRealm}/console/#/";
        }

        return $"{baseUrl}/admin/{effectiveRealm}/console/#/{subPath}";
    }
}
