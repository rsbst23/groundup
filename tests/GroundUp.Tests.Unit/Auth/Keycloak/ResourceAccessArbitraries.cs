using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FsCheck;

namespace GroundUp.Tests.Unit.Auth.Keycloak;

/// <summary>
/// Test data types for property-based tests of <see cref="GroundUp.Auth.Keycloak.ResourceAccessRoleExtractor"/>.
/// </summary>

/// <summary>
/// Represents a valid JWT token containing a well-formed resource_access claim.
/// </summary>
public sealed record ValidResourceAccessToken(string Token, string ClientId, IReadOnlyList<string> ExpectedRoles);

/// <summary>
/// Represents a valid claims collection containing a well-formed resource_access claim.
/// </summary>
public sealed record ValidResourceAccessClaims(IEnumerable<Claim> Claims, string ClientId, IReadOnlyList<string> ExpectedRoles);

/// <summary>
/// Represents a JWT token with a missing or malformed resource_access claim.
/// </summary>
public sealed record MalformedResourceAccessToken(string Token, string ClientId);

/// <summary>
/// Represents a claims collection with a missing or malformed resource_access claim.
/// </summary>
public sealed record MalformedResourceAccessClaims(IEnumerable<Claim> Claims, string ClientId);

/// <summary>
/// Custom FsCheck Arbitrary generators for resource_access JSON structures.
/// </summary>
public static class ResourceAccessArbitraries
{
    /// <summary>
    /// Generates a valid alphanumeric identifier (non-empty, no whitespace).
    /// </summary>
    private static Gen<string> GenClientId()
    {
        return Gen.Elements(
            "my-app", "frontend-client", "backend-api", "groundup-app",
            "admin-console", "mobile-client", "web-portal", "service-worker",
            "test-client-123", "app_v2");
    }

    /// <summary>
    /// Generates a valid role name (non-empty, no whitespace).
    /// </summary>
    private static Gen<string> GenRoleName()
    {
        return Gen.Elements(
            "admin", "user", "manager", "viewer", "editor",
            "super-admin", "read-only", "write-access", "delete-access",
            "tenant-admin", "billing-manager", "report-viewer",
            "api-access", "full-control", "limited-access");
    }

    /// <summary>
    /// Generates a list of distinct role names (1 to 8 roles).
    /// </summary>
    private static Gen<IReadOnlyList<string>> GenRoleList()
    {
        return Gen.Choose(1, 8).SelectMany(count =>
            Gen.ListOf(count, GenRoleName())
                .Select(roles => (IReadOnlyList<string>)roles.Distinct().ToList().AsReadOnly()));
    }

