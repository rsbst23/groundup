namespace GroundUp.Core.Enums;

/// <summary>
/// The outcome of a settings authorization check.
/// </summary>
public enum SettingsAuthorizationResult
{
    /// <summary>The caller may perform the operation.</summary>
    Allowed = 0,

    /// <summary>The caller is known but not permitted to perform the operation.</summary>
    Forbidden = 1,

    /// <summary>There is no authenticated caller.</summary>
    Unauthenticated = 2
}
