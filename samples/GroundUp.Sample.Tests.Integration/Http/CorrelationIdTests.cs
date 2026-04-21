using System.Net;
using FluentAssertions;
using GroundUp.Sample.Tests.Integration.Fixtures;
using GroundUp.Tests.Common.Fixtures;

namespace GroundUp.Sample.Tests.Integration.Http;

/// <summary>
/// Integration tests for the Correlation ID middleware.
/// Verifies that X-Correlation-Id is echoed when provided and generated when absent.
/// </summary>
[Collection("Api")]
public sealed class CorrelationIdTests : IntegrationTestBase
{
    private const string Endpoint = "/api/todoitems";

    public CorrelationIdTests(SampleApiFactory factory) : base(factory.CreateClient())
    {
    }

    [Fact]
    public async Task Request_WithCorrelationId_EchoesCorrelationIdInResponse()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Add("X-Correlation-Id", correlationId);

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Correlation-Id", out var values).Should().BeTrue();
        values!.First().Should().Be(correlationId);
    }

    [Fact]
    public async Task Request_WithoutCorrelationId_GeneratesCorrelationIdInResponse()
    {
        // Act
        var response = await Client.GetAsync(Endpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Correlation-Id", out var values).Should().BeTrue();
        values!.First().Should().NotBeNullOrEmpty();
    }
}
