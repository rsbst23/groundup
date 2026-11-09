namespace GroundUp.Auth.Services;

/// <summary>
/// Output of a successful <see cref="IOrganizationProvisioningService.ProvisionAsync"/> call.
/// </summary>
/// <param name="TenantId">The identifier of the newly created tenant.</param>
/// <param name="Name">The organization's display name.</param>
/// <param name="Slug">The unique URL-friendly slug assigned to the organization (possibly disambiguated).</param>
public sealed record OrganizationProvisioningResult(
    Guid TenantId,
    string Name,
    string Slug);
