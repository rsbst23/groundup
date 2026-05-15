namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Signing key material with metadata for JWT token generation and validation.
/// </summary>
/// <param name="KeyId">The unique identifier for this signing key (used as the JWT 'kid' header).</param>
/// <param name="KeyMaterial">The raw key bytes used for signing and validation.</param>
/// <param name="Algorithm">The signing algorithm identifier (default: HS256).</param>
public record SigningKeyInfo(string KeyId, byte[] KeyMaterial, string Algorithm = "HS256");
