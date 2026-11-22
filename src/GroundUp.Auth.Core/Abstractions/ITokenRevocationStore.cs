namespace GroundUp.Auth.Core.Abstractions;

/// <summary>
/// Records when a user's access ended and answers whether a token issued earlier is revoked.
/// A revocation is a cutoff time: tokens issued before it (by their <c>iat</c> claim) are rejected,
/// tokens issued after it are not, so a user who leaves and later rejoins can sign in again.
/// <para>
/// <b>Phase 15 (GU-47):</b> the default implementation is in-memory and per process. It must be
/// replaced by an <c>ICache</c>-backed implementation when the caching module exists, so a
/// revocation reaches every application instance.
/// </para>
/// </summary>
public interface ITokenRevocationStore
{
    /// <summary>
    /// Revokes the user's tokens for one tenant (tokens whose <c>tid</c> is <paramref name="tenantId"/>)
    /// issued before now.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="tenantId">The tenant whose access ended.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeTenantAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every token of the user, with or without a tenant, issued before now
    /// (for example when the user account is deactivated).
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeAllAccessAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when a token with these claims was issued before a revocation that applies to it.
    /// </summary>
    /// <param name="userId">The token's user (<c>sub</c>).</param>
    /// <param name="tenantId">The token's tenant (<c>tid</c>), or null for a tenantless token.</param>
    /// <param name="issuedAt">The token's issue time (<c>iat</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the token is revoked.</returns>
    Task<bool> IsRevokedAsync(
        Guid userId,
        Guid? tenantId,
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken = default);
}
