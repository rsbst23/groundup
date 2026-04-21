using System.Net;
using FluentAssertions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Tests.Integration.Fixtures;
using GroundUp.Tests.Common.Fixtures;

namespace GroundUp.Sample.Tests.Integration.Http;

/// <summary>
/// Integration tests for TodoItem pagination via query parameters.
/// Creates items with a shared unique prefix and uses ContainsFilters for isolation.
/// </summary>
[Collection("Api")]
public sealed class TodoItemPagingTests : IntegrationTestBase
{
    private const string Endpoint = "/api/todoitems";

    public TodoItemPagingTests(SampleApiFactory factory) : base(factory.CreateClient())
    {
    }

    [Fact]
    public async Task GetAll_WithPageSize2_ReturnsCorrectPageAndHeaders()
    {
        // Arrange — create 5 items with a shared unique prefix
        var guid = Guid.NewGuid().ToString("N");
        var prefix = $"Page_{guid}";

        for (var i = 1; i <= 5; i++)
        {
            await Client.PostAsync(Endpoint, ToJsonContent(new TodoItemDto
            {
                Title = $"{prefix}_{i}"
            }));
        }

        // Act — request page 1 with page size 2, filtered by the unique prefix
        var response = await Client.GetAsync(
            $"{Endpoint}?PageSize=2&PageNumber=1&ContainsFilters[Title]={prefix}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResultAsync<PaginatedData<TodoItemDto>>(response);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Items.Should().HaveCount(2);

        // Pagination headers
        response.Headers.GetValues("X-Total-Count").First().Should().Be("5");
        response.Headers.GetValues("X-Total-Pages").First().Should().Be("3");
        response.Headers.GetValues("X-Page-Number").First().Should().Be("1");
        response.Headers.GetValues("X-Page-Size").First().Should().Be("2");
    }
}
