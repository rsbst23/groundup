using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Results;

namespace GroundUp.Tests.Integration.Settings;

/// <summary>
/// Integration tests for setting group update operations via the HTTP API.
/// Uses <see cref="SettingsApiFactory"/> with Testcontainers Postgres.
/// </summary>
[Collection("SettingsApi")]
public sealed class SettingsAdminGroupUpdateTests : IAsyncLifetime
{
    private readonly SettingsApiFactory _factory;
    private HttpClient _client = null!;

    public SettingsAdminGroupUpdateTests(SettingsApiFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UpdateGroup_PersistsChanges()
    {
        // Arrange — create a group
        var key = $"Group_{Guid.NewGuid():N}"[..20];
        var createDto = new CreateSettingGroupDto(key, "Original Name", "Original desc", "icon-old", 1);
        var createResponse = await _client.PostAsJsonAsync("/api/settings/groups", createDto);
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content
            .ReadFromJsonAsync<OperationResult<SettingGroupDto>>())!.Data!;

        // Act — update the group
        var updateDto = new UpdateSettingGroupDto("updated-key", "Updated Name", "Updated desc", "icon-new", 99);
        var response = await _client.PutAsJsonAsync(
            $"/api/settings/groups/{created.Id}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OperationResult<SettingGroupDto>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.Key.Should().Be("updated-key");
        result.Data.DisplayName.Should().Be("Updated Name");
        result.Data.Description.Should().Be("Updated desc");
        result.Data.Icon.Should().Be("icon-new");
        result.Data.DisplayOrder.Should().Be(99);
    }

    [Fact]
    public async Task UpdateGroup_NonExistentId_Returns404()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var updateDto = new UpdateSettingGroupDto("key", "Name", null, null, 0);

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/settings/groups/{nonExistentId}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
