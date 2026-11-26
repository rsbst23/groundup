namespace GroundUp.Auth.Core;

/// <summary>
/// Setting keys and limits for invitations (GU-68).
/// </summary>
public static class InvitationSettingKeys
{
    /// <summary>How long a new or resent invitation stays valid, in hours (System and tenant levels).</summary>
    public const string LifetimeHours = "auth.invitations.lifetime-hours";

    /// <summary>
    /// The client app's accept page, with a <c>{token}</c> placeholder and an optional
    /// <c>{tenantSlug}</c> placeholder (System level). Invitations cannot be sent while it is empty.
    /// </summary>
    public const string AcceptUrl = "auth.invitations.accept-url";

    /// <summary>
    /// When true, accepting an invitation also requires the identity provider to have verified the
    /// email (System level). By default the invitation link counts as proof of the email.
    /// </summary>
    public const string RequireVerifiedEmail = "auth.invitations.require-verified-email";

    /// <summary>Default lifetime: 7 days.</summary>
    public const int LifetimeHoursDefault = 168;

    /// <summary>Shortest lifetime.</summary>
    public const int LifetimeHoursMin = 1;

    /// <summary>Longest lifetime: 30 days.</summary>
    public const int LifetimeHoursMax = 720;

    /// <summary>The token placeholder in <see cref="AcceptUrl"/>.</summary>
    public const string TokenPlaceholder = "{token}";

    /// <summary>The tenant slug placeholder in <see cref="AcceptUrl"/>.</summary>
    public const string TenantSlugPlaceholder = "{tenantSlug}";
}
