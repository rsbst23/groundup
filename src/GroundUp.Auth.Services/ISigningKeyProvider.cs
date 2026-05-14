using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Services;

/// <summary>
/// Abstraction for resolving JWT signing keys. Supports per-tenant key isolation.
/// Consumers may implement this interface to integrate with external key management systems
/// (AWS KMS, Azure Key Vault, HashiCorp Vault, etc.).
/// This is internal infrastructure — never exposed via API or SDK layer.
/// </summary>
public interface ISigningKeyProvider
{
    /// <summary>
    /// Resolves the signing key for a specific tenant.
    /// Falls back to the application-wide default key if no tenant-specific key is configured.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to resolve the signing key for.</param>
    /// <returns>The signing key information including key material, key ID, and algorithm.</returns>
    Task<SigningKeyInfo> GetSigningKeyAsync(Guid tenantId);

    /// <summary>
    /// Resolves a validation key by its key ID (from the JWT <c>kid</c> header).
    /// Used during token validation to locate the correct key for signature verification.
    /// </summary>
    /// <param name="kid">The key identifier from the JWT header.</param>
    /// <returns>The signing key information if found; otherwise null.</returns>
    Task<SigningKeyInfo?> GetValidationKeyAsync(string kid);
}
