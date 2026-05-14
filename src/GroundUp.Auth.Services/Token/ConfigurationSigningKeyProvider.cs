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
    private readonly SigningKeyInfo _defaultKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationSigningKeyProvider"/> class.
    /// </summary>
    /// <param name="options">The auth options containing the JWT signing key.</param>
    public ConfigurationSigningKeyProvider(IOptions<AuthOptions> options)
    {
        var keyBytes = Encoding.UTF8.GetBytes(options.Value.JwtSigningKey);
        _defaultKey = new SigningKeyInfo("default", keyBytes, "HS256");
    }

    /// <inheritdoc />
    public Task<SigningKeyInfo> GetSigningKeyAsync(Guid tenantId) =>
        Task.FromResult(_defaultKey);

    /// <inheritdoc />
    public Task<SigningKeyInfo?> GetValidationKeyAsync(string kid) =>
        Task.FromResult<SigningKeyInfo?>(kid == "default" ? _defaultKey : null);
}
