using System.Text.Json.Serialization;

namespace GroundUp.Auth.Keycloak.Models;

/// <summary>
/// Represents a subset of the Keycloak user representation used for GET/POST operations.
/// </summary>
internal sealed record KeycloakUserRepresentation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("firstName")] string? FirstName,
    [property: JsonPropertyName("lastName")] string? LastName,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("requiredActions")] List<string>? RequiredActions);
