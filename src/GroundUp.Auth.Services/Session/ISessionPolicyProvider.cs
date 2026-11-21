namespace GroundUp.Auth.Services.Session;

/// <summary>
/// Resolves the effective session timeouts from the Settings module. A tenant value can only
/// shorten the System value. Does not depend on the request's tenant context, so it can be
/// used by middleware that runs before tenant resolution.
/// </summary>
public interface ISessionPolicyProvider
{
    /// <summary>
    /// Gets the System-level policy. Used for tokens that have no tenant yet and for logins
    /// where the tenant is not known.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SessionPolicy> GetSystemPolicyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the policy for a tenant: the tenant's value where set, never longer than the
    /// System value.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SessionPolicy> GetPolicyForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
