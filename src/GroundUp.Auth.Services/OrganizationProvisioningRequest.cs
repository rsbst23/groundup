namespace GroundUp.Auth.Services;

/// <summary>
/// Input for <see cref="IOrganizationProvisioningService.ProvisionAsync"/>.
/// The founding user must already exist; the service never creates users or calls the identity provider.
/// </summary>
/// <param name="UserId">The GroundUp identifier of the founding user.</param>
/// <param name="ExternalUserId">
/// The founding user's identity-provider subject for the shared realm. Stored on the
/// membership row.
/// </param>
/// <param name="OrganizationName">The display name of the new organization. The slug is derived from it.</param>
public sealed record OrganizationProvisioningRequest(
    Guid UserId,
    string ExternalUserId,
    string OrganizationName);
