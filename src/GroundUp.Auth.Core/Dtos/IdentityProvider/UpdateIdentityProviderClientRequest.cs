namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request to update an existing identity provider client.
/// </summary>
/// <param name="RedirectUris">New redirect URIs, or null to leave unchanged.</param>
/// <param name="RequiresPkce">New PKCE requirement, or null to leave unchanged.</param>
public record UpdateIdentityProviderClientRequest(
    IReadOnlyList<string>? RedirectUris,
    bool? RequiresPkce);
