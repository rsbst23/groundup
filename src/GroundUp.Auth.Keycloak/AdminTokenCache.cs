using System.Net.Http.Json;
using System.Text.Json.Serialization;
using GroundUp.Auth.Services.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Keycloak;

/// <summary>
/// Thread-safe cache for the Keycloak admin service account token.
/// Acquires tokens via client_credentials grant and caches them with a 30-second safety margin.
/// Invalidates the cached token when <see cref="KeycloakOptions"/> changes (e.g., credential rotation).
/// </summary>
internal sealed class AdminTokenCache : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<KeycloakOptions> _options;
    private readonly ILogger<AdminTokenCache> _logger;
    private readonly IDisposable? _optionsChangeSubscription;

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    private const int SafetyMarginSeconds = 30;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminTokenCache"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory for creating named clients.</param>
    /// <param name="options">The options monitor providing current Keycloak configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public AdminTokenCache(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<KeycloakOptions> options,
        ILogger<AdminTokenCache> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;

        _optionsChangeSubscription = options.OnChange(_ => InvalidateCache());
    }

    /// <summary>
    /// Returns a valid admin token, either from cache or by acquiring a new one via client_credentials grant.
    /// Returns null if token acquisition fails (caller handles the null).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A valid admin access token, or null if acquisition failed.</returns>
    public async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        // Fast path: check cache without acquiring semaphore
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _cachedToken;
        }

        await _semaphore.WaitAsync(ct);
        try
        {
            // Double-check after acquiring semaphore
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _cachedToken;
            }

            return await AcquireTokenAsync(ct);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Releases the semaphore and disposes the options change subscription.
    /// </summary>
    public void Dispose()
    {
        _optionsChangeSubscription?.Dispose();
        _semaphore.Dispose();
    }

    private async Task<string?> AcquireTokenAsync(CancellationToken ct)
    {
        var opts = _options.CurrentValue;
        var tokenUrl = $"{opts.InternalBaseUrl.TrimEnd('/')}/realms/{opts.SharedRealmName}/protocol/openid-connect/token";

        var client = _httpClientFactory.CreateClient("KeycloakAdmin");

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = opts.AdminClientId,
            ["client_secret"] = opts.AdminClientSecret
        });

        try
        {
            var response = await client.PostAsync(tokenUrl, formData, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Failed to acquire Keycloak admin token. Status: {StatusCode}",
                    (int)response.StatusCode);
                _cachedToken = null;
                _expiresAt = DateTimeOffset.MinValue;
                return null;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<AdminTokenResponse>(ct);

            if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                _logger.LogError("Keycloak admin token response was empty or malformed.");
                _cachedToken = null;
                _expiresAt = DateTimeOffset.MinValue;
                return null;
            }

            _cachedToken = tokenResponse.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - SafetyMarginSeconds);

            return _cachedToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Exception while acquiring Keycloak admin token.");
            _cachedToken = null;
            _expiresAt = DateTimeOffset.MinValue;
            return null;
        }
    }

    private void InvalidateCache()
    {
        _cachedToken = null;
        _expiresAt = DateTimeOffset.MinValue;
        _logger.LogInformation("Admin token cache invalidated due to options change.");
    }

    /// <summary>
    /// Internal token response model for the client_credentials grant.
    /// </summary>
    private sealed record AdminTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
