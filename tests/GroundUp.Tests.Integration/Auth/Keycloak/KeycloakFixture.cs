using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GroundUp.Auth.Keycloak;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.Keycloak;

namespace GroundUp.Tests.Integration.Auth.Keycloak;

/// <summary>
/// Shared xUnit fixture that starts a Keycloak container for integration testing.
/// Creates the groundup realm via the Admin REST API after startup.
/// </summary>
public sealed class KeycloakFixture : IAsyncLifetime
{
    private readonly KeycloakContainer _container;

    /// <summary>Admin username for the master realm.</summary>
    public const string AdminUsername = "admin";

    /// <summary>Admin password for the master realm.</summary>
    public const string AdminPassword = "admin";

    /// <summary>The default test realm name.</summary>
    public const string TestRealmName = "groundup";

    /// <summary>The default app client ID within the test realm.</summary>
    public const string AppClientId = "groundup-app";

    public KeycloakFixture()
    {
        // Use the Testcontainers.Keycloak builder — it handles port, health check, and startup
        _container = new KeycloakBuilder()
            .WithImage("quay.io/keycloak/keycloak:26.0")
            .Build();
    }

    /// <summary>
    /// The base URL of the running Keycloak container (e.g., http://localhost:{port}).
    /// </summary>
    public string BaseUrl => _container.GetBaseAddress().TrimEnd('/');

    /// <summary>
    /// Creates a <see cref="KeycloakOptions"/> configured to point at the test container.
    /// The admin client is registered in the master realm with full admin privileges.
    /// </summary>
    public KeycloakOptions CreateOptions() => new()
    {
        InternalBaseUrl = BaseUrl,
        PublicBaseUrl = BaseUrl,
        SharedRealmName = "master",  // Admin token comes from master realm
        AdminClientId = "admin-cli",
        AdminClientSecret = "admin-cli-secret",
        AppClientId = AppClientId
    };

    /// <summary>
    /// Creates options pointing at the test realm (for IdP service tests that operate within a realm).
    /// </summary>
    public KeycloakOptions CreateRealmOptions() => new()
    {
        InternalBaseUrl = BaseUrl,
        PublicBaseUrl = BaseUrl,
        SharedRealmName = TestRealmName,
        AdminClientId = "admin-cli",
        AdminClientSecret = "admin-cli-secret",
        AppClientId = AppClientId
    };

    /// <summary>
    /// Creates an <see cref="IOptionsMonitor{KeycloakOptions}"/> wrapping master realm options (for admin operations).
    /// </summary>
    public IOptionsMonitor<KeycloakOptions> CreateOptionsMonitor()
    {
        var monitor = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        monitor.CurrentValue.Returns(CreateOptions());
        monitor.OnChange(Arg.Any<Action<KeycloakOptions, string>>())
            .Returns(Substitute.For<IDisposable>());
        return monitor;
    }

    /// <summary>
    /// Creates an <see cref="IOptionsMonitor{KeycloakOptions}"/> wrapping test realm options (for IdP service tests).
    /// </summary>
    public IOptionsMonitor<KeycloakOptions> CreateRealmOptionsMonitor()
    {
        var monitor = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        monitor.CurrentValue.Returns(CreateRealmOptions());
        monitor.OnChange(Arg.Any<Action<KeycloakOptions, string>>())
            .Returns(Substitute.For<IDisposable>());
        return monitor;
    }

