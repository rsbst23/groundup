namespace GroundUp.Auth.Core.Enums;

/// <summary>
/// Distinguishes standard tenants from enterprise tenants that support SSO federation.
/// </summary>
public enum TenantType
{
    /// <summary>Standard tenant with local authentication.</summary>
    Standard = 0,

    /// <summary>Enterprise tenant with SSO federation support.</summary>
    Enterprise = 1
}
