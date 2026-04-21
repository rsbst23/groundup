using System.Net;
using FluentAssertions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Tests.Integration.Fixtures;
using GroundUp.Tests.Common.Fixtures;

namespace GroundUp.Sample.Tests.Integration.Http;

/// <summary>
/// Integration tests for TodoItem CRUD operations via the full HTTP stack.
/// Each test creates its own data with GUID-suffixed titles for isolation.
/// </summary>
[Collection("Api")]
public sealed class TodoItemCrudTests : IntegrationTestBase
{
    private const string Endpoint = "/api/todoitems";

    public TodoItemCrudTests(SampleApiFactory factory) : base(factory.CreateClient())
    {
    }

    [Fact]
    public async Task Create_ValidTodoItem_Returns201WithGuidAndLocation()
    {
        // Arrange
        var dto = new TodoItemDto
        {
            Title = $"Todo_{Guid.NewGuid():N}",
            Description = "Integration test item"
        };

        // Act
        var response = await Client.PostAsync(Endpoint, ToJsonContent(dto));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await ReadResultAsync<TodoItemDto>(response);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().NotBe(Guid.Empty);

        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetById_ExistingItem_Returns200WithMatchingData()
    {
        // Arrange — create an item first
        var title = $"Todo_{Guid.NewGuid():N}";
        var description = "Get by ID test";
        var createDto = new TodoItemDto { Title = title, Description = description };
        var createResponse = await Client.PostAsync(Endpoint, ToJsonContent(createDto));
        var createResult = await ReadResultAsync<TodoItemDto>(createResponse);
        var id = createResult!.Data!.Id;

        // Act
        var response = await Client.GetAsync($"{Endpoint}/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResultAsync<TodoItemDto>(response);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(id);
        result.Data.Title.Should().Be(title);
        result.Data.Description.Should().Be(description);
    }

    [Fact]
    public async Task Update_ExistingItem_Returns200WithUpdatedValues()
    {
        // Arrange — create an item
        var originalTitle = $"Todo_{Guid.NewGuid():N}";
        var createDto = new TodoItemDto { Title = originalTitle, Description = "Original" };
        var createResponse = await Client.PostAsync(Endpoint, ToJsonContent(createDto));
        var createResult = await ReadResultAsync<TodoItemDto>(createResponse);
        var id = createResult!.Data!.Id;

        // Act — update with a new title (Id must match the existing entity)
        var updatedTitle = $"Updated_{Guid.NewGuid():N}";
        var updateDto = new TodoItemDto { Id = id, Title = updatedTitle, Description = "Updated" };
        var updateResponse = await Client.PutAsync($"{Endpoint}/{id}", ToJsonContent(updateDto));

        // Assert — update response
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateResult = await ReadResultAsync<TodoItemDto>(updateResponse);
        updateResult.Should().NotBeNull();
        updateResult!.Data.Should().NotBeNull();
        updateResult.Data!.Title.Should().Be(updatedTitle);

        // Assert — GET confirms persistence
        var getResponse = await Client.GetAsync($"{Endpoint}/{id}");
        var getResult = await ReadResultAsync<TodoItemDto>(getResponse);
        getResult!.Data!.Title.Should().Be(updatedTitle);
        getResult.Data.Description.Should().Be("Updated");
    }

    [Fact]
    public async Task Delete_ExistingItem_Returns200AndSubsequentGetReturns404()
    {
        // Arrange — create an item
        var createDto = new TodoItemDto { Title = $"Todo_{Guid.NewGuid():N}" };
        var createResponse = await Client.PostAsync(Endpoint, ToJsonContent(createDto));
        var createResult = await ReadResultAsync<TodoItemDto>(createResponse);
        var id = createResult!.Data!.Id;

        // Act
        var deleteResponse = await Client.DeleteAsync($"{Endpoint}/{id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await Client.GetAsync($"{Endpoint}/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAll_AfterCreatingItem_ReturnsItemInPaginatedResults()
    {
        // Arrange — create an item with a unique title
        var uniqueTitle = $"Todo_{Guid.NewGuid():N}";
        var createDto = new TodoItemDto { Title = uniqueTitle, Description = "Paginated test" };
        await Client.PostAsync(Endpoint, ToJsonContent(createDto));

        // Act — GET all filtered by the unique title
        var response = await Client.GetAsync($"{Endpoint}?Filters[Title]={uniqueTitle}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResultAsync<PaginatedData<TodoItemDto>>(response);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Items.Should().ContainSingle(i => i.Title == uniqueTitle);

        // Pagination headers
        response.Headers.GetValues("X-Total-Count").Should().NotBeEmpty();
        response.Headers.GetValues("X-Page-Number").Should().NotBeEmpty();
        response.Headers.GetValues("X-Page-Size").Should().NotBeEmpty();
        response.Headers.GetValues("X-Total-Pages").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Delete_ExistingItem_NotReturnedInGetAll()
    {
        // Arrange — create an item with a unique title, then delete it
        var uniqueTitle = $"Todo_{Guid.NewGuid():N}";
        var createDto = new TodoItemDto { Title = uniqueTitle };
        var createResponse = await Client.PostAsync(Endpoint, ToJsonContent(createDto));
        var createResult = await ReadResultAsync<TodoItemDto>(createResponse);
        var id = createResult!.Data!.Id;

        await Client.DeleteAsync($"{Endpoint}/{id}");

        // Act — GET all filtered by the unique title
        var response = await Client.GetAsync($"{Endpoint}?Filters[Title]={uniqueTitle}");

        // Assert — deleted item should not appear
        var result = await ReadResultAsync<PaginatedData<TodoItemDto>>(response);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Items.Should().BeEmpty();
    }
}
