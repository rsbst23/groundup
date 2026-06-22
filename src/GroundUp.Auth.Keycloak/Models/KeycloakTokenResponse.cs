using System.Text.Json.Serialization;

namespace GroundUp.Auth.Keycloak.Models;

/// <summary>
/// Represents the JSON response from Keycloak's token endpoint.
/// </summary>
internal sealed record KeycloakTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("token_type")] string TokenType);
