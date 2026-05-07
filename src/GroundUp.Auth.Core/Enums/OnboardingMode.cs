namespace GroundUp.Auth.Core.Enums;

/// <summary>
/// Defines how users join a tenant.
/// </summary>
public enum OnboardingMode
{
    /// <summary>Users can only join via explicit invitation.</summary>
    InviteOnly = 0,

    /// <summary>Users can join via a shareable link.</summary>
    JoinLink = 1,

    /// <summary>Any authenticated user can join freely.</summary>
    Open = 2
}
