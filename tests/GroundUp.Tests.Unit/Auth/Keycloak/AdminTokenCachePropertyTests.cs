using System.Net;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using GroundUp.Auth.Keycloak;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Keycloak;

/// <summary>
/// Property-based tests for <see cref="AdminTokenCache"/>.
/// Feature: phase-10b-keycloak-provider
/// Property 10: Admin Token Caching With Expiry-Based Refresh
/// Property 11: Admin Token Failure Does Not Expose Secrets
/// Validates: Requirements 8.2, 8.3, 8.4
/// </summary>
[Trait("Category", "Property")]
public sealed class AdminTokenCachePropertyTests
{
    private static readonly KeycloakOptions ValidOptions = new()
    {
        InternalBaseUrl = "http://keycloak:8080",
        SharedRealmName = "groundup",
        AdminClientId = "admin-cli",
        AdminClientSecret = "super-secret-password-123",
        PublicBaseUrl = "https://keycloak.example.com",
        AppClientId = "groundup-app"
    };

    private static (AdminTokenCache cache, MockHttpMessageHandler handler) CreateCacheWithHandler(
        int expiresIn = 300, HttpStatusCode statusCode = HttpStatusCode.OK, string? accessToken = "test-token")
    {
        var handler = new MockHttpMessageHandler(statusCode, expiresIn, accessToken);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://keycloak:8080") };

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("KeycloakAdmin").Returns(httpClient);

        var optionsMonitor = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        optionsMonitor.CurrentValue.Returns(ValidOptions);

        var logger = NullLoggerFactory.Instance.CreateLogger<AdminTokenCache>();

        var cache = new AdminTokenCache(httpClientFactory, optionsMonitor, logger);
        return (cache, handler);
    }

    /// <summary>
    /// Property 10: For any valid expires_in value (greater than safety margin of 30s),
    /// the cached token is returned on subsequent calls without re-fetching.
    /// **Validates: Requirement 8.2**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(AdminTokenCacheArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 10: Cached token returned without refetch")]
    public Property GetTokenAsync_ValidToken_ReturnsCachedWithoutRefetch(PositiveInt expiresInWrapper)
    {
        var expiresIn = expiresInWrapper.Get + 60; // Always above safety margin
        var (cache, handler) = CreateCacheWithHandler(expiresIn);

        using (cache)
        {
            // First call acquires token
            var token1 = cache.GetTokenAsync(CancellationToken.None).GetAwaiter().GetResult();
            // Second call should use cache
            var token2 = cache.GetTokenAsync(CancellationToken.None).GetAwaiter().GetResult();

            return (token1 == token2 && handler.CallCount == 1).ToProperty();
        }
    }

