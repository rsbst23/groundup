using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// Integration tests for setup endpoint rate limiting.
/// Verifies: exceed limit → 429 with Retry-After, loopback bypass in dev, disabled when complete.
/// </summary>
[Collection("SetupRateLimitApi")]
public sealed class SetupRateLimitTests : IAsyncLifetime
{
    private readonly SetupRateLimitApiFactory _factory;
    private HttpClient _client = null!;

    public SetupRateLimitTests(SetupRateLimitApiFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    #region Exceed Limit → 429 with Retry-After (Req 20.4)

    [Fact]
    public async Task ExceedRateLimit_Returns429WithRetryAfterAndRateLimitedErrorShape()
    {
        // Arrange — configure a very low limit so we can exceed it easily
        SetupRateLimitApiFactory.IsSetupComplete = false;
        SetupRateLimitApiFactory.RequestsPerWindow = 3;
        SetupRateLimitApiFactory.WindowSeconds = 60;
        SetupRateLimitApiFactory.BypassLoopback = false;

        // Act — send requests to the public /setup endpoint (no auth required)
        HttpResponseMessage? rateLimitedResponse = null;
        for (var i = 0; i < 5; i++)
        {
            var response = await _client.GetAsync("/setup");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = response;
                break;
            }
        }

        // Assert
        rateLimitedResponse.Should().NotBeNull("expected to hit rate limit after exceeding the window");
        rateLimitedResponse!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Verify Retry-After header is present
        rateLimitedResponse.Headers.RetryAfter.Should().NotBeNull();

        // Verify error response shape
        var body = await rateLimitedResponse.Content.ReadFromJsonAsync<RateLimitedResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("rate_limited");
        body.Message.Should().Contain("Too many setup requests");
    }

    [Fact]
    public async Task ExceedRateLimit_RetryAfterHeaderContainsPositiveSeconds()
    {
        // Arrange
        SetupRateLimitApiFactory.IsSetupComplete = false;
        SetupRateLimitApiFactory.RequestsPerWindow = 2;
        SetupRateLimitApiFactory.WindowSeconds = 60;
        SetupRateLimitApiFactory.BypassLoopback = false;

        // Act — exhaust the limit
        HttpResponseMessage? rateLimitedResponse = null;
        for (var i = 0; i < 4; i++)
        {
            var response = await _client.GetAsync("/setup");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = response;
                break;
            }
        }

        // Assert
        rateLimitedResponse.Should().NotBeNull("expected to hit rate limit after exceeding the window");
        var retryAfter = rateLimitedResponse!.Headers.RetryAfter;
        retryAfter.Should().NotBeNull();

        // Retry-After should be a positive number of seconds within the window
        retryAfter!.Delta.Should().NotBeNull();
        retryAfter.Delta!.Value.TotalSeconds.Should().BeGreaterThan(0)
            .And.BeLessThanOrEqualTo(60);
    }

    #endregion

    #region Loopback Bypass in Development (Req 20.7)

    [Fact]
    public async Task LoopbackAddress_InDevelopment_BypassesRateLimit()
    {
        // Arrange — enable loopback bypass (simulates Development environment)
        // Use X-Test-RemoteIp header to simulate a loopback address in the test host
        // (WebApplicationFactory doesn't set RemoteIpAddress on the connection)
        SetupRateLimitApiFactory.IsSetupComplete = false;
        SetupRateLimitApiFactory.RequestsPerWindow = 2;
        SetupRateLimitApiFactory.WindowSeconds = 60;
        SetupRateLimitApiFactory.BypassLoopback = true;

        // Act — send more requests than the limit allows with simulated loopback IP
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/setup");
            request.Headers.Add("X-Test-RemoteIp", "127.0.0.1");
            responses.Add(await _client.SendAsync(request));
        }

        // Assert — none should be 429 because loopback is bypassed in dev
        responses.Should().NotContain(r => r.StatusCode == HttpStatusCode.TooManyRequests,
            "loopback addresses should bypass rate limiting in Development environment");
    }

    #endregion

    #region Disabled When Complete (Req 20.6)

    [Fact]
    public async Task RateLimit_DisabledWhenSetupComplete()
    {
        // Arrange — mark setup as complete
        SetupRateLimitApiFactory.IsSetupComplete = true;
        SetupRateLimitApiFactory.RequestsPerWindow = 1;
        SetupRateLimitApiFactory.WindowSeconds = 60;
        SetupRateLimitApiFactory.BypassLoopback = false;

        // Act — send more requests than the limit would allow to /setup
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            responses.Add(await _client.GetAsync("/setup"));
        }

        // Assert — none should be 429 because rate limiting is disabled post-setup
        responses.Should().NotContain(r => r.StatusCode == HttpStatusCode.TooManyRequests,
            "rate limiting should be disabled once setup is complete");
    }

    #endregion

    /// <summary>DTO for deserializing the 429 JSON response body.</summary>
    private sealed record RateLimitedResponse(string Code, string Message);
}
