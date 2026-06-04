namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request to provision the first SuperAdmin user in the GroundUp database.
/// </summary>
/// <param name="Email">The email address for the admin user.</param>
/// <param name="DisplayName">The display name for the admin user.</param>
/// <param name="ExternalUserId">The Keycloak-assigned external user ID.</param>
/// <param name="TenantId">The system tenant ID to assign the user to.</param>
public sealed record ProvisionFirstSuperAdminRequest(
    string Email,
    string DisplayName,
    string ExternalUserId,
    Guid TenantId);
