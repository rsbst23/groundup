using System.Text.Json.Serialization;

namespace GroundUp.Auth.Keycloak.Models;

/// <summary>
/// Represents a subset of the Keycloak client representation used for GET/POST operations.
/// </summary>
internal sealed record KeycloakClientRepresentation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("secret")] string? Secret,
    [property: JsonPropertyName("redirectUris")] List<string>? RedirectUris,
    [property: JsonPropertyName("publicClient")] bool PublicClient,
    [property: JsonPropertyName("attributes")] Dictionary<string, string>? Attributes);
