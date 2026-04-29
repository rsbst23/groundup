using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Tests.Integration.Fixtures;
using GroundUp.Tests.Common.Fixtures;

namespace GroundUp.Sample.Tests.Integration.Http;

/// <summary>
/// Integration tests proving tenant isolation for the Project entity.
/// Each test uses unique tenant GUIDs and GUID-suffixed names for full isolation.
/// Validates that cross-tenant reads, updates, and deletes are blocked.
/// </summary>
[Collection("Api")]
public sealed class TenantIsolationTests : IntegrationTestBase
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private const string Endpoint = "/api/projects";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public TenantIsolationTests(SampleApiFactory factory) : base(factory.CreateClient())
    {
    }

    /// <summary>
    /// Sends an HTTP request with the X-Tenant-Id header set.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Guid tenantId, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        if (content != null) request.Content = content;
        return await Client.SendAsync(request);
    }

    /// <summary>
    /// Creates a Project as the specified tenant and returns the response.
    /// </summary>
    private async Task<(HttpResponseMessage Response, ProjectDto? Project)> CreateProjectAsync(
        Guid tenantId, string name, string? description = null)
    {
        var dto = new ProjectDto { Name = name, Description = description ?? "Test project" };
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await SendAsync(HttpMethod.Post, Endpoint, tenantId, content);
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OperationResult<ProjectDto>>(body, JsonOptions);
            return (response, result?.Data);
        }

        return (response, null);
    }

    [Fact]
    public async Task SameTenant_Create_ThenGetAll_ReturnsProject()
    {
        // Arrange
        var uniqueName = $"Proj_{Guid.NewGuid():N}";

        // Act — create as TenantA
        var (createResponse, created) = await CreateProjectAsync(TenantA, uniqueName);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Should().NotBeNull();

        // Act — GetAll as TenantA filtered by unique name
        var getResponse = await SendAsync(HttpMethod.Get, $"{Endpoint}?Filters[Name]={uniqueName}", TenantA);

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OperationResult<PaginatedData<ProjectDto>>>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Items.Should().ContainSingle(p => p.Name == uniqueName);
    }

    [Fact]
    public async Task CrossTenant_GetAll_ReturnsEmpty()
    {
        // Arrange — create as TenantA
        var uniqueName = $"Proj_{Guid.NewGuid():N}";
        var (createResponse, _) = await CreateProjectAsync(TenantA, uniqueName);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act — GetAll as TenantB filtered by the same unique name
        var getResponse = await SendAsync(HttpMethod.Get, $"{Endpoint}?Filters[Name]={uniqueName}", TenantB);

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OperationResult<PaginatedData<ProjectDto>>>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CrossTenant_GetById_Returns404()
    {
        // Arrange — create as TenantA
        var uniqueName = $"Proj_{Guid.NewGuid():N}";
        var (createResponse, created) = await CreateProjectAsync(TenantA, uniqueName);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Should().NotBeNull();

        // Act — GetById as TenantB
        var getResponse = await SendAsync(HttpMethod.Get, $"{Endpoint}/{created!.Id}", TenantB);

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CrossTenant_Update_Returns404_DataUnchanged()
    {
        // Arrange — create as TenantA with known name
        var originalName = $"Proj_{Guid.NewGuid():N}";
        var (createResponse, created) = await CreateProjectAsync(TenantA, originalName);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Should().NotBeNull();

        // Act — attempt PUT as TenantB with modified name
        var modifiedDto = new ProjectDto { Id = created!.Id, Name = "Modified_By_TenantB", Description = "Hacked" };
        var json = JsonSerializer.Serialize(modifiedDto, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var updateResponse = await SendAsync(HttpMethod.Put, $"{Endpoint}/{created.Id}", TenantB, content);

        // Assert — TenantB gets 404
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Assert — TenantA still sees original data
        var getResponse = await SendAsync(HttpMethod.Get, $"{Endpoint}/{created.Id}", TenantA);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OperationResult<ProjectDto>>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Name.Should().Be(originalName);
    }

    [Fact]
    public async Task CrossTenant_Delete_Returns404_DataStillExists()
    {
        // Arrange — create as TenantA
        var uniqueName = $"Proj_{Guid.NewGuid():N}";
        var (createResponse, created) = await CreateProjectAsync(TenantA, uniqueName);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Should().NotBeNull();

        // Act — attempt DELETE as TenantB
        var deleteResponse = await SendAsync(HttpMethod.Delete, $"{Endpoint}/{created!.Id}", TenantB);

        // Assert — TenantB gets 404
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Assert — TenantA still sees the project
        var getResponse = await SendAsync(HttpMethod.Get, $"{Endpoint}/{created.Id}", TenantA);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MultiTenant_GetAll_ReturnsOnlyOwnData()
    {
        // Arrange — unique prefix per test run
        var runId = Guid.NewGuid().ToString("N");

        // Create 2 projects as TenantA
        var (createA1, _) = await CreateProjectAsync(TenantA, $"ProjA_{runId}_1");
        createA1.StatusCode.Should().Be(HttpStatusCode.Created);
        var (createA2, _) = await CreateProjectAsync(TenantA, $"ProjA_{runId}_2");
        createA2.StatusCode.Should().Be(HttpStatusCode.Created);

        // Create 2 projects as TenantB
        var (createB1, _) = await CreateProjectAsync(TenantB, $"ProjB_{runId}_1");
        createB1.StatusCode.Should().Be(HttpStatusCode.Created);
        var (createB2, _) = await CreateProjectAsync(TenantB, $"ProjB_{runId}_2");
        createB2.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act — GetAll as TenantA filtered by TenantA's prefix
        var getResponse = await SendAsync(
            HttpMethod.Get,
            $"{Endpoint}?ContainsFilters[Name]=ProjA_{runId}",
            TenantA);

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OperationResult<PaginatedData<ProjectDto>>>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items.Should().AllSatisfy(p => p.Name.Should().StartWith($"ProjA_{runId}"));
        result.Data.Items.Should().NotContain(p => p.Name.StartsWith($"ProjB_{runId}"));
    }
}
