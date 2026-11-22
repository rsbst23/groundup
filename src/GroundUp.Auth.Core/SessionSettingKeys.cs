namespace GroundUp.Auth.Core;

/// <summary>
/// Keys, bounds and defaults of the session timeout settings. Shared by the seeder
/// (which defines them), the session policy provider (which reads them) and the write
/// validator (which checks them).
/// </summary>
public static class SessionSettingKeys
{
    /// <summary>How long a token is valid, in minutes. Also the inactivity timeout.</summary>
    public const string TokenLifetimeMinutes = "auth.session.token-lifetime-minutes";

    /// <summary>The longest a session can last from the user's real authentication, in minutes.</summary>
    public const string AbsoluteLifetimeMinutes = "auth.session.absolute-lifetime-minutes";

    /// <summary>Minimum token lifetime in minutes.</summary>
    public const int TokenLifetimeMin = 2;

    /// <summary>Maximum token lifetime in minutes.</summary>
    public const int TokenLifetimeMax = 120;

    /// <summary>
    /// Default token lifetime in minutes. Short because, until the caching module provides a shared
    /// revocation store, a revoked token is honored by other instances until it expires (GU-71).
    /// </summary>
    public const int TokenLifetimeDefault = 15;

    /// <summary>Minimum absolute session lifetime in minutes.</summary>
    public const int AbsoluteLifetimeMin = 10;

    /// <summary>Maximum absolute session lifetime in minutes (24 hours).</summary>
    public const int AbsoluteLifetimeMax = 1440;

    /// <summary>Default absolute session lifetime in minutes (8 hours).</summary>
    public const int AbsoluteLifetimeDefault = 480;
}
