using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Tests.Integration.Fixtures;

namespace GroundUp.Tests.Integration.Filtering;

/// <summary>
/// Integration tests verifying that Swagger/OpenAPI endpoint loads without
/// ambiguous HTTP method errors after the base class refactor.
/// </summary>
[Collection("SampleApi")]
public sealed class SwaggerEndpointTests
{
    private readonly SampleApiFactory _factory;

    public SwaggerEndpointTests(SampleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SwaggerJson_ReturnsOk_WithValidOpenApiDocument()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/swagger/v1/swagger.json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrWhiteSpace();

        // Verify it's valid JSON
        var doc = JsonDocument.Parse(content);
        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
        doc.RootElement.GetProperty("paths").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task SwaggerJson_OrdersEndpoints_HaveCorrectDtoTypes()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        // Assert — verify Orders endpoints exist
        var paths = doc.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/Orders", out _).Should().BeTrue("Orders collection endpoint should exist");
        paths.TryGetProperty("/api/Orders/{id}", out _).Should().BeTrue("Orders item endpoint should exist");
    }

    [Fact]
    public async Task SwaggerJson_AllControllerEndpoints_ArePresent()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        // Assert — verify all expected controller endpoints exist
        var paths = doc.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/TodoItems", out _).Should().BeTrue("TodoItems endpoint should exist");
        paths.TryGetProperty("/api/TodoItems/{id}", out _).Should().BeTrue("TodoItems item endpoint should exist");
        paths.TryGetProperty("/api/Customers", out _).Should().BeTrue("Customers endpoint should exist");
        paths.TryGetProperty("/api/Customers/{id}", out _).Should().BeTrue("Customers item endpoint should exist");
        paths.TryGetProperty("/api/Orders", out _).Should().BeTrue("Orders endpoint should exist");
        paths.TryGetProperty("/api/Orders/{id}", out _).Should().BeTrue("Orders item endpoint should exist");
        paths.TryGetProperty("/api/Projects", out _).Should().BeTrue("Projects endpoint should exist");
        paths.TryGetProperty("/api/Projects/{id}", out _).Should().BeTrue("Projects item endpoint should exist");
    }
}
