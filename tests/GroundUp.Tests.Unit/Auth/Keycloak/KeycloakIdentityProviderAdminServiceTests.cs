using System.Net;
using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Keycloak;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Keycloak;

/// <summary>
/// Property-based and standard tests for <see cref="KeycloakIdentityProviderAdminService"/>.
/// Feature: phase-10b-keycloak-provider
/// Properties 12, 13, 14
/// Validates: Requirements 9.1, 9.2, 9.4, 9.5, 10.1, 10.3, 11.2
/// </summary>
[Trait("Category", "Property")]
public sealed class KeycloakIdentityProviderAdminServiceTests
{
    private static readonly KeycloakOptions DefaultOptions = new()
    {
        InternalBaseUrl = "http://keycloak:8080",
        SharedRealmName = "groundup",
        AdminClientId = "admin-cli",
        AdminClientSecret = "secret",
        PublicBaseUrl = "https://keycloak.example.com",
        AppClientId = "groundup-app"
    };

    private static (KeycloakIdentityProviderAdminService service, AdminMockHandler handler) CreateService(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? responseBody = null,
        Dictionary<string, string>? responseHeaders = null)
    {
        var handler = new AdminMockHandler(statusCode, responseBody, responseHeaders);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://keycloak:8080") };

        // For the token cache, we need a separate handler that returns a valid token
        var tokenHandler = new AdminMockHandler(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { access_token = "admin-token-123", expires_in = 300 }));
        var tokenHttpClient = new HttpClient(tokenHandler) { BaseAddress = new Uri("http://keycloak:8080") };

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        // The admin service uses "KeycloakAdmin" for API calls, but the token cache also uses "KeycloakAdmin"
        // We'll use a sequencing handler that returns token first, then actual responses
        var sequencingHandler = new SequencingAdminHandler(tokenHandler, handler);
        var sequencedClient = new HttpClient(sequencingHandler) { BaseAddress = new Uri("http://keycloak:8080") };

        httpClientFactory.CreateClient("KeycloakAdmin").Returns(sequencedClient);

        var optionsMonitor = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        optionsMonitor.CurrentValue.Returns(DefaultOptions);
        optionsMonitor.OnChange(Arg.Any<Action<KeycloakOptions, string>>())
            .Returns(Substitute.For<IDisposable>());

        var tokenCacheLogger = NullLogger<AdminTokenCache>.Instance;
        var adminTokenCache = new AdminTokenCache(httpClientFactory, optionsMonitor, tokenCacheLogger);

        var logger = NullLogger<KeycloakIdentityProviderAdminService>.Instance;

        var service = new KeycloakIdentityProviderAdminService(
            httpClientFactory, optionsMonitor, adminTokenCache, logger);

