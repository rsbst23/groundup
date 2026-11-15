using System.Security.Claims;

namespace GroundUp.Auth.Services;

/// <summary>
/// Generates and validates GroundUp-issued JWT tokens containing user identity,
/// tenant, and role claims. Permissions are resolved server-side via
/// <see cref="IPermissionService"/> and are never embedded in the token.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a signed JWT for the specified user and tenant.
    /// The token includes standard claims (sub, tid, email, name, roles[])
    /// plus any additional claims provided.
    /// </summary>
    /// <param name="userId">The user identifier (becomes the <c>sub</c> claim).</param>
    /// <param name="tenantId">The tenant identifier (becomes the <c>tid</c> claim).</param>
    /// <param name="additionalClaims">Optional additional claims to include in the token.</param>
    /// <returns>The signed JWT string, or null if the user does not exist.</returns>
    Task<string?> GenerateTokenAsync(Guid userId, Guid tenantId, IEnumerable<Claim>? additionalClaims = null);

    /// <summary>
    /// Generates a signed JWT for a signed-in user who has no current tenant (a tenantless session).
    /// The token carries identity claims only (sub, email, name) plus any additional claims provided.
    /// It has no <c>tid</c> claim and no role claims, not even system roles: a tenantless session
    /// grants nothing beyond identity and the ability to choose or create a tenant.
    /// </summary>
    /// <param name="userId">The user identifier (becomes the <c>sub</c> claim).</param>
    /// <param name="additionalClaims">Optional additional claims to include in the token (for example <c>auth_time</c>).</param>
    /// <returns>The signed JWT string, or null if the user does not exist or is inactive.</returns>
    Task<string?> GenerateTenantlessTokenAsync(Guid userId, IEnumerable<Claim>? additionalClaims = null);

    /// <summary>
    /// Validates a JWT and returns the authenticated ClaimsPrincipal.
    /// Verifies signature, issuer, audience, and expiration.
    /// Never throws exceptions — all failure cases return null.
    /// </summary>
    /// <param name="token">The JWT string to validate.</param>
    /// <returns>The ClaimsPrincipal if the token is valid; otherwise null.</returns>
    Task<ClaimsPrincipal?> ValidateTokenAsync(string token);
}
