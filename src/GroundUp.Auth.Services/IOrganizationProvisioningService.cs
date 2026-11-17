using GroundUp.Core.Results;

namespace GroundUp.Auth.Services;

/// <summary>
/// Provisions a new standard organization for an existing user. Shared by the sign-up callback
/// (<see cref="NewOrganizationFlowHandler"/>) and the authenticated organization-creation endpoint
/// so both produce identical results.
/// <para>
/// This service contains pure GroundUp domain logic. It never calls the identity provider and
/// never creates users — callers resolve the user first.
/// </para>
/// </summary>
public interface IOrganizationProvisioningService
{
    /// <summary>
    /// Creates, in a single transaction: a standard tenant, a membership for the founding user,
    /// a tenant-scoped <c>TenantAdmin</c> system role, and the assignment of that role to the user.
    /// Retries with a disambiguated slug when the derived slug is already taken.
    /// </summary>
    /// <param name="request">The founding user and organization name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A success result with the new tenant's id, name and slug; or a failure result with
    /// error code <c>SLUG_COLLISION</c> (HTTP 409) when no unique slug could be found.
    /// Unexpected infrastructure failures roll the transaction back and propagate as exceptions.
    /// </returns>
    /// <remarks>
    /// The ambient <c>TenantContext</c> is pointed at the new tenant while the tenant-scoped rows are
    /// written and is restored to its previous value before this method returns. Callers that need the
    /// context to reference the new tenant afterwards (for example to issue a token) must set it explicitly.
    /// </remarks>
    Task<OperationResult<OrganizationProvisioningResult>> ProvisionAsync(
        OrganizationProvisioningRequest request,
        CancellationToken cancellationToken = default);
}