    /// <summary>
    /// Builds a fake JWT token string (header.payload.signature) from a given payload JSON.
    /// The signature is a dummy — ResourceAccessRoleExtractor only parses the payload.
    /// </summary>
    private static string BuildFakeJwt(string payloadJson)
    {
        var header = Base64UrlEncode("{\"alg\":\"RS256\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode(payloadJson);
        var signature = Base64UrlEncode("fake-signature-data");
        return $"{header}.{payload}.{signature}";
    }

    /// <summary>
    /// Base64url-encodes a UTF-8 string.
    /// </summary>
    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Arbitrary for <see cref="ValidResourceAccessToken"/>:
    /// Generates a fake JWT token with a well-formed resource_access claim.
    /// </summary>
    public static Arbitrary<ValidResourceAccessToken> ValidResourceAccessTokenArb()
    {
        var gen = from clientId in GenClientId()
                  from roles in GenRoleList()
                  from otherClientsCount in Gen.Choose(0, 3)
                  from otherClients in Gen.ListOf(otherClientsCount, GenOtherClientEntry(clientId))
                  select BuildValidToken(clientId, roles, otherClients);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Arbitrary for <see cref="ValidResourceAccessClaims"/>:
    /// Generates a claims collection with a well-formed resource_access claim.
    /// </summary>
    public static Arbitrary<ValidResourceAccessClaims> ValidResourceAccessClaimsArb()
    {
        var gen = from clientId in GenClientId()
                  from roles in GenRoleList()
                  from otherClientsCount in Gen.Choose(0, 3)
                  from otherClients in Gen.ListOf(otherClientsCount, GenOtherClientEntry(clientId))
                  select BuildValidClaims(clientId, roles, otherClients);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Arbitrary for <see cref="MalformedResourceAccessToken"/>:
    /// Generates tokens with various malformed resource_access structures.
    /// </summary>
    public static Arbitrary<MalformedResourceAccessToken> MalformedResourceAccessTokenArb()
    {
        var gen = from clientId in GenClientId()
                  from malformedCase in Gen.Choose(0, 7)
                  select BuildMalformedToken(clientId, malformedCase);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Arbitrary for <see cref="MalformedResourceAccessClaims"/>:
    /// Generates claim collections with various malformed resource_access structures.
    /// </summary>
    public static Arbitrary<MalformedResourceAccessClaims> MalformedResourceAccessClaimsArb()
    {
        var gen = from clientId in GenClientId()
                  from malformedCase in Gen.Choose(0, 6)
                  select BuildMalformedClaims(clientId, malformedCase);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates an "other client" entry (different from the target clientId) for realistic resource_access.
    /// </summary>
    private static Gen<(string ClientId, IReadOnlyList<string> Roles)> GenOtherClientEntry(string excludeClientId)
    {
        return from otherId in GenClientId().Where(id => id != excludeClientId)
               from otherRoles in GenRoleList()
               select (otherId, otherRoles);
    }

    private static ValidResourceAccessToken BuildValidToken(
        string clientId,
        IReadOnlyList<string> roles,
        IEnumerable<(string ClientId, IReadOnlyList<string> Roles)> otherClients)
    {
        var resourceAccess = BuildResourceAccessJson(clientId, roles, otherClients);
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["sub"] = Guid.NewGuid().ToString(),
            ["iss"] = "https://keycloak.example.com/realms/test",
            ["resource_access"] = JsonSerializer.Deserialize<JsonElement>(resourceAccess)
        });

        var token = BuildFakeJwt(payloadJson);
        return new ValidResourceAccessToken(token, clientId, roles);
    }

    private static ValidResourceAccessClaims BuildValidClaims(
        string clientId,
        IReadOnlyList<string> roles,
        IEnumerable<(string ClientId, IReadOnlyList<string> Roles)> otherClients)
    {
        var resourceAccess = BuildResourceAccessJson(clientId, roles, otherClients);
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("resource_access", resourceAccess)
        };

        return new ValidResourceAccessClaims(claims, clientId, roles);
    }

    private static string BuildResourceAccessJson(
        string clientId,
        IReadOnlyList<string> roles,
        IEnumerable<(string ClientId, IReadOnlyList<string> Roles)> otherClients)
    {
        var dict = new Dictionary<string, object>
        {
            [clientId] = new { roles }
        };

        foreach (var (otherId, otherRoles) in otherClients)
        {
            dict.TryAdd(otherId, new { roles = otherRoles });
        }

        return JsonSerializer.Serialize(dict);
    }

    private static MalformedResourceAccessToken BuildMalformedToken(string clientId, int caseIndex)
    {
        var token = caseIndex switch
        {
            0 => BuildFakeJwt(JsonSerializer.Serialize(new { sub = "user1" })), // no resource_access
            1 => BuildFakeJwt(JsonSerializer.Serialize(new { resource_access = "not-an-object" })), // resource_access is string
            2 => BuildFakeJwt(JsonSerializer.Serialize(new { resource_access = new { other_client = new { roles = new[] { "admin" } } } })), // different client
            3 => BuildFakeJwt(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["resource_access"] = JsonSerializer.Deserialize<JsonElement>(
                    JsonSerializer.Serialize(new Dictionary<string, object> { [clientId] = "not-an-object" }))
            })), // client entry is string, not object
            4 => BuildFakeJwt(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["resource_access"] = JsonSerializer.Deserialize<JsonElement>(
                    JsonSerializer.Serialize(new Dictionary<string, object> { [clientId] = new { roles = "not-an-array" } }))
            })), // roles is string, not array
            5 => "not.a.jwt", // token is not valid base64url
            6 => "single-segment-no-dots", // not even 3 segments
            7 => "", // empty token
            _ => BuildFakeJwt(JsonSerializer.Serialize(new { sub = "user1" }))
        };

        return new MalformedResourceAccessToken(token, clientId);
    }

    private static MalformedResourceAccessClaims BuildMalformedClaims(string clientId, int caseIndex)
    {
        var claims = caseIndex switch
        {
            0 => new List<Claim> { new("sub", "user1") }, // no resource_access claim
            1 => new List<Claim> { new("resource_access", "not-json-at-all") }, // not valid JSON
            2 => new List<Claim> { new("resource_access", "\"just-a-string\"") }, // valid JSON but not object
            3 => new List<Claim> { new("resource_access", JsonSerializer.Serialize(new { other_client = new { roles = new[] { "admin" } } })) }, // different client
            4 => new List<Claim> { new("resource_access", JsonSerializer.Serialize(new Dictionary<string, object> { [clientId] = "not-an-object" })) }, // client entry is not object
            5 => new List<Claim> { new("resource_access", JsonSerializer.Serialize(new Dictionary<string, object> { [clientId] = new { roles = "not-an-array" } })) }, // roles is not array
            6 => new List<Claim>(), // empty claims list
            _ => new List<Claim> { new("sub", "user1") }
        };

        return new MalformedResourceAccessClaims(claims, clientId);
    }
}
