using System.Text.Json.Serialization;

namespace GroundUp.Auth.Keycloak.Models;

/// <summary>
/// Represents the Keycloak credential representation used for PUT operations
/// (e.g., setting/resetting user passwords).
/// </summary>
internal sealed record KeycloakCredentialRepresentation(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("temporary")] bool Temporary);
