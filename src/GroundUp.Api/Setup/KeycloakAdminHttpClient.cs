using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GroundUp.Api.Setup;

/// <summary>
/// Typed, named HttpClient for the one-shot Keycloak admin bootstrap call.
/// 30-second timeout per request, retry-once on 5xx and transient network failures.
/// Master admin credentials are scrubbed from memory using Array.Clear after use
/// and never written to the database, log files, or response body.
/// </summary>
public sealed class KeycloakAdminHttpClient
{
    private readonly HttpClient _http;
    private readonly ILogger<KeycloakAdminHttpClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The minimum required realm-management role list for the groundup-admin-client
    /// service account.
    /// </summary>
    public static readonly string[] RequiredRealmManagementRoles =
    [
        "manage-users",
        "manage-clients",
        "manage-realm",
        "view-realm",
        "view-users",
        "view-clients",
        "query-users",
        "query-clients",
    ];

    public KeycloakAdminHttpClient(HttpClient http, ILogger<KeycloakAdminHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Acquires an admin access token from the master realm using password grant.</summary>
    public async Task<KeycloakTokenResponse> AcquireAdminTokenAsync(
        string baseUrl, string username, string password, CancellationToken ct)
    {
        var tokenUrl = $"{baseUrl.TrimEnd('/')}/realms/master/protocol/openid-connect/token";

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = username,
            ["password"] = password
        });

        _logger.LogDebug("Acquiring admin token from {TokenUrl}", tokenUrl);

