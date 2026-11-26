using System.Net;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Keycloak;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundUp.Tests.Unit.Auth.Keycloak;

/// <summary>
/// Property and unit tests for <see cref="KeycloakRetryPolicy"/>, run with zero delay.
/// Phase 10b Properties 18 and 19 (Requirements 14.2, 14.3, 14.6).
/// </summary>
[Trait("Category", "Property")]
public sealed class PollyRetryPolicyPropertyTests
{
    private static readonly Polly.IAsyncPolicy<HttpResponseMessage> Policy =
        KeycloakRetryPolicy.Create(NullLogger.Instance, _ => TimeSpan.Zero);

    /// <summary>Calls the policy with a delegate that returns the given statuses in order (the last repeats).</summary>
    private static async Task<(HttpResponseMessage Response, int Calls)> ExecuteAsync(params HttpStatusCode[] statuses)
    {
        var calls = 0;
        var response = await Policy.ExecuteAsync(() =>
        {
            var status = statuses[Math.Min(calls, statuses.Length - 1)];
            calls++;
            return Task.FromResult(new HttpResponseMessage(status));
        });
        return (response, calls);
    }

    /// <summary>
    /// Property 18 and 19: any 5xx is retried <see cref="KeycloakRetryPolicy.RetryCount"/> times, then the
    /// last response is returned without throwing.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property ServerError_IsRetried_ThenLastResponseReturned()
    {
        return Prop.ForAll(Arb.From(Gen.Choose(500, 599)), code =>
        {
            var (response, calls) = ExecuteAsync((HttpStatusCode)code).GetAwaiter().GetResult();

            return (calls == KeycloakRetryPolicy.RetryCount + 1 && (int)response.StatusCode == code)
                .Label($"status {code}: {calls} calls");
        });
    }

    /// <summary>Property 18: a 4xx other than 408 is not retried.</summary>
    [Property(MaxTest = 50)]
    public Property ClientError_IsNotRetried()
    {
        var clientErrors = Gen.Choose(400, 499).Where(code => code != 408);
        return Prop.ForAll(Arb.From(clientErrors), code =>
        {
            var (response, calls) = ExecuteAsync((HttpStatusCode)code).GetAwaiter().GetResult();

            return (calls == 1 && (int)response.StatusCode == code).Label($"status {code}: {calls} calls");
        });
    }

    [Fact]
    public async Task RequestTimeout408_IsRetried()
    {
        var (_, calls) = await ExecuteAsync(HttpStatusCode.RequestTimeout);

        calls.Should().Be(KeycloakRetryPolicy.RetryCount + 1);
    }

    [Fact]
    public async Task SuccessAfterFailures_ReturnsSuccess()
    {
        var (response, calls) = await ExecuteAsync(
            HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadGateway, HttpStatusCode.OK);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        calls.Should().Be(3);
    }

    [Fact]
    public async Task Success_IsNotRetried()
    {
        var (_, calls) = await ExecuteAsync(HttpStatusCode.OK);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task NetworkException_IsRetried_ThenRethrown()
    {
        // The Keycloak services catch HttpRequestException; the policy itself rethrows after the last attempt
        var calls = 0;

        var act = () => Policy.ExecuteAsync(() =>
        {
            calls++;
            throw new HttpRequestException("connection refused");
        });

        await act.Should().ThrowAsync<HttpRequestException>();
        calls.Should().Be(KeycloakRetryPolicy.RetryCount + 1);
    }
}