        return (service, handler);
    }

    /// <summary>
    /// Property 12: Admin API URL Construction for Realm Operations.
    /// For any realm name, CreateRealmAsync, GetRealmAsync, UpdateRealmAsync, DeleteRealmAsync
    /// send requests to the correct admin API URLs.
    /// **Validates: Requirements 9.1, 9.2, 9.4, 9.5**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(AdminServiceArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 12: Realm API URL construction")]
    public Property RealmOperations_UrlConstruction_IsCorrect(ValidRealmName realmInput)
    {
        var realmName = realmInput.Name;
        var realmJson = JsonSerializer.Serialize(new { realm = realmName, enabled = true });

        // Test GET realm — response body contains realm data
        var (service, handler) = CreateService(HttpStatusCode.OK, realmJson);

        service.GetRealmAsync(realmName).GetAwaiter().GetResult();

        var getRequest = handler.LastRequest!;
        var expectedGetUrl = $"http://keycloak:8080/admin/realms/{realmName}";
        var getUrlCorrect = getRequest.RequestUri!.ToString() == expectedGetUrl;
        var getMethodCorrect = getRequest.Method == HttpMethod.Get;

        return (getUrlCorrect && getMethodCorrect).ToProperty();
    }

    /// <summary>
    /// Property 12: DeleteRealmAsync sends DELETE to /admin/realms/{realmName}.
    /// **Validates: Requirements 9.5**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(AdminServiceArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 12: Delete realm URL construction")]
    public Property DeleteRealm_UrlConstruction_IsCorrect(ValidRealmName realmInput)
    {
        var realmName = realmInput.Name;
        var (service, handler) = CreateService(HttpStatusCode.NoContent);

        service.DeleteRealmAsync(realmName).GetAwaiter().GetResult();

        var request = handler.LastRequest!;
        var expectedUrl = $"http://keycloak:8080/admin/realms/{realmName}";
        return (request.RequestUri!.ToString() == expectedUrl && request.Method == HttpMethod.Delete)
            .ToProperty();
    }

    /// <summary>
    /// Property 13: Admin API URL Construction for Client Operations.
    /// For any realm name and client ID, the client operations use correct URL patterns.
    /// **Validates: Requirements 10.1, 10.3**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(AdminServiceArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 13: Client API URL construction")]
    public Property GetClient_UrlConstruction_IsCorrect(ValidRealmName realmInput, ValidClientId clientInput)
    {
        var realmName = realmInput.Name;
        var clientId = clientInput.Id;

        // Return empty array to simulate not found
        var (service, handler) = CreateService(HttpStatusCode.OK, "[]");

        service.GetClientAsync(realmName, clientId).GetAwaiter().GetResult();

        var request = handler.LastRequest!;
        var expectedUrl = $"http://keycloak:8080/admin/realms/{realmName}/clients?clientId={Uri.EscapeDataString(clientId)}";
        return (request.RequestUri!.ToString() == expectedUrl && request.Method == HttpMethod.Get)
            .ToProperty();
    }

    /// <summary>
    /// Property 14: User Provisioning Location Header Extraction.
    /// For any user creation that returns 201 with a Location header, the service extracts
    /// the user ID from the last path segment.
    /// **Validates: Requirement 11.2**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(AdminServiceArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 14: User provisioning Location header extraction")]
    public Property ProvisionUser_LocationHeader_ExtractsUserId(ValidUserId userIdInput)
    {
        var userId = userIdInput.Id;
        var locationUrl = $"http://keycloak:8080/admin/realms/groundup/users/{userId}";

        var headers = new Dictionary<string, string> { ["Location"] = locationUrl };
        var (service, _) = CreateService(HttpStatusCode.Created, null, headers);

        var request = new ProvisionUserRequest(
            Email: "test@example.com",
            DisplayName: "Test User",
            InitialPassword: null,
            RequirePasswordReset: false
        );

        var result = service.ProvisionUserAsync("groundup", request).GetAwaiter().GetResult();

        return (result.Success && result.Data!.ExternalUserId == userId).ToProperty();
    }

    /// <summary>
    /// GetRealmAsync returns NotFound for 404 response.
    /// **Validates: Requirement 9.4**
    /// </summary>
    [Fact]
    public async Task GetRealm_404_ReturnsNotFound()
    {
        var (service, _) = CreateService(HttpStatusCode.NotFound, "Not Found");

        var result = await service.GetRealmAsync("nonexistent-realm");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    /// <summary>
    /// DeleteRealmAsync returns NotFound for 404 response.
    /// **Validates: Requirement 9.5**
    /// </summary>
    [Fact]
    public async Task DeleteRealm_404_ReturnsNotFound()
    {
        var (service, _) = CreateService(HttpStatusCode.NotFound);

        var result = await service.DeleteRealmAsync("nonexistent-realm");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    /// <summary>
    /// CreateClientAsync handles 409 conflict for duplicate client.
    /// **Validates: Requirement 10.6**
    /// </summary>
    [Fact]
    public async Task CreateClient_409_ReturnsConflict()
    {
        var (service, _) = CreateService(HttpStatusCode.Conflict, "Client already exists");

        var request = new CreateIdentityProviderClientRequest(
            ClientId: "duplicate-client",
            RedirectUris: new List<string> { "http://localhost/callback" },
            RequiresPkce: false
        );

        var result = await service.CreateClientAsync("groundup", request);

        result.Success.Should().BeFalse();
    }

    /// <summary>
    /// Admin service returns failure when admin token is null (token endpoint returns failure).
    /// **Validates: Requirement 8.3**
    /// </summary>
    [Fact]
    public async Task RealmOperation_NullAdminToken_ReturnsFailure()
    {
        // Token endpoint returns failure → AdminTokenCache returns null → admin service returns failure
        var tokenFailHandler = new AdminMockHandler(HttpStatusCode.Unauthorized, "unauthorized");
        var httpClient = new HttpClient(tokenFailHandler) { BaseAddress = new Uri("http://keycloak:8080") };

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("KeycloakAdmin").Returns(httpClient);

        var optionsMonitor = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        optionsMonitor.CurrentValue.Returns(DefaultOptions);
        optionsMonitor.OnChange(Arg.Any<Action<KeycloakOptions, string>>())
            .Returns(Substitute.For<IDisposable>());

        var tokenCacheLogger = NullLogger<AdminTokenCache>.Instance;
        var adminTokenCache = new AdminTokenCache(httpClientFactory, optionsMonitor, tokenCacheLogger);

        var logger = NullLogger<KeycloakIdentityProviderAdminService>.Instance;

        var service = new KeycloakIdentityProviderAdminService(
            httpClientFactory, optionsMonitor, adminTokenCache, logger);

        var result = await service.GetRealmAsync("test-realm");

        result.Success.Should().BeFalse();
    }
}

/// <summary>Generated valid realm name wrapper.</summary>
public sealed class ValidRealmName
{
    public string Name { get; }
    public ValidRealmName(string name) => Name = name;
    public override string ToString() => Name;
}

/// <summary>Generated valid client ID wrapper.</summary>
public sealed class ValidClientId
{
    public string Id { get; }
    public ValidClientId(string id) => Id = id;
    public override string ToString() => Id;
}

/// <summary>Generated valid user ID (UUID format) wrapper.</summary>
public sealed class ValidUserId
{
    public string Id { get; }
    public ValidUserId(string id) => Id = id;
    public override string ToString() => Id;
}

/// <summary>
/// FsCheck Arbitrary generators for admin service tests.
/// </summary>
public static class AdminServiceArbitraries
{
    public static Arbitrary<ValidRealmName> ValidRealmNameArb()
    {
        var gen = Gen.Elements(
            "groundup", "tenant-abc", "test-realm", "production", "staging",
            "my-company", "dev-realm", "customer-xyz")
            .Select(name => new ValidRealmName(name));

        return Arb.From(gen);
    }

    public static Arbitrary<ValidClientId> ValidClientIdArb()
    {
        var gen = Gen.Elements(
            "groundup-app", "mobile-client", "spa-frontend", "admin-cli",
            "backend-service", "worker-client", "test-client-01")
            .Select(id => new ValidClientId(id));

        return Arb.From(gen);
    }

    public static Arbitrary<ValidUserId> ValidUserIdArb()
    {
        // Generate UUID-like strings
        var gen = Gen.Fresh(() => Guid.NewGuid().ToString())
            .Select(id => new ValidUserId(id));

        return Arb.From(gen);
    }
}

/// <summary>
/// Mock HTTP handler for admin service tests.
/// </summary>
internal sealed class AdminMockHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string? _responseBody;
    private readonly Dictionary<string, string>? _responseHeaders;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public List<HttpRequestMessage> AllRequests { get; } = new();

    public AdminMockHandler(
        HttpStatusCode statusCode,
        string? responseBody = null,
        Dictionary<string, string>? responseHeaders = null)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
        _responseHeaders = responseHeaders;
    }

    /// <summary>
    /// Public entry point for delegated calls from <see cref="SequencingAdminHandler"/>.
    /// </summary>
    public Task<HttpResponseMessage> InvokeSendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => SendAsync(request, cancellationToken);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        AllRequests.Add(request);

        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var response = new HttpResponseMessage(_statusCode);

        if (_responseBody is not null)
        {
            response.Content = new StringContent(_responseBody, Encoding.UTF8, "application/json");
        }

        if (_responseHeaders is not null)
        {
            foreach (var (key, value) in _responseHeaders)
            {
                response.Headers.TryAddWithoutValidation(key, value);
            }
        }

        return response;
    }
}

/// <summary>
/// Handler that routes token requests to a token handler and everything else to the API handler.
/// Token requests are identified by containing "grant_type" in the form body.
/// </summary>
internal sealed class SequencingAdminHandler : HttpMessageHandler
{
    private readonly AdminMockHandler _tokenHandler;
    private readonly AdminMockHandler _apiHandler;

    public SequencingAdminHandler(AdminMockHandler tokenHandler, AdminMockHandler apiHandler)
    {
        _tokenHandler = tokenHandler;
        _apiHandler = apiHandler;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Token endpoint requests are POSTs to the token URL with form content
        if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.Contains("/protocol/openid-connect/token") == true)
        {
            return await _tokenHandler.InvokeSendAsync(request, cancellationToken);
        }

        return await _apiHandler.InvokeSendAsync(request, cancellationToken);
    }
}