    /// <summary>
    /// Creates an HttpClient pointing at the Keycloak container.
    /// </summary>
    public HttpClient CreateHttpClient() => new()
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// Creates an <see cref="IHttpClientFactory"/> that returns clients pointing at the container.
    /// </summary>
    public IHttpClientFactory CreateHttpClientFactory()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("KeycloakIdp").Returns(_ => CreateHttpClient());
        factory.CreateClient("KeycloakAdmin").Returns(_ => CreateHttpClient());
        return factory;
    }

    /// <summary>
    /// Creates a fully configured <see cref="AdminTokenCache"/> for integration tests.
    /// </summary>
    internal AdminTokenCache CreateAdminTokenCache()
    {
        var factory = CreateHttpClientFactory();
        var monitor = CreateOptionsMonitor();
        var logger = NullLogger<AdminTokenCache>.Instance;
        return new AdminTokenCache(factory, monitor, logger);
    }

    /// <summary>
    /// Acquires an admin token for direct API calls in test setup.
    /// </summary>
    public async Task<string> GetAdminTokenAsync()
    {
        using var client = CreateHttpClient();
        var tokenUrl = $"{BaseUrl}/realms/master/protocol/openid-connect/token";

        // Try with client_secret first (after setup), fall back to without (during setup)
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["client_secret"] = "admin-cli-secret",
            ["username"] = AdminUsername,
            ["password"] = AdminPassword
        });

        var response = await client.PostAsync(tokenUrl, formData);
        
        if (!response.IsSuccessStatusCode)
        {
            // Fallback: admin-cli might still be public (during initial setup)
            formData = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = AdminUsername,
                ["password"] = AdminPassword
            });
            response = await client.PostAsync(tokenUrl, formData);
        }
        
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("access_token").GetString()!;
    }

    public async Task InitializeAsync()
    {
        // The KeycloakBuilder handles the wait strategy internally (waits for HTTP 200 on /health/ready)
        await _container.StartAsync();

        // After container is ready, create the test realm via Admin REST API
        await CreateTestRealmAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates the groundup test realm with the app client and admin service account via the Admin REST API.
    /// More reliable than file-based realm import which can have path issues in containers.
    /// </summary>
    private async Task CreateTestRealmAsync()
    {
        var adminToken = await GetAdminTokenAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Create the realm with all verification disabled for testing
        var realmBody = new
        {
            realm = TestRealmName,
            enabled = true,
            registrationAllowed = false,
            loginWithEmailAllowed = true,
            duplicateEmailsAllowed = false,
            verifyEmail = false,
            requiredActions = Array.Empty<object>(),
            defaultDefaultClientScopes = new[] { "web-origins", "acr", "roles", "profile", "email" }
        };

        var createRealmResponse = await client.PostAsJsonAsync($"{BaseUrl}/admin/realms", realmBody);
        createRealmResponse.EnsureSuccessStatusCode();

        // Create the groundup-app client (public client with PKCE and direct access grants for testing)
        var appClientBody = new
        {
            clientId = AppClientId,
            enabled = true,
            publicClient = true,
            standardFlowEnabled = true,
            directAccessGrantsEnabled = true,
            redirectUris = new[] { "https://localhost:*/auth/callback", "http://localhost:*/auth/callback" },
            webOrigins = new[] { "https://localhost:*", "http://localhost:*" },
            attributes = new Dictionary<string, string>
            {
                ["pkce.code.challenge.method"] = "S256"
            }
        };

        var createAppClientResponse = await client.PostAsJsonAsync(
            $"{BaseUrl}/admin/realms/{TestRealmName}/clients", appClientBody);
        createAppClientResponse.EnsureSuccessStatusCode();

        // Modify admin-cli to be a confidential client with service accounts
        // This gives the client_credentials grant the same permissions as the admin user
        var adminCliClients = await client.GetFromJsonAsync<JsonElement[]>(
            $"{BaseUrl}/admin/realms/master/clients?clientId=admin-cli");
        var adminCliInternalId = adminCliClients![0].GetProperty("id").GetString()!;

        var updateAdminCli = new
        {
            clientId = "admin-cli",
            publicClient = false,
            secret = "admin-cli-secret",
            serviceAccountsEnabled = true,
            directAccessGrantsEnabled = true
        };

        var updateResponse = await client.PutAsJsonAsync(
            $"{BaseUrl}/admin/realms/master/clients/{adminCliInternalId}", updateAdminCli);
        updateResponse.EnsureSuccessStatusCode();;

        // Get the service account user for admin-cli and assign admin roles
        var serviceAccountUser = await client.GetFromJsonAsync<JsonElement>(
            $"{BaseUrl}/admin/realms/master/clients/{adminCliInternalId}/service-account-user");
        var serviceAccountUserId = serviceAccountUser!.GetProperty("id").GetString()!;

        // Assign the master realm's "admin" realm role to the service account
        // AND the master-realm client's admin role for full cross-realm management
        var adminRealmRoles = await client.GetFromJsonAsync<JsonElement[]>(
            $"{BaseUrl}/admin/realms/master/roles");
        
        var rolesToAssign = new List<object>();
        
        foreach (var role in adminRealmRoles!)
        {
            var roleName = role.GetProperty("name").GetString();
            if (roleName == "admin" || roleName == "create-realm")
            {
                rolesToAssign.Add(new
                {
                    id = role.GetProperty("id").GetString(),
                    name = roleName
                });
            }
        }

        if (rolesToAssign.Count > 0)
        {
            var assignResponse = await client.PostAsJsonAsync(
                $"{BaseUrl}/admin/realms/master/users/{serviceAccountUserId}/role-mappings/realm",
                rolesToAssign);
            assignResponse.EnsureSuccessStatusCode();
        }

        // Also assign ALL client roles from the master-realm client
        // This is what the built-in admin user has and is required for managing other realms
        var masterRealmClients = await client.GetFromJsonAsync<JsonElement[]>(
            $"{BaseUrl}/admin/realms/master/clients?clientId=master-realm");
        
        if (masterRealmClients is { Length: > 0 })
        {
            var masterRealmClientId = masterRealmClients[0].GetProperty("id").GetString()!;
            
            // Get all available client roles
            var clientRoles = await client.GetFromJsonAsync<JsonElement[]>(
                $"{BaseUrl}/admin/realms/master/clients/{masterRealmClientId}/roles");
            
            if (clientRoles is { Length: > 0 })
            {
                var clientRolesToAssign = clientRoles.Select(r => new
                {
                    id = r.GetProperty("id").GetString(),
                    name = r.GetProperty("name").GetString()
                }).ToList();

                var clientRoleResponse = await client.PostAsJsonAsync(
                    $"{BaseUrl}/admin/realms/master/users/{serviceAccountUserId}/role-mappings/clients/{masterRealmClientId}",
                    clientRolesToAssign);
                clientRoleResponse.EnsureSuccessStatusCode();
            }
        }
    }

    /// <summary>
    /// Finds the realm.json file by walking up from the test binary directory.
    /// Kept for potential future use.
    /// </summary>
    private static string FindRealmJsonPath()
    {
        var dir = AppContext.BaseDirectory;

        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "keycloak", "realm.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException(
            "Could not find keycloak/realm.json relative to the test binary directory.");
    }
}

/// <summary>
/// xUnit collection definition that shares a single <see cref="KeycloakFixture"/>
/// across all test classes in the "Keycloak" collection.
/// </summary>
[CollectionDefinition("Keycloak")]
public sealed class KeycloakCollection : ICollectionFixture<KeycloakFixture>
{
}
