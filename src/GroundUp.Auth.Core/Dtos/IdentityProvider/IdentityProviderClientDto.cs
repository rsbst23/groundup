namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of an identity provider client (application registration).
/// </summary>
/// <param name="ClientId">The client identifier.</param>
/// <param name="ClientSecret">The client secret, if confidential. Null for public clients.</param>
/// <param name="RedirectUris">Allowed redirect URIs for the client.</param>
/// <param name="RequiresPkce">Whether PKCE is required for authorization code flows.</param>
public record IdentityProviderClientDto(
    string ClientId,
    string? ClientSecret,
    IReadOnlyList<string> RedirectUris,
    bool RequiresPkce);
