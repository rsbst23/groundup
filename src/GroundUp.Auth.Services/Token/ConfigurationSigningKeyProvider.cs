using System.Text;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.Configuration;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services.Token;

/// <summary>
/// Default signing key provider that uses <see cref="AuthOptions.JwtSigningKey"/> for all tenants.
/// Single-key mode — suitable for applications that don't need per-tenant key isolation.
/// Returns a "default" key ID for all tenants and resolves only that key ID on validation.
/// </summary>
public sealed class ConfigurationSigningKeyProvider : ISigningKeyProvider
{
    /// <summary>
    /// Minimum key length in bytes for HMAC-SHA256 (256 bits) per RFC 4868.
    /// </summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// The well-known key identifier emitted by this single-key provider.
    /// </summary>
    public const string DefaultKeyId = "default";

    private readonly SigningKeyInfo _defaultKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationSigningKeyProvider"/> class.
    /// </summary>
    /// <param name="options">The auth options containing the JWT signing key.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="AuthOptions.JwtSigningKey"/> is missing or shorter than
    /// 32 bytes (256 bits) when UTF-8 encoded — the minimum required for HMAC-SHA256.
    /// </exception>
    public ConfigurationSigningKeyProvider(IOptions<AuthOptions> options)
    {
        var signingKey = options.Value.JwtSigningKey;
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                "AuthOptions.JwtSigningKey is not configured. Set it via the 'GroundUp:Auth:JwtSigningKey' " +
                "configuration section or the AddGroundUpAuth(configure) overload.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(signingKey);
        if (keyBytes.Length < MinimumKeyBytes)
        {
            throw new InvalidOperationException(
                $"AuthOptions.JwtSigningKey must be at least {MinimumKeyBytes} bytes ({MinimumKeyBytes * 8} bits) " +
                $"when UTF-8 encoded for HMAC-SHA256. Current length: {keyBytes.Length} bytes.");
        }

        _defaultKey = new SigningKeyInfo(DefaultKeyId, keyBytes, "HS256");
    }

    /// <inheritdoc />
    public Task<SigningKeyInfo> GetSigningKeyAsync(Guid tenantId) =>
        Task.FromResult(_defaultKey);

    /// <inheritdoc />
    public Task<SigningKeyInfo?> GetValidationKeyAsync(string kid) =>
        Task.FromResult<SigningKeyInfo?>(kid == DefaultKeyId ? _defaultKey : null);
}
