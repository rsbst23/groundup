using System.Net;
using FluentAssertions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Tests.Integration.Fixtures;
using GroundUp.Tests.Common.Fixtures;

namespace GroundUp.Sample.Tests.Integration.Http;

/// <summary>
/// Integration tests for TodoItem filtering via query parameters.
/// Each test creates its own data with GUID-suffixed titles for isolation.
/// </summary>
[Collection("Api")]
public sealed class TodoItemFilterTests : IntegrationTestBase
{
    private const string Endpoint = "/api/todoitems";

    public TodoItemFilterTests(SampleApiFactory factory) : base(factory.CreateClient())
    {
    }

    [Fact]
    public async Task GetAll_WithTitleFilter_ReturnsOnlyMatchingItems()
    {
        // Arrange — create 3 items with distinct unique titles
        var guid = Guid.NewGuid().ToString("N");
        var titleA = $"FilterA_{guid}";
        var titleB = $"FilterB_{guid}";
        var titleC = $"FilterC_{guid}";

        await Client.PostAsync(Endpoint, ToJsonContent(new TodoItemDto { Title = titleA }));
        await Client.PostAsync(Endpoint, ToJsonContent(new TodoItemDto { Title = titleB }));
        await Client.PostAsync(Endpoint, ToJsonContent(new TodoItemDto { Title = titleC }));

        // Act — filter by exact title match for titleA
        var response = await Client.GetAsync($"{Endpoint}?Filters[Title]={titleA}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResultAsync<PaginatedData<TodoItemDto>>(response);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Items.Should().ContainSingle();
        result.Data.Items[0].Title.Should().Be(titleA);
    }

    [Fact]
    public async Task GetAll_WithNonMatchingTitleFilter_ReturnsEmptyList()
    {
        // Arrange — use a title that doesn't exist
        var nonExistentTitle = $"NonExistent_{Guid.NewGuid():N}";

        // Act
        var response = await Client.GetAsync($"{Endpoint}?Filters[Title]={nonExistentTitle}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResultAsync<PaginatedData<TodoItemDto>>(response);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Items.Should().BeEmpty();
    }
}