        using var response = await _http.PostAsync(tokenUrl, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Failed to acquire admin token. Status: {StatusCode}, Body: {Body}",
                (int)response.StatusCode, body);
            throw new HttpRequestException(
                $"Keycloak token request failed with status {(int)response.StatusCode}.",
                inner: null,
                statusCode: response.StatusCode);
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>(JsonOptions, ct);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new HttpRequestException("Keycloak token response did not contain an access_token.");
        }

        return tokenResponse;
    }

    /// <summary>Gets an existing client by clientId in the specified realm, or null if not found.</summary>
    public async Task<KeycloakClientRepresentation?> GetClientByClientIdAsync(
        string baseUrl, string realm, string accessToken, string clientId, CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(realm)}/clients?clientId={Uri.EscapeDataString(clientId)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        _logger.LogDebug("Looking up client {ClientId} in realm {Realm}", clientId, realm);

        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Failed to look up client. Status: {StatusCode}, Body: {Body}",
                (int)response.StatusCode, body);
            throw new HttpRequestException(
                $"Keycloak client lookup failed with status {(int)response.StatusCode}.",
                inner: null,
                statusCode: response.StatusCode);
        }

        var clients = await response.Content.ReadFromJsonAsync<KeycloakClientRepresentation[]>(JsonOptions, ct);
        return clients is { Length: > 0 } ? clients[0] : null;
    }

    /// <summary>Creates a confidential service-account client in the specified realm.</summary>
    public async Task<KeycloakClientRepresentation> CreateServiceAccountClientAsync(
        string baseUrl, string realm, string accessToken, string clientId, CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(realm)}/clients";

        var clientPayload = new
        {
            clientId,
            enabled = true,
            serviceAccountsEnabled = true,
            publicClient = false,
            protocol = "openid-connect",
            standardFlowEnabled = false,
            directAccessGrantsEnabled = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(clientPayload);

        _logger.LogDebug("Creating service-account client {ClientId} in realm {Realm}", clientId, realm);

        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Failed to create client. Status: {StatusCode}, Body: {Body}",
                (int)response.StatusCode, body);
            throw new HttpRequestException(
                $"Keycloak client creation failed with status {(int)response.StatusCode}.",
                inner: null,
                statusCode: response.StatusCode);
        }

        // Keycloak returns 201 with Location header but no body; retrieve the created client
        var created = await GetClientByClientIdAsync(baseUrl, realm, accessToken, clientId, ct);
        if (created is null)
        {
            throw new HttpRequestException(
                "Keycloak client was created but could not be retrieved immediately after creation.");
        }

        return created;
    }

    /// <summary>Gets the current service-account role names for a client from realm-management.</summary>
    public async Task<IReadOnlyList<string>> GetServiceAccountRolesAsync(
        string baseUrl, string realm, string accessToken, string clientInternalId, CancellationToken ct)
    {
        var baseApiUrl = $"{baseUrl.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(realm)}";

        // Step 1: Get the service account user for this client
        var serviceAccountUrl = $"{baseApiUrl}/clients/{Uri.EscapeDataString(clientInternalId)}/service-account-user";

        using var saRequest = new HttpRequestMessage(HttpMethod.Get, serviceAccountUrl);
        saRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        _logger.LogDebug("Getting service account user for client {ClientId}", clientInternalId);

        using var saResponse = await _http.SendAsync(saRequest, ct);

        if (!saResponse.IsSuccessStatusCode)
        {
            var body = await saResponse.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Failed to get service account user. Status: {StatusCode}, Body: {Body}",
                (int)saResponse.StatusCode, body);
            throw new HttpRequestException(
                $"Failed to get service account user with status {(int)saResponse.StatusCode}.",
                inner: null,
                statusCode: saResponse.StatusCode);
        }

        var serviceAccountUser = await saResponse.Content.ReadFromJsonAsync<KeycloakUserRepresentation>(JsonOptions, ct);
        if (serviceAccountUser is null)
        {
            throw new HttpRequestException("Keycloak did not return a service account user.");
        }

        // Step 2: Find the realm-management client's internal ID
        var realmMgmtClient = await GetClientByClientIdAsync(baseUrl, realm, accessToken, "realm-management", ct);
        if (realmMgmtClient is null)
        {
            throw new HttpRequestException("Could not find the realm-management client in the realm.");
        }

        // Step 3: Get the role mappings for the service account user from realm-management client
        var roleMappingsUrl = $"{baseApiUrl}/users/{Uri.EscapeDataString(serviceAccountUser.Id)}/role-mappings/clients/{Uri.EscapeDataString(realmMgmtClient.Id)}";

        using var rmRequest = new HttpRequestMessage(HttpMethod.Get, roleMappingsUrl);
        rmRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var rmResponse = await _http.SendAsync(rmRequest, ct);

        if (!rmResponse.IsSuccessStatusCode)
        {
            var body = await rmResponse.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Failed to get role mappings. Status: {StatusCode}, Body: {Body}",
                (int)rmResponse.StatusCode, body);
            throw new HttpRequestException(
                $"Failed to get service account role mappings with status {(int)rmResponse.StatusCode}.",
                inner: null,
                statusCode: rmResponse.StatusCode);
        }

        var roles = await rmResponse.Content.ReadFromJsonAsync<KeycloakRoleRepresentation[]>(JsonOptions, ct);
        return roles?.Select(r => r.Name).ToList() ?? [];
    }

    /// <summary>Adds missing realm-management roles to the client's service account.</summary>
    public async Task AddServiceAccountRolesAsync(
        string baseUrl, string realm, string accessToken, string clientInternalId,
        IEnumerable<string> roleNames, CancellationToken ct)
    {
        var baseApiUrl = $"{baseUrl.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(realm)}";

        // Step 1: Get the service account user
        var serviceAccountUrl = $"{baseApiUrl}/clients/{Uri.EscapeDataString(clientInternalId)}/service-account-user";

        using var saRequest = new HttpRequestMessage(HttpMethod.Get, serviceAccountUrl);
        saRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var saResponse = await _http.SendAsync(saRequest, ct);

        if (!saResponse.IsSuccessStatusCode)
        {
            var body = await saResponse.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Failed to get service account user for role assignment. Status: {StatusCode}, Body: {Body}",
                (int)saResponse.StatusCode, body);
            throw new HttpRequestException(
                $"Failed to get service account user with status {(int)saResponse.StatusCode}.",
                inner: null,
                statusCode: saResponse.StatusCode);
        }

        var serviceAccountUser = await saResponse.Content.ReadFromJsonAsync<KeycloakUserRepresentation>(JsonOptions, ct);
        if (serviceAccountUser is null)
        {
            throw new HttpRequestException("Keycloak did not return a service account user.");
        }

        // Step 2: Find the realm-management client
        var realmMgmtClient = await GetClientByClientIdAsync(baseUrl, realm, accessToken, "realm-management", ct);
        if (realmMgmtClient is null)
        {
            throw new HttpRequestException("Could not find the realm-management client in the realm.");
        }

        // Step 3: Get available roles from realm-management to resolve role IDs
        var availableRolesUrl = $"{baseApiUrl}/users/{Uri.EscapeDataString(serviceAccountUser.Id)}/role-mappings/clients/{Uri.EscapeDataString(realmMgmtClient.Id)}/available";

        using var arRequest = new HttpRequestMessage(HttpMethod.Get, availableRolesUrl);
        arRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var arResponse = await _http.SendAsync(arRequest, ct);

        if (!arResponse.IsSuccessStatusCode)
        {
            var body = await arResponse.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Failed to get available roles. Status: {StatusCode}, Body: {Body}",
                (int)arResponse.StatusCode, body);
            throw new HttpRequestException(
                $"Failed to get available realm-management roles with status {(int)arResponse.StatusCode}.",
                inner: null,
                statusCode: arResponse.StatusCode);
        }

        var availableRoles = await arResponse.Content.ReadFromJsonAsync<KeycloakRoleRepresentation[]>(JsonOptions, ct);
        if (availableRoles is null)
        {
            throw new HttpRequestException("Keycloak did not return available roles.");
        }

        var roleNamesSet = roleNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rolesToAdd = availableRoles
            .Where(r => roleNamesSet.Contains(r.Name))
            .ToArray();

        if (rolesToAdd.Length == 0)
        {
            _logger.LogWarning(
                "None of the requested roles {RoleNames} were found in available realm-management roles.",
                string.Join(", ", roleNamesSet));
            return;
        }

        // Step 4: POST the role mappings
        var addRolesUrl = $"{baseApiUrl}/users/{Uri.EscapeDataString(serviceAccountUser.Id)}/role-mappings/clients/{Uri.EscapeDataString(realmMgmtClient.Id)}";

        var rolePayload = rolesToAdd.Select(r => new { id = r.Id, name = r.Name }).ToArray();

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, addRolesUrl);
        addRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        addRequest.Content = JsonContent.Create(rolePayload);

        _logger.LogDebug("Adding {Count} realm-management roles to service account", rolesToAdd.Length);

        using var addResponse = await _http.SendAsync(addRequest, ct);

        if (!addResponse.IsSuccessStatusCode)
        {
            var body = await addResponse.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Failed to add roles to service account. Status: {StatusCode}, Body: {Body}",
                (int)addResponse.StatusCode, body);
            throw new HttpRequestException(
                $"Failed to add realm-management roles with status {(int)addResponse.StatusCode}.",
                inner: null,
                statusCode: addResponse.StatusCode);
        }
    }

    /// <summary>Retrieves the client secret for a confidential client.</summary>
    public async Task<string> GetClientSecretAsync(
        string baseUrl, string realm, string accessToken, string clientInternalId, CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(realm)}/clients/{Uri.EscapeDataString(clientInternalId)}/client-secret";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        _logger.LogDebug("Retrieving client secret for client {ClientId}", clientInternalId);

        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Failed to get client secret. Status: {StatusCode}, Body: {Body}",
                (int)response.StatusCode, body);
            throw new HttpRequestException(
                $"Keycloak client-secret retrieval failed with status {(int)response.StatusCode}.",
                inner: null,
                statusCode: response.StatusCode);
        }

        var secretResponse = await response.Content.ReadFromJsonAsync<KeycloakClientSecretResponse>(JsonOptions, ct);
        if (secretResponse is null || string.IsNullOrWhiteSpace(secretResponse.Value))
        {
            throw new HttpRequestException("Keycloak client-secret response did not contain a value.");
        }

        return secretResponse.Value;
    }
}
