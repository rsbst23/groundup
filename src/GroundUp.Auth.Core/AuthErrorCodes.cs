namespace GroundUp.Auth.Core;

/// <summary>
/// Error codes returned by the authentication module's membership, role-assignment, invitation and
/// join-link operations (GU-68). General codes (NOT_FOUND, FORBIDDEN, CONFLICT and so on) are in
/// <see cref="GroundUp.Core.ErrorCodes"/>.
/// </summary>
public static class AuthErrorCodes
{
    /// <summary>The tenant's last active TenantAdmin cannot lose that role or leave.</summary>
    public const string LastAdmin = "LAST_ADMIN";

    /// <summary>The last active SuperAdmin cannot lose that role.</summary>
    public const string LastSuperAdmin = "LAST_SUPERADMIN";

    /// <summary>The invited email already belongs to an active member of the tenant.</summary>
    public const string AlreadyMember = "ALREADY_MEMBER";

    /// <summary>The email already has a pending invitation to the tenant.</summary>
    public const string InvitationPending = "INVITATION_PENDING";

    /// <summary>The signed-in email does not match the invited email.</summary>
    public const string InvitationEmailMismatch = "INVITATION_EMAIL_MISMATCH";

    /// <summary>The identity provider has not verified the signed-in email, and verification is required.</summary>
    public const string EmailNotVerified = "EMAIL_NOT_VERIFIED";

    /// <summary>The invitation was revoked, expired, or its creator may no longer grant its role.</summary>
    public const string InvitationNoLongerValid = "INVITATION_NO_LONGER_VALID";

    /// <summary>The invitation was already accepted.</summary>
    public const string InvitationAlreadyUsed = "INVITATION_ALREADY_USED";

    /// <summary>The invitation belongs to a different organization than the host names.</summary>
    public const string InvitationWrongOrganization = "INVITATION_WRONG_ORGANIZATION";

    /// <summary>The <c>auth.invitations.accept-url</c> setting is empty.</summary>
    public const string InvitationUrlNotConfigured = "INVITATION_URL_NOT_CONFIGURED";

    /// <summary>The join link was revoked, expired, used up, or its creator may no longer grant its role.</summary>
    public const string JoinLinkNoLongerValid = "JOIN_LINK_NO_LONGER_VALID";

    /// <summary>The return URL given when starting a sign-in is not a path on this site (GU-87).</summary>
    public const string InvalidReturnUrl = "INVALID_RETURN_URL";

    /// <summary>The identity provider reported an error to the sign-in callback (for example access_denied).</summary>
    public const string IdpError = "IDP_ERROR";

    /// <summary>Invitations and join links are not available for enterprise tenants yet (GU-69).</summary>
    public const string EnterpriseNotSupported = "ENTERPRISE_NOT_SUPPORTED";

    /// <summary>Another role of the tenant has the same name, ignoring case (GU-95).</summary>
    public const string RoleNameTaken = "ROLE_NAME_TAKEN";

    /// <summary>
    /// The role cannot be deleted because members hold it, invitations or join links grant it, or it
    /// is the tenant's default role. The response's <c>errors</c> list the reasons (GU-95).
    /// </summary>
    public const string RoleInUse = "ROLE_IN_USE";

    /// <summary>
    /// The role is built in (TenantAdmin): it cannot be renamed, deleted or made the default role (GU-95).
    /// </summary>
    public const string RoleIsBuiltIn = "ROLE_IS_BUILT_IN";
}
