namespace GroundUp.Auth.Core.Abstractions;

using GroundUp.Auth.Core.Dtos;
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

    /// <summary>
    /// Returns true if the system tenant exists in the auth database.
    /// Used by the setup wizard to validate preconditions before first-admin creation.
    /// </summary>
    Task<bool> HasSystemTenantAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if the SuperAdmin role exists in the auth database.
    /// Used by the setup wizard to validate preconditions before first-admin creation.
    /// </summary>
    Task<bool> HasSuperAdminRoleAsync(CancellationToken cancellationToken = default);
}
