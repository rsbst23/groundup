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
/// Imports the groundup realm from keycloak/realm.json and exposes admin credentials
/// and HttpClient factory for test setup.
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
        var realmJsonPath = FindRealmJsonPath();

        _container = new KeycloakBuilder()
            .WithImage("quay.io/keycloak/keycloak:26.0")
            .WithResourceMapping(realmJsonPath, "/opt/keycloak/data/import/")
            .WithCommand("start-dev", "--import-realm")
            .Build();
    }

    /// <summary>
    /// The base URL of the running Keycloak container (e.g., http://localhost:{port}).
    /// </summary>
    public string BaseUrl => _container.GetBaseAddress().TrimEnd('/');

    /// <summary>
    /// Creates a <see cref="KeycloakOptions"/> configured to point at the test container.
    /// </summary>
    public KeycloakOptions CreateOptions() => new()
    {
        InternalBaseUrl = BaseUrl,
        PublicBaseUrl = BaseUrl,
        SharedRealmName = TestRealmName,
        AdminClientId = "admin-cli",
        AdminClientSecret = AdminPassword, // In dev mode, admin-cli uses the admin password
        AppClientId = AppClientId
    };

    /// <summary>
    /// Creates an <see cref="IOptionsMonitor{KeycloakOptions}"/> wrapping test options.
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
    /// Creates an HttpClient pointing at the Keycloak container with no special headers.
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
    /// Uses the resource owner password credentials grant against the master realm.
    /// </summary>
    public async Task<string> GetAdminTokenAsync()
    {
        using var client = CreateHttpClient();
        var tokenUrl = $"{BaseUrl}/realms/master/protocol/openid-connect/token";

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = AdminUsername,
            ["password"] = AdminPassword
        });

        var response = await client.PostAsync(tokenUrl, formData);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("access_token").GetString()!;
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Wait for Keycloak to be fully ready by hitting the realms endpoint
        // Keycloak takes 30-60s to start (Java cold start + realm import)
        using var client = CreateHttpClient();
        var ready = false;
        var retries = 0;
        const int maxRetries = 60;

        while (!ready && retries < maxRetries)
        {
            try
            {
                var response = await client.GetAsync($"{BaseUrl}/realms/{TestRealmName}");
                ready = response.IsSuccessStatusCode;
            }
            catch
            {
                // Container not ready yet
            }

            if (!ready)
            {
                await Task.Delay(2000);
                retries++;
            }
        }

        if (!ready)
        {
            throw new InvalidOperationException(
                $"Keycloak container did not become ready within {maxRetries * 2} seconds. BaseUrl: {BaseUrl}");
        }
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Finds the realm.json file by walking up from the test binary directory.
    /// </summary>
    private static string FindRealmJsonPath()
    {
        var dir = AppContext.BaseDirectory;

        // Walk up to find the solution root (contains keycloak/realm.json)
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
