using System.Text.Json.Serialization;

namespace GroundUp.Auth.Keycloak.Models;

/// <summary>
/// Represents a subset of the Keycloak realm representation used for GET/POST operations.
/// </summary>
internal sealed record KeycloakRealmRepresentation(
    [property: JsonPropertyName("realm")] string Realm,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("enabled")] bool Enabled);
