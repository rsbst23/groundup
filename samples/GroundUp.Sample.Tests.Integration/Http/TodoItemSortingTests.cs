using System.Net;
using FluentAssertions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Tests.Integration.Fixtures;
using GroundUp.Tests.Common.Fixtures;

namespace GroundUp.Sample.Tests.Integration.Http;

/// <summary>
/// Integration tests for TodoItem sorting via query parameters.
/// Creates items with distinct titles sharing a unique prefix for isolation.
/// </summary>
[Collection("Api")]
public sealed class TodoItemSortingTests : IntegrationTestBase
{
    private const string Endpoint = "/api/todoitems";

    public TodoItemSortingTests(SampleApiFactory factory) : base(factory.CreateClient())
    {
    }

    [Fact]
    public async Task GetAll_WithSortByTitle_ReturnsItemsInAscendingOrder()
    {
        // Arrange — create 3 items with titles that sort as A < B < C
        var guid = Guid.NewGuid().ToString("N");
        var prefix = $"Sort_{guid}";

        await Client.PostAsync(Endpoint, ToJsonContent(new TodoItemDto { Title = $"{prefix}_C" }));
        await Client.PostAsync(Endpoint, ToJsonContent(new TodoItemDto { Title = $"{prefix}_A" }));
        await Client.PostAsync(Endpoint, ToJsonContent(new TodoItemDto { Title = $"{prefix}_B" }));

        // Act — sort ascending by Title, filtered by the unique prefix
        var response = await Client.GetAsync(
            $"{Endpoint}?SortBy=Title&ContainsFilters[Title]={prefix}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResultAsync<PaginatedData<TodoItemDto>>(response);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Items.Should().HaveCount(3);

        var titles = result.Data.Items.Select(i => i.Title).ToList();
        titles.Should().ContainInOrder($"{prefix}_A", $"{prefix}_B", $"{prefix}_C");
    }

    [Fact]
    public async Task GetAll_WithSortByTitleDesc_ReturnsItemsInDescendingOrder()
    {
        // Arrange — create 3 items with titles that sort as A < B < C
        var guid = Guid.NewGuid().ToString("N");
        var prefix = $"Sort_{guid}";

        await Client.PostAsync(Endpoint, ToJsonContent(new TodoItemDto { Title = $"{prefix}_C" }));
        await Client.PostAsync(Endpoint, ToJsonContent(new TodoItemDto { Title = $"{prefix}_A" }));
        await Client.PostAsync(Endpoint, ToJsonContent(new TodoItemDto { Title = $"{prefix}_B" }));

        // Act — sort descending by Title, filtered by the unique prefix
        var response = await Client.GetAsync(
            $"{Endpoint}?SortBy=Title%20desc&ContainsFilters[Title]={prefix}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResultAsync<PaginatedData<TodoItemDto>>(response);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Items.Should().HaveCount(3);

        var titles = result.Data.Items.Select(i => i.Title).ToList();
        titles.Should().ContainInOrder($"{prefix}_C", $"{prefix}_B", $"{prefix}_A");
    }
}
