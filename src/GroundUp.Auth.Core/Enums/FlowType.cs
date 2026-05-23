namespace GroundUp.Auth.Core.Enums;

/// <summary>
/// Identifies the type of OAuth authentication flow being initiated.
/// Each value corresponds to a distinct onboarding or authentication scenario.
/// </summary>
public enum FlowType
{
    /// <summary>User is creating a brand-new organization (tenant).</summary>
    NewOrganization = 0,

    /// <summary>User is accepting an invitation to join an existing tenant.</summary>
    Invitation = 1,

    /// <summary>User is joining a tenant via a public join link.</summary>
    JoinLink = 2,

    /// <summary>First admin bootstrapping an enterprise tenant.</summary>
    EnterpriseFirstAdmin = 3,

    /// <summary>Enterprise SSO auto-join for federated users.</summary>
    EnterpriseSsoAutoJoin = 4,

    /// <summary>User selecting which tenant to authenticate into.</summary>
    MultiTenantSelection = 5,

    /// <summary>Silent token refresh flow.</summary>
    TokenRefresh = 6
}
