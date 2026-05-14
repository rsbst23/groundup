using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services;

/// <summary>
/// Manages authentication session operations including tenant selection and token refresh.
/// Orchestrates between user-tenant membership queries and token generation to provide
/// a complete session lifecycle for authenticated users.
/// </summary>
public interface IAuthSessionService
{
    /// <summary>
    /// Selects a tenant for the authenticated user's session.
    /// If <paramref name="tenantId"/> is null and the user belongs to exactly one tenant,
    /// auto-selects that tenant and returns a token. If the user belongs to multiple tenants,
    /// returns the list of available tenants for explicit selection.
    /// If <paramref name="tenantId"/> is specified, validates membership and issues a scoped token.
    /// </summary>
    /// <param name="userId">The authenticated user's identifier.</param>
    /// <param name="tenantId">The tenant to select, or null to trigger auto-select or list.</param>
    /// <returns>
    /// A successful result containing the tenant selection response,
    /// or a forbidden result if the user does not belong to the specified tenant.
    /// </returns>
    Task<OperationResult<SetTenantResponseDto>> SetTenantAsync(Guid userId, Guid? tenantId);

    /// <summary>
    /// Refreshes the token for the specified user and tenant with fresh roles.
    /// Re-validates tenant membership before issuing a new token.
    /// </summary>
    /// <param name="userId">The authenticated user's identifier.</param>
    /// <param name="tenantId">The tenant to refresh the token for.</param>
    /// <returns>
    /// A successful result containing the new token string,
    /// or a forbidden result if the user no longer belongs to the tenant.
    /// </returns>
    Task<OperationResult<string>> RefreshTokenAsync(Guid userId, Guid tenantId);
}