    /// <summary>
    /// Property 10: When the token acquisition fails (non-success HTTP), returns null.
    /// **Validates: Requirement 8.3**
    /// </summary>
    [Property(MaxTest = 20, Arbitrary = new[] { typeof(AdminTokenCacheArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 10: Failed acquisition returns null")]
    public Property GetTokenAsync_FailedAcquisition_ReturnsNull(FailureStatusCode statusCodeWrapper)
    {
        var (cache, _) = CreateCacheWithHandler(statusCode: statusCodeWrapper.Code);

        using (cache)
        {
            var token = cache.GetTokenAsync(CancellationToken.None).GetAwaiter().GetResult();
            return (token == null).ToProperty();
        }
    }

    /// <summary>
    /// Property 11: When token acquisition fails, the error message (exception/log) never contains
    /// the AdminClientSecret value.
    /// **Validates: Requirement 8.4**
    /// </summary>
    [Property(MaxTest = 20, Arbitrary = new[] { typeof(AdminTokenCacheArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 11: Failure does not expose secrets")]
    public Property GetTokenAsync_Failure_DoesNotExposeSecretInLogs(FailureStatusCode statusCodeWrapper)
    {
        var logMessages = new List<string>();
        var loggerFactory = new CapturingLoggerFactory(logMessages);
        var logger = loggerFactory.CreateLogger<AdminTokenCache>();

        var handler = new MockHttpMessageHandler(statusCodeWrapper.Code, 300, null);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://keycloak:8080") };

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("KeycloakAdmin").Returns(httpClient);

        var optionsMonitor = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        optionsMonitor.CurrentValue.Returns(ValidOptions);

        using var cache = new AdminTokenCache(httpClientFactory, optionsMonitor, logger);

        var token = cache.GetTokenAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Secret should never appear in any log message
        var secretExposed = logMessages.Any(msg =>
            msg.Contains(ValidOptions.AdminClientSecret, StringComparison.OrdinalIgnoreCase));

        return (!secretExposed).ToProperty();
    }

    /// <summary>
    /// Verifies that options change invalidates the cached token.
    /// **Validates: Requirement 8.5**
    /// </summary>
    [Fact]
    public async Task GetTokenAsync_AfterOptionsChange_RefetchesToken()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, 300, "initial-token");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://keycloak:8080") };

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("KeycloakAdmin").Returns(httpClient);

        Action<KeycloakOptions, string>? changeCallback = null;
        var optionsMonitor = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        optionsMonitor.CurrentValue.Returns(ValidOptions);
        optionsMonitor.OnChange(Arg.Do<Action<KeycloakOptions, string>>(cb => changeCallback = cb))
            .Returns(Substitute.For<IDisposable>());

        var logger = NullLoggerFactory.Instance.CreateLogger<AdminTokenCache>();

        using var cache = new AdminTokenCache(httpClientFactory, optionsMonitor, logger);

        // First call
        var token1 = await cache.GetTokenAsync(CancellationToken.None);
        handler.CallCount.Should().Be(1);

        // Simulate options change
        handler.NextToken = "refreshed-token";
        changeCallback?.Invoke(ValidOptions, "");

        // Second call should re-fetch
        var token2 = await cache.GetTokenAsync(CancellationToken.None);
        handler.CallCount.Should().Be(2);
        token2.Should().Be("refreshed-token");
    }
}

/// <summary>
/// Wrapper type for non-success HTTP status codes.
/// </summary>
public sealed class FailureStatusCode
{
    public HttpStatusCode Code { get; }

    public FailureStatusCode(HttpStatusCode code) => Code = code;
    public override string ToString() => $"Status={Code}";
}

/// <summary>
/// FsCheck Arbitrary generators for AdminTokenCache tests.
/// </summary>
public static class AdminTokenCacheArbitraries
{
    public static Arbitrary<FailureStatusCode> FailureStatusCodeArb()
    {
        var gen = Gen.Elements(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.GatewayTimeout)
            .Select(code => new FailureStatusCode(code));

        return Arb.From(gen);
    }
}

/// <summary>
/// Mock HTTP handler for testing AdminTokenCache without real HTTP calls.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly int _expiresIn;
    private string? _accessToken;
    public int CallCount { get; private set; }
    public string? NextToken { get; set; }

    public MockHttpMessageHandler(HttpStatusCode statusCode, int expiresIn, string? accessToken)
    {
        _statusCode = statusCode;
        _expiresIn = expiresIn;
        _accessToken = accessToken;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;

        var token = NextToken ?? _accessToken;
        if (NextToken is not null)
        {
            _accessToken = NextToken;
            NextToken = null;
        }

        if (_statusCode != HttpStatusCode.OK)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("error")
            });
        }

        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = token,
            expires_in = _expiresIn,
            token_type = "Bearer"
        });

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        });
    }
}

/// <summary>
/// Logger factory that captures log messages for assertion.
/// </summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly List<string> _messages;

    public CapturingLoggerFactory(List<string> messages) => _messages = messages;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }

    public ILogger<T> CreateLogger<T>() => new CapturingLogger<T>(_messages);
}

internal sealed class CapturingLogger<T> : CapturingLogger, ILogger<T>
{
    public CapturingLogger(List<string> messages) : base(messages) { }
}

internal class CapturingLogger : ILogger
{
    private readonly List<string> _messages;

    public CapturingLogger(List<string> messages) => _messages = messages;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _messages.Add(message);
        if (exception is not null)
        {
            _messages.Add(exception.ToString());
        }
    }
}
