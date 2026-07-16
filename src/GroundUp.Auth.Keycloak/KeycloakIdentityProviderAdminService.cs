using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Keycloak.Models;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core;
using GroundUp.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Keycloak;

/// <summary>
/// Keycloak implementation of <see cref="IIdentityProviderAdminService"/>.
/// Handles realm CRUD, client CRUD, and user provisioning via the Keycloak Admin REST API.
/// Uses the "KeycloakAdmin" named HttpClient via <see cref="IHttpClientFactory"/>.
/// </summary>
internal sealed class KeycloakIdentityProviderAdminService : IIdentityProviderAdminService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<KeycloakOptions> _options;
    private readonly AdminTokenCache _adminTokenCache;
    private readonly ILogger<KeycloakIdentityProviderAdminService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="KeycloakIdentityProviderAdminService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory for creating named clients.</param>
    /// <param name="options">The options monitor providing current Keycloak configuration.</param>
    /// <param name="adminTokenCache">The admin token cache for acquiring bearer tokens.</param>
    /// <param name="logger">The logger instance.</param>
    public KeycloakIdentityProviderAdminService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<KeycloakOptions> options,
        AdminTokenCache adminTokenCache,
        ILogger<KeycloakIdentityProviderAdminService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _adminTokenCache = adminTokenCache;
        _logger = logger;
    }

    // ─── Realm operations ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<OperationResult<RealmDto>> CreateRealmAsync(
        CreateRealmRequest request, CancellationToken cancellationToken = default)
    {
        var token = await _adminTokenCache.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return OperationResult<RealmDto>.Fail("Failed to acquire Keycloak admin token", 503);
        }

        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms";

        var body = new KeycloakRealmRepresentation(
            request.RealmName,
            request.DisplayName,
            Enabled: true);

        var client = CreateAuthorizedClient(token);
        var response = await client.PostAsJsonAsync(url, body, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return OperationResult<RealmDto>.Fail(
                $"Realm '{request.RealmName}' already exists", 409, ErrorCodes.Conflict);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "CreateRealmAsync failed with status {StatusCode} for realm {RealmName}",
                (int)response.StatusCode, request.RealmName);
            return OperationResult<RealmDto>.Fail(
                $"Failed to create realm '{request.RealmName}'", (int)response.StatusCode);
        }

        var realmDto = new RealmDto(
            request.RealmName,
            request.DisplayName ?? request.RealmName,
            Enabled: true);

        return OperationResult<RealmDto>.Ok(realmDto, statusCode: 201);
    }

    /// <inheritdoc />
    public async Task<OperationResult<RealmDto>> GetRealmAsync(
        string realmName, CancellationToken cancellationToken = default)
    {
        var token = await _adminTokenCache.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return OperationResult<RealmDto>.Fail("Failed to acquire Keycloak admin token", 503);
        }

        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}";

        var client = CreateAuthorizedClient(token);
        var response = await client.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return OperationResult<RealmDto>.NotFound($"Realm '{realmName}' not found");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetRealmAsync failed with status {StatusCode} for realm {RealmName}",
                (int)response.StatusCode, realmName);
            return OperationResult<RealmDto>.Fail(
                $"Failed to retrieve realm '{realmName}'", (int)response.StatusCode);
        }

        var realm = await response.Content.ReadFromJsonAsync<KeycloakRealmRepresentation>(JsonOptions, cancellationToken);

        if (realm is null)
        {
            return OperationResult<RealmDto>.Fail("Failed to deserialize realm response", 500);
        }

        return OperationResult<RealmDto>.Ok(MapToRealmDto(realm));
    }

    /// <inheritdoc />
    public async Task<OperationResult<RealmDto>> UpdateRealmAsync(
        string realmName, UpdateRealmRequest request, CancellationToken cancellationToken = default)
    {
        var token = await _adminTokenCache.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return OperationResult<RealmDto>.Fail("Failed to acquire Keycloak admin token", 503);
        }

        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}";

        var client = CreateAuthorizedClient(token);

        // GET current realm to merge changes
        var getResponse = await client.GetAsync(url, cancellationToken);

        if (getResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return OperationResult<RealmDto>.NotFound($"Realm '{realmName}' not found");
        }

        if (!getResponse.IsSuccessStatusCode)
        {
            return OperationResult<RealmDto>.Fail(
                $"Failed to retrieve realm '{realmName}' for update", (int)getResponse.StatusCode);
        }

        // Parse as a mutable JSON document and modify fields
        var existingJson = await getResponse.Content.ReadAsStringAsync(cancellationToken);
        using var jsonDoc = JsonDocument.Parse(existingJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in jsonDoc.RootElement.EnumerateObject())
            {
                if (request.DisplayName is not null && prop.Name == "displayName")
                {
                    writer.WriteString("displayName", request.DisplayName);
                }
                else if (request.Enabled is not null && prop.Name == "enabled")
                {
                    writer.WriteBoolean("enabled", request.Enabled.Value);
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        var updatedJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        var content = new StringContent(updatedJson, System.Text.Encoding.UTF8, "application/json");
        var putResponse = await client.PutAsync(url, content, cancellationToken);

        if (putResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return OperationResult<RealmDto>.NotFound($"Realm '{realmName}' not found");
        }

        if (!putResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "UpdateRealmAsync failed with status {StatusCode} for realm {RealmName}",
                (int)putResponse.StatusCode, realmName);
            return OperationResult<RealmDto>.Fail(
                $"Failed to update realm '{realmName}'", (int)putResponse.StatusCode);
        }

        // Re-fetch the realm to return the updated state
        return await GetRealmAsync(realmName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteRealmAsync(
        string realmName, CancellationToken cancellationToken = default)
    {
        var token = await _adminTokenCache.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return OperationResult.Fail("Failed to acquire Keycloak admin token", 503);
        }

        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}";

        var client = CreateAuthorizedClient(token);
        var response = await client.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return OperationResult.NotFound($"Realm '{realmName}' not found");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "DeleteRealmAsync failed with status {StatusCode} for realm {RealmName}",
                (int)response.StatusCode, realmName);
            return OperationResult.Fail(
                $"Failed to delete realm '{realmName}'", (int)response.StatusCode);
        }

        return OperationResult.Ok();
    }

    // ─── Client operations ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<OperationResult<IdentityProviderClientDto>> CreateClientAsync(
        string realmName, CreateIdentityProviderClientRequest request, CancellationToken cancellationToken = default)
    {
        var token = await _adminTokenCache.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return OperationResult<IdentityProviderClientDto>.Fail("Failed to acquire Keycloak admin token", 503);
        }

        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}/clients";

        var attributes = new Dictionary<string, string>();
        if (request.RequiresPkce)
        {
            attributes["pkce.code.challenge.method"] = "S256";
        }

        var body = new
        {
            clientId = request.ClientId,
            publicClient = false,
            redirectUris = request.RedirectUris,
            attributes
        };

        var client = CreateAuthorizedClient(token);
        var response = await client.PostAsJsonAsync(url, body, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return OperationResult<IdentityProviderClientDto>.Fail(
                $"Client '{request.ClientId}' already exists in realm '{realmName}'", 409, ErrorCodes.Conflict);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "CreateClientAsync failed with status {StatusCode} for client {ClientId} in realm {RealmName}",
                (int)response.StatusCode, request.ClientId, realmName);
            return OperationResult<IdentityProviderClientDto>.Fail(
                $"Failed to create client '{request.ClientId}'", (int)response.StatusCode);
        }

        // Re-fetch the client to get the full representation including generated secret
        return await GetClientAsync(realmName, request.ClientId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IdentityProviderClientDto>> GetClientAsync(
        string realmName, string clientId, CancellationToken cancellationToken = default)
    {
        var token = await _adminTokenCache.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return OperationResult<IdentityProviderClientDto>.Fail("Failed to acquire Keycloak admin token", 503);
        }

        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}/clients?clientId={Uri.EscapeDataString(clientId)}";

        var client = CreateAuthorizedClient(token);
        var response = await client.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetClientAsync failed with status {StatusCode} for client {ClientId} in realm {RealmName}",
                (int)response.StatusCode, clientId, realmName);
            return OperationResult<IdentityProviderClientDto>.Fail(
                $"Failed to retrieve client '{clientId}'", (int)response.StatusCode);
        }

        var clients = await response.Content.ReadFromJsonAsync<List<KeycloakClientRepresentation>>(JsonOptions, cancellationToken);

        if (clients is null || clients.Count == 0)
        {
            return OperationResult<IdentityProviderClientDto>.NotFound(
                $"Client '{clientId}' not found in realm '{realmName}'");
        }

        return OperationResult<IdentityProviderClientDto>.Ok(MapToClientDto(clients[0]));
    }

    /// <inheritdoc />
    public async Task<OperationResult<IdentityProviderClientDto>> UpdateClientAsync(
        string realmName, string clientId, UpdateIdentityProviderClientRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = await _adminTokenCache.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return OperationResult<IdentityProviderClientDto>.Fail("Failed to acquire Keycloak admin token", 503);
        }

        // Resolve the internal UUID for this clientId
        var internalId = await ResolveClientInternalIdAsync(token, realmName, clientId, cancellationToken);
        if (internalId is null)
        {
            return OperationResult<IdentityProviderClientDto>.NotFound(
                $"Client '{clientId}' not found in realm '{realmName}'");
        }

        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}/clients/{internalId}";

        var client = CreateAuthorizedClient(token);

        // GET current client representation to merge changes into
        var getResponse = await client.GetAsync(url, cancellationToken);

        if (getResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return OperationResult<IdentityProviderClientDto>.NotFound(
                $"Client '{clientId}' not found in realm '{realmName}'");
        }

        if (!getResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "UpdateClientAsync: GET failed with status {StatusCode} for client {ClientId} in realm {RealmName}",
                (int)getResponse.StatusCode, clientId, realmName);
            return OperationResult<IdentityProviderClientDto>.Fail(
                $"Failed to retrieve client '{clientId}' for update", (int)getResponse.StatusCode);
        }

        var existingJson = await getResponse.Content.ReadAsStringAsync(cancellationToken);
        var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingJson, JsonOptions);

        if (existing is null)
        {
            return OperationResult<IdentityProviderClientDto>.Fail("Failed to deserialize current client state", 500);
        }

        // Merge changes into the full representation
        var merged = new Dictionary<string, object?>(existing.Count);
        foreach (var kvp in existing)
        {
            merged[kvp.Key] = kvp.Value;
        }

        if (request.RedirectUris is not null)
        {
            merged["redirectUris"] = request.RedirectUris;
        }

        if (request.RequiresPkce is not null)
        {
            // Merge into existing attributes, preserving other attribute keys
            var existingAttributes = new Dictionary<string, string>();
            if (existing.TryGetValue("attributes", out var attrElement) && attrElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in attrElement.EnumerateObject())
                {
                    existingAttributes[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            if (request.RequiresPkce.Value)
            {
                existingAttributes["pkce.code.challenge.method"] = "S256";
            }
            else
            {
                existingAttributes["pkce.code.challenge.method"] = "";
            }

            merged["attributes"] = existingAttributes;
        }

        var putResponse = await client.PutAsJsonAsync(url, merged, JsonOptions, cancellationToken);

        if (putResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return OperationResult<IdentityProviderClientDto>.NotFound(
                $"Client '{clientId}' not found in realm '{realmName}'");
        }

        if (!putResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "UpdateClientAsync failed with status {StatusCode} for client {ClientId} in realm {RealmName}",
                (int)putResponse.StatusCode, clientId, realmName);
            return OperationResult<IdentityProviderClientDto>.Fail(
                $"Failed to update client '{clientId}'", (int)putResponse.StatusCode);
        }

        // Re-fetch to return updated state
        return await GetClientAsync(realmName, clientId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteClientAsync(
        string realmName, string clientId, CancellationToken cancellationToken = default)
    {
        var token = await _adminTokenCache.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return OperationResult.Fail("Failed to acquire Keycloak admin token", 503);
        }

        // Resolve the internal UUID for this clientId
        var internalId = await ResolveClientInternalIdAsync(token, realmName, clientId, cancellationToken);
        if (internalId is null)
        {
            return OperationResult.NotFound(
                $"Client '{clientId}' not found in realm '{realmName}'");
        }

        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}/clients/{internalId}";

        var client = CreateAuthorizedClient(token);
        var response = await client.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return OperationResult.NotFound(
                $"Client '{clientId}' not found in realm '{realmName}'");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "DeleteClientAsync failed with status {StatusCode} for client {ClientId} in realm {RealmName}",
                (int)response.StatusCode, clientId, realmName);
            return OperationResult.Fail(
                $"Failed to delete client '{clientId}'", (int)response.StatusCode);
        }

        return OperationResult.Ok();
    }

    // ─── User provisioning ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<OperationResult<ProvisionedUserDto>> ProvisionUserAsync(
        string realmName, ProvisionUserRequest request, CancellationToken cancellationToken = default)
    {
        var token = await _adminTokenCache.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return OperationResult<ProvisionedUserDto>.Fail("Failed to acquire Keycloak admin token", 503);
        }

        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}/users";

        // Split DisplayName into firstName / lastName
        var (firstName, lastName) = SplitDisplayName(request.DisplayName);

        // Build required actions list
        List<string>? requiredActions = null;
        if (request.RequirePasswordReset && request.InitialPassword is null)
        {
            requiredActions = ["UPDATE_PASSWORD"];
        }

        var userRepresentation = new KeycloakUserRepresentation(
            Id: string.Empty,
            Username: request.Email,
            Email: request.Email,
            FirstName: firstName,
            LastName: lastName,
            Enabled: true,
            EmailVerified: true,
            RequiredActions: requiredActions);

        var client = CreateAuthorizedClient(token);
        var response = await client.PostAsJsonAsync(url, userRepresentation, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return OperationResult<ProvisionedUserDto>.Fail(
                $"User with email '{request.Email}' already exists in realm '{realmName}'", 409, ErrorCodes.Conflict);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "ProvisionUserAsync failed with status {StatusCode} for email {Email} in realm {RealmName}",
                (int)response.StatusCode, request.Email, realmName);
            return OperationResult<ProvisionedUserDto>.Fail(
                $"Failed to provision user '{request.Email}'", (int)response.StatusCode);
        }

        // Extract userId from Location header (last segment)
        var userId = ExtractUserIdFromLocationHeader(response);
        if (userId is null)
        {
            _logger.LogError("ProvisionUserAsync: Location header missing or malformed for user {Email}", request.Email);
            return OperationResult<ProvisionedUserDto>.Fail(
                "User created but failed to extract user ID from response", 500);
        }

        // If InitialPassword is provided, set it via PUT reset-password
        if (request.InitialPassword is not null)
        {
            var setPasswordResult = await SetUserPasswordInternalAsync(
                client, opts, realmName, userId, request.InitialPassword,
                temporary: request.RequirePasswordReset, cancellationToken);

            if (!setPasswordResult)
            {
                _logger.LogWarning(
                    "ProvisionUserAsync: User {UserId} created but failed to set initial password", userId);
            }
        }

        var provisionedUser = new ProvisionedUserDto(
            userId,
            request.Email,
            request.DisplayName,
            request.RequirePasswordReset);

        return OperationResult<ProvisionedUserDto>.Ok(provisionedUser, statusCode: 201);
    }

    /// <inheritdoc />
    public async Task<OperationResult> SetUserCredentialsAsync(
        string realmName, string externalUserId, IdentityProviderUserCredentialsDto credentials,
        CancellationToken cancellationToken = default)
    {
        var token = await _adminTokenCache.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return OperationResult.Fail("Failed to acquire Keycloak admin token", 503);
        }

        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}/users/{externalUserId}/reset-password";

        var credentialBody = new KeycloakCredentialRepresentation(
            Type: "password",
            Value: credentials.Password,
            Temporary: credentials.Temporary);

        var client = CreateAuthorizedClient(token);
        var response = await client.PutAsJsonAsync(url, credentialBody, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return OperationResult.NotFound($"User '{externalUserId}' not found in realm '{realmName}'");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "SetUserCredentialsAsync failed with status {StatusCode} for user {UserId} in realm {RealmName}",
                (int)response.StatusCode, externalUserId, realmName);
            return OperationResult.Fail(
                $"Failed to set credentials for user '{externalUserId}'", (int)response.StatusCode);
        }

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteUserAsync(
        string realmName, string externalUserId, CancellationToken cancellationToken = default)
    {
        var token = await _adminTokenCache.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return OperationResult.Fail("Failed to acquire Keycloak admin token", 503);
        }

        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}/users/{externalUserId}";

        var client = CreateAuthorizedClient(token);
        var response = await client.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return OperationResult.NotFound($"User '{externalUserId}' not found in realm '{realmName}'");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "DeleteUserAsync failed with status {StatusCode} for user {UserId} in realm {RealmName}",
                (int)response.StatusCode, externalUserId, realmName);
            return OperationResult.Fail(
                $"Failed to delete user '{externalUserId}'", (int)response.StatusCode);
        }

        return OperationResult.Ok();
    }

    // ─── Private helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an HttpClient with the admin bearer token already set.
    /// </summary>
    private HttpClient CreateAuthorizedClient(string token)
    {
        var client = _httpClientFactory.CreateClient("KeycloakAdmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Resolves the internal Keycloak UUID for a client by its clientId.
    /// Returns null if the client is not found.
    /// </summary>
    private async Task<string?> ResolveClientInternalIdAsync(
        string token, string realmName, string clientId, CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}/clients?clientId={Uri.EscapeDataString(clientId)}";

        var client = CreateAuthorizedClient(token);
        var response = await client.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var clients = await response.Content.ReadFromJsonAsync<List<KeycloakClientRepresentation>>(JsonOptions, cancellationToken);

        return clients is { Count: > 0 } ? clients[0].Id : null;
    }

    /// <summary>
    /// Sets user password using the reset-password admin endpoint.
    /// Returns true on success, false on failure.
    /// </summary>
    private static async Task<bool> SetUserPasswordInternalAsync(
        HttpClient client, KeycloakOptions opts, string realmName, string userId,
        string password, bool temporary, CancellationToken cancellationToken)
    {
        var url = $"{opts.InternalBaseUrl.TrimEnd('/')}/admin/realms/{realmName}/users/{userId}/reset-password";

        var credentialBody = new KeycloakCredentialRepresentation(
            Type: "password",
            Value: password,
            Temporary: temporary);

        var response = await client.PutAsJsonAsync(url, credentialBody, JsonOptions, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Extracts the user ID from the Location header returned by Keycloak
    /// when creating a user. The header format is: .../users/{userId}
    /// </summary>
    private static string? ExtractUserIdFromLocationHeader(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.ToString();
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        // The userId is the last segment of the Location URI
        var lastSlash = location.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == location.Length - 1)
        {
            return null;
        }

        return location[(lastSlash + 1)..];
    }

    /// <summary>
    /// Splits a display name into firstName and lastName components.
    /// Uses first word as firstName and remainder as lastName.
    /// </summary>
    private static (string? FirstName, string? LastName) SplitDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return (null, null);
        }

        var parts = displayName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => (null, null),
            1 => (parts[0], null),
            _ => (parts[0], parts[1])
        };
    }

    /// <summary>
    /// Maps a Keycloak realm representation to a <see cref="RealmDto"/>.
    /// </summary>
    private static RealmDto MapToRealmDto(KeycloakRealmRepresentation realm)
    {
        return new RealmDto(
            realm.Realm,
            realm.DisplayName ?? realm.Realm,
            realm.Enabled);
    }

    /// <summary>
    /// Maps a Keycloak client representation to an <see cref="IdentityProviderClientDto"/>.
    /// Extracts PKCE requirement from the attributes dictionary.
    /// </summary>
    private static IdentityProviderClientDto MapToClientDto(KeycloakClientRepresentation client)
    {
        var requiresPkce = client.Attributes is not null
            && client.Attributes.TryGetValue("pkce.code.challenge.method", out var pkceMethod)
            && string.Equals(pkceMethod, "S256", StringComparison.OrdinalIgnoreCase);

        return new IdentityProviderClientDto(
            client.ClientId,
            client.Secret,
            (IReadOnlyList<string>)(client.RedirectUris?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>()),
            requiresPkce);
    }
}
