namespace GroundUp.Auth.Services.Bootstrap;

using GroundUp.Core.Results;

/// <summary>
/// Service contract used by the setup wizard to provision the first SuperAdmin
/// user in the auth-module database. Performs User + UserTenant + SuperAdmin
/// role assignment in a single transaction with explicit existence checks for
/// idempotent retry support.
/// </summary>
public interface IIdentityBootstrapService
{
    /// <summary>
    /// Idempotently provisions the first SuperAdmin user in the GroundUp database.
    /// Uses explicit existence checks before each insert so retries on a partially-
    /// completed state do not throw primary-key or unique-constraint violations.
    /// </summary>
    Task<OperationResult<BootstrapAdminResultDto>> ProvisionFirstSuperAdminAsync(
        ProvisionFirstSuperAdminRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if at least one user with the SuperAdmin role exists in the database.
    /// Used by the setup wizard's status endpoint and Complete-step preconditions.
    /// </summary>
    Task<bool> HasSuperAdminAsync(CancellationToken cancellationToken = default);
}
