namespace GroundUp.Core.Configuration;

/// <summary>
/// Configuration options bound from the "GroundUp" section.
/// Contains master key resolution settings, database connection, and bootstrap admin token.
/// </summary>
public sealed class BootstrapOptions
{
    /// <summary>
    /// The configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "GroundUp";

    /// <summary>
    /// The database connection string for the GroundUp framework database.
    /// </summary>
    public string? DatabaseConnection { get; set; }

    /// <summary>
    /// The master key as a base64-encoded string (environment variable source).
    /// Lower priority than <see cref="MasterKeyPath"/>.
    /// </summary>
    public string? MasterKey { get; set; }

    /// <summary>
    /// File path to the master key file containing a base64-encoded key.
    /// Higher priority than <see cref="MasterKey"/>.
    /// </summary>
    public string? MasterKeyPath { get; set; }

    /// <summary>
    /// The one-time bearer token used to authenticate setup wizard requests.
    /// Must be at least 32 characters. Rejected automatically once setup completes.
    /// </summary>
    public string? BootstrapAdminToken { get; set; }

    /// <summary>
    /// Keycloak bootstrap credentials for Path B auto-provisioning.
    /// </summary>
    public KeycloakBootstrapOptions Keycloak { get; set; } = new();
}

/// <summary>
/// Keycloak master admin credentials used during the bootstrap wizard step.
/// These credentials are accepted once, used to create a service-account client,
/// and then immediately discarded. They are never persisted to the database.
/// </summary>
public sealed class KeycloakBootstrapOptions
{
    /// <summary>
    /// The Keycloak master admin username for initial client provisioning.
    /// </summary>
    public string? BootstrapAdminUsername { get; set; }

    /// <summary>
    /// The Keycloak master admin password for initial client provisioning.
    /// </summary>
    public string? BootstrapAdminPassword { get; set; }
}
