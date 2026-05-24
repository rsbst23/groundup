namespace GroundUp.Core.Dtos.Setup;

/// <summary>
/// Request to set the application identity during the setup wizard.
/// Captures the application name and default domain.
/// </summary>
/// <param name="ApplicationName">The display name of the application.</param>
/// <param name="DefaultDomain">The default domain for the application (e.g., "example.com").</param>
public sealed record SetAppIdentityRequest(string? ApplicationName, string? DefaultDomain);

/// <summary>
/// Request to configure the identity provider URLs during the setup wizard.
/// Captures the Keycloak base URLs and shared realm name.
/// </summary>
/// <param name="PublicBaseUrl">The public-facing Keycloak base URL (used by browsers).</param>
/// <param name="InternalBaseUrl">The internal Keycloak base URL (used by backend services).</param>
/// <param name="SharedRealmName">The Keycloak realm name shared across tenants.</param>
public sealed record SetIdentityProviderRequest(
    string? PublicBaseUrl,
    string? InternalBaseUrl,
    string? SharedRealmName);

/// <summary>
/// Request to bootstrap the Keycloak admin client during the setup wizard.
/// Master admin credentials are used once to create a service-account client
/// and are never persisted to the database.
/// </summary>
/// <param name="MasterAdminUsername">The Keycloak master realm admin username.</param>
/// <param name="MasterAdminPassword">The Keycloak master realm admin password.</param>
public sealed record KeycloakBootstrapRequest(
    string? MasterAdminUsername,
    string? MasterAdminPassword);

/// <summary>
/// Request to create the first super admin user during the setup wizard.
/// The user is provisioned in both Keycloak and the local database.
/// </summary>
/// <param name="Email">The email address for the first admin user.</param>
/// <param name="DisplayName">The display name for the first admin user.</param>
/// <param name="Password">The password for the first admin user.</param>
public sealed record CreateFirstAdminRequest(
    string? Email,
    string? DisplayName,
    string? Password);
