namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request to create a new identity provider client within a realm.
/// </summary>
/// <param name="ClientId">The client identifier to register.</param>
/// <param name="RedirectUris">Allowed redirect URIs for the client.</param>
/// <param name="RequiresPkce">Whether PKCE should be enforced.</param>
public record CreateIdentityProviderClientRequest(
    string ClientId,
    IReadOnlyList<string> RedirectUris,
    bool RequiresPkce);
