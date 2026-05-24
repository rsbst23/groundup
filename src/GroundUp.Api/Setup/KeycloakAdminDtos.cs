namespace GroundUp.Api.Setup;

/// <summary>Token response from Keycloak's token endpoint.</summary>
public sealed record KeycloakTokenResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("access_token")]
    string AccessToken);

/// <summary>Keycloak client representation (subset of fields we need).</summary>
public sealed record KeycloakClientRepresentation(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")]
    string Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("clientId")]
    string ClientId,
    [property: System.Text.Json.Serialization.JsonPropertyName("secret")]
    string? Secret);

/// <summary>Keycloak role representation.</summary>
public sealed record KeycloakRoleRepresentation(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")]
    string Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("name")]
    string Name);

/// <summary>Keycloak user representation (for service account lookup).</summary>
public sealed record KeycloakUserRepresentation(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")]
    string Id);

/// <summary>Client secret response.</summary>
public sealed record KeycloakClientSecretResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("value")]
    string Value);
