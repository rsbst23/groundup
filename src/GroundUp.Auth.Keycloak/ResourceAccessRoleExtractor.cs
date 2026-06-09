using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace GroundUp.Auth.Keycloak;

/// <summary>
/// Static utility for extracting client-specific roles from the Keycloak
/// <c>resource_access</c> claim found in access tokens and ID tokens.
/// </summary>
/// <remarks>
/// The <c>resource_access</c> claim has the structure:
/// <code>
/// {
///   "client-id": { "roles": ["role1", "role2"] },
///   ...
/// }
/// </code>
/// This class navigates that structure and returns role strings for a given client ID.
/// Returns an empty list on any failure — never throws, never returns null.
/// Does NOT log internally (static utility).
/// </remarks>
public static class ResourceAccessRoleExtractor
{
    private static readonly IReadOnlyList<string> EmptyRoles = Array.Empty<string>();

    /// <summary>
    /// Extracts roles for the specified <paramref name="clientId"/> from the
    /// <c>resource_access</c> claim in a JWT token string.
    /// </summary>
    /// <param name="token">A JWT token string (three dot-separated base64url segments).</param>
    /// <param name="clientId">The client identifier to look up in <c>resource_access</c>.</param>
    /// <returns>
    /// The list of role strings for the specified client, or an empty list if the token
    /// is malformed, the claim is missing, or the structure is unexpected.
    /// </returns>
    public static IReadOnlyList<string> ExtractRoles(string token, string clientId)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(clientId))
        {
            return EmptyRoles;
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return EmptyRoles;
        }

        try
        {
            var payloadJson = DecodeBase64Url(parts[1]);
            if (payloadJson is null)
            {
                return EmptyRoles;
            }

            return ParseRolesFromJson(payloadJson, clientId);
        }
        catch
        {
            return EmptyRoles;
        }
    }

    /// <summary>
    /// Extracts roles for the specified <paramref name="clientId"/> from a
    /// <c>resource_access</c> claim within a claims collection.
    /// </summary>
    /// <param name="claims">The claims collection (e.g., from a <c>ClaimsPrincipal</c>).</param>
    /// <param name="clientId">The client identifier to look up in <c>resource_access</c>.</param>
    /// <returns>
    /// The list of role strings for the specified client, or an empty list if the claim
    /// is missing, malformed, or does not contain the specified client ID.
    /// </returns>
    public static IReadOnlyList<string> ExtractRolesFromClaims(IEnumerable<Claim> claims, string clientId)
    {
        if (claims is null || string.IsNullOrWhiteSpace(clientId))
        {
            return EmptyRoles;
        }

        try
        {
            var resourceAccessClaim = FindResourceAccessClaim(claims);
            if (resourceAccessClaim is null)
            {
                return EmptyRoles;
            }

            return ParseRolesFromResourceAccessJson(resourceAccessClaim, clientId);
        }
        catch
        {
            return EmptyRoles;
        }
    }

    /// <summary>
    /// Decodes a base64url-encoded string to a UTF-8 string.
    /// </summary>
    private static string? DecodeBase64Url(string base64Url)
    {
        try
        {
            // Add padding if necessary
            var padded = (base64Url.Length % 4) switch
            {
                2 => base64Url + "==",
                3 => base64Url + "=",
                _ => base64Url
            };

            var base64 = padded.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a full JWT payload JSON and extracts roles from the <c>resource_access</c> property.
    /// </summary>
    private static IReadOnlyList<string> ParseRolesFromJson(string json, string clientId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Navigate to resource_access property
        if (!root.TryGetProperty("resource_access", out var resourceAccess)
            || resourceAccess.ValueKind != JsonValueKind.Object)
        {
            return EmptyRoles;
        }

        return ExtractRolesFromResourceAccessElement(resourceAccess, clientId);
    }

    /// <summary>
    /// Parses a <c>resource_access</c> claim value (JSON object) and extracts roles for the given client ID.
    /// </summary>
    private static IReadOnlyList<string> ParseRolesFromResourceAccessJson(string resourceAccessJson, string clientId)
    {
        using var doc = JsonDocument.Parse(resourceAccessJson);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return EmptyRoles;
        }

        return ExtractRolesFromResourceAccessElement(root, clientId);
    }

    /// <summary>
    /// Extracts roles from a pre-parsed <c>resource_access</c> JSON element.
    /// </summary>
    private static IReadOnlyList<string> ExtractRolesFromResourceAccessElement(
        JsonElement resourceAccess, string clientId)
    {
        // Navigate to the client entry
        if (!resourceAccess.TryGetProperty(clientId, out var clientEntry)
            || clientEntry.ValueKind != JsonValueKind.Object)
        {
            return EmptyRoles;
        }

        // Navigate to the roles array
        if (!clientEntry.TryGetProperty("roles", out var rolesElement)
            || rolesElement.ValueKind != JsonValueKind.Array)
        {
            return EmptyRoles;
        }

        var roles = new List<string>();
        foreach (var roleElement in rolesElement.EnumerateArray())
        {
            if (roleElement.ValueKind == JsonValueKind.String)
            {
                var role = roleElement.GetString();
                if (role is not null)
                {
                    roles.Add(role);
                }
            }
        }

        return roles.AsReadOnly();
    }

    /// <summary>
    /// Finds the <c>resource_access</c> claim in a claims collection.
    /// </summary>
    private static string? FindResourceAccessClaim(IEnumerable<Claim> claims)
    {
        foreach (var claim in claims)
        {
            if (string.Equals(claim.Type, "resource_access", StringComparison.OrdinalIgnoreCase))
            {
                return claim.Value;
            }
        }

        return null;
    }
}
