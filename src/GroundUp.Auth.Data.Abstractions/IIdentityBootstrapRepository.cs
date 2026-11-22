using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Data.Abstractions;

/// <summary>
/// System-level data access for first-run setup (the first SuperAdmin). Every method deliberately
/// bypasses tenant filtering: the System tenant and the SuperAdmin role are looked up across tenants,
/// before any tenant context exists. Kept in one repository so the bypass lives in one documented place.
/// </summary>
public interface IIdentityBootstrapRepository
{
    /// <summary>The id of the role with this name, or null. Searches every tenant.</summary>
    Task<Guid?> FindRoleIdByNameAsync(string roleName, CancellationToken cancellationToken = default);

    /// <summary>True when a role with this name exists in any tenant.</summary>
    Task<bool> RoleExistsAsync(string roleName, CancellationToken cancellationToken = default);

    /// <summary>The user with this email, or null.</summary>
    Task<UserDto?> FindUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>True when the tenant exists (any tenant, no filter).</summary>
    Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>True when any user holds a role with this name.</summary>
    Task<bool> AnyUserHasRoleAsync(string roleName, CancellationToken cancellationToken = default);

    /// <summary>The lowest user id holding a role with this name, or null.</summary>
    Task<Guid?> GetFirstUserIdWithRoleAsync(string roleName, CancellationToken cancellationToken = default);

    /// <summary>
    /// In one transaction, creates whichever of these does not exist yet (checked before each insert):
    /// the user (by email), the active membership in <see cref="SuperAdminRecord.TenantId"/>, and the
    /// assignment of <see cref="SuperAdminRecord.RoleId"/>. Safe to retry after a partial failure.
    /// </summary>
    /// <returns>The user id; 409 <c>CONFLICT</c> when a concurrent insert hit a unique constraint.</returns>
    Task<OperationResult<Guid>> EnsureSuperAdminAsync(SuperAdminRecord record, CancellationToken cancellationToken = default);
}
