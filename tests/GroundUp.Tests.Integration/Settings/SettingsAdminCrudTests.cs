using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Tests.Integration.Settings;

/// <summary>
/// Admin CRUD tests via HTTP endpoints (POST/PUT/DELETE on levels, groups, definitions).
/// Uses <see cref="SettingsApiFactory"/> with Testcontainers Postgres.
/// </summary>
[Collection("SettingsApi")]
public sealed class SettingsAdminCrudTests : IAsyncLifetime
{
    private readonly SettingsApiFactory _factory;
    private HttpClient _client = null!;

    public SettingsAdminCrudTests(SettingsApiFactory factory)
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
        SettingsApiFactory.TestScopeChain.Clear();
        _client.Dispose();
        return Task.CompletedTask;
    }

    #region Level CRUD

    [Fact]
    public async Task CreateLevel_ReturnsCreatedLevel()
    {
        // Arrange
        var name = $"Level_{Guid.NewGuid():N}"[..20];
        var dto = new CreateSettingLevelDto(name, "Test level", null, 5);

        // Act
        var response = await _client.PostAsJsonAsync("/api/settings/levels", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OperationResult<SettingLevelDto>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.Name.Should().Be(name);
        result.Data.Description.Should().Be("Test level");
        result.Data.ParentId.Should().BeNull();
        result.Data.DisplayOrder.Should().Be(5);
        result.Data.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UpdateLevel_PersistsChanges()
    {
        // Arrange
        var name = $"Level_{Guid.NewGuid():N}"[..20];
        var createDto = new CreateSettingLevelDto(name, null, null, 0);
        var createResponse = await _client.PostAsJsonAsync("/api/settings/levels", createDto);
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content
            .ReadFromJsonAsync<OperationResult<SettingLevelDto>>())!.Data!;

        var updatedName = $"Updated_{Guid.NewGuid():N}"[..20];
        var updateDto = new UpdateSettingLevelDto(updatedName, "Updated desc", null, 10);

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/settings/levels/{created.Id}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OperationResult<SettingLevelDto>>();
        result!.Success.Should().BeTrue();
        result.Data!.Name.Should().Be(updatedName);
        result.Data.Description.Should().Be("Updated desc");
        result.Data.DisplayOrder.Should().Be(10);
    }

    [Fact]
    public async Task DeleteLevel_WithNoReferences_Succeeds()
    {
        // Arrange
        var name = $"Level_{Guid.NewGuid():N}"[..20];
        var createDto = new CreateSettingLevelDto(name, null, null, 0);
        var createResponse = await _client.PostAsJsonAsync("/api/settings/levels", createDto);
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content
            .ReadFromJsonAsync<OperationResult<SettingLevelDto>>())!.Data!;

        // Act
        var response = await _client.DeleteAsync($"/api/settings/levels/{created.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OperationResult<object>>();
        result!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllLevels_ReturnsLevels()
    {
        // Arrange — create a level to ensure at least one exists
        var name = $"Level_{Guid.NewGuid():N}"[..20];
        var createDto = new CreateSettingLevelDto(name, null, null, 0);
        await _client.PostAsJsonAsync("/api/settings/levels", createDto);

        // Act
        var response = await _client.GetAsync("/api/settings/levels");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content
            .ReadFromJsonAsync<OperationResult<List<SettingLevelDto>>>();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeEmpty();
    }

    #endregion

    #region Group CRUD

    [Fact]
    public async Task CreateGroup_ReturnsCreatedGroup()
    {
        // Arrange
        var key = $"Group_{Guid.NewGuid():N}"[..20];
        var dto = new CreateSettingGroupDto(key, "My Group", "A test group", "icon-cog", 1);

        // Act
        var response = await _client.PostAsJsonAsync("/api/settings/groups", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OperationResult<SettingGroupDto>>();
        result!.Success.Should().BeTrue();
        result.Data!.Key.Should().Be(key);
        result.Data.DisplayName.Should().Be("My Group");
        result.Data.Description.Should().Be("A test group");
        result.Data.Icon.Should().Be("icon-cog");
        result.Data.DisplayOrder.Should().Be(1);
        result.Data.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DeleteGroup_Succeeds()
    {
        // Arrange
        var key = $"Group_{Guid.NewGuid():N}"[..20];
        var createDto = new CreateSettingGroupDto(key, "Delete Me", null, null, 0);
        var createResponse = await _client.PostAsJsonAsync("/api/settings/groups", createDto);
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content
            .ReadFromJsonAsync<OperationResult<SettingGroupDto>>())!.Data!;

        // Act
        var response = await _client.DeleteAsync($"/api/settings/groups/{created.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Definition CRUD

    [Fact]
    public async Task CreateDefinition_WithOptionsAndAllowedLevels_ReturnsFullDefinition()
    {
        // Arrange
        var level = await CreateLevelAsync("System");
        var key = $"Def_{Guid.NewGuid():N}"[..20];

        var options = new List<CreateSettingOptionDto>
        {
            new("light", "Light Theme", 0, true, null),
            new("dark", "Dark Theme", 1, false, null),
            new("auto", "Auto", 2, false, null)
        };

        var dto = new CreateSettingDefinitionDto(
            Key: key,
            DataType: SettingDataType.String,
            DefaultValue: "light",
            GroupId: null,
            DisplayName: "Theme Setting",
            Description: "Choose a theme",
            Placeholder: null,
            Category: "Appearance",
            DisplayOrder: 0,
            IsVisible: true,
            IsReadOnly: false,
            AllowMultiple: false,
            IsEncrypted: false,
            IsSecret: false,
            IsRequired: false,
            MinValue: null,
            MaxValue: null,
            MinLength: null,
            MaxLength: null,
            RegexPattern: null,
            ValidationMessage: null,
            DependsOnKey: null,
            DependsOnOperator: null,
            DependsOnValue: null,
            CustomValidatorType: null,
            Options: options,
            AllowedLevelIds: new List<Guid> { level.Id });

        // Act
        var response = await _client.PostAsJsonAsync("/api/settings/definitions", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content
            .ReadFromJsonAsync<OperationResult<SettingDefinitionDto>>();
        result!.Success.Should().BeTrue();
        result.Data!.Key.Should().Be(key);
        result.Data.DataType.Should().Be(SettingDataType.String);
        result.Data.DefaultValue.Should().Be("light");
        result.Data.DisplayName.Should().Be("Theme Setting");
        result.Data.Category.Should().Be("Appearance");
        result.Data.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SetValue_ThenResolve_ReturnsSetValue()
    {
        // Arrange
        var level = await CreateLevelAsync("System");
        var key = $"Def_{Guid.NewGuid():N}"[..20];

        await CreateSimpleDefinitionAsync(key, SettingDataType.String, "default", level.Id);

        // Configure scope chain to include the system level
        SettingsApiFactory.TestScopeChain.Clear();
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(level.Id, null));

        // Act — set a value
        var setDto = new SetSettingValueDto("custom-value", level.Id, null);
        var setResponse = await _client.PutAsJsonAsync($"/api/settings/{key}", setDto);
        setResponse.EnsureSuccessStatusCode();

        // Act — resolve the value
        var getResponse = await _client.GetAsync($"/api/settings/{key}");
        getResponse.EnsureSuccessStatusCode();
        var resolved = await getResponse.Content.ReadFromJsonAsync<OperationResult<string>>();

        // Assert
        resolved!.Success.Should().BeTrue();
        resolved.Data.Should().Be("custom-value");
    }

    [Fact]
    public async Task DeleteDefinition_RemovesDefinition()
    {
        // Arrange
        var level = await CreateLevelAsync("System");
        var key = $"Def_{Guid.NewGuid():N}"[..20];
        var definition = await CreateSimpleDefinitionAsync(key, SettingDataType.String, "x", level.Id);

        // Act
        var response = await _client.DeleteAsync($"/api/settings/definitions/{definition.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify it's gone — resolving should return NotFound
        var getResponse = await _client.GetAsync($"/api/settings/{key}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAllDefinitions_ReturnsDefinitions()
    {
        // Arrange — create a definition to ensure at least one exists
        var level = await CreateLevelAsync("System");
        var key = $"Def_{Guid.NewGuid():N}"[..20];
        await CreateSimpleDefinitionAsync(key, SettingDataType.String, "val", level.Id);

        // Act
        var response = await _client.GetAsync("/api/settings/definitions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content
            .ReadFromJsonAsync<OperationResult<List<SettingDefinitionDto>>>();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeEmpty();
        result.Data!.Should().Contain(d => d.Key == key);
    }

    #endregion

    #region Helper Methods

    private async Task<SettingLevelDto> CreateLevelAsync(string name)
    {
        var uniqueName = $"{name}_{Guid.NewGuid():N}"[..20];
        var dto = new CreateSettingLevelDto(uniqueName, null, null, 0);
        var response = await _client.PostAsJsonAsync("/api/settings/levels", dto);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OperationResult<SettingLevelDto>>();
        result!.Success.Should().BeTrue();
        return result.Data!;
    }

    private async Task<SettingDefinitionDto> CreateSimpleDefinitionAsync(
        string key, SettingDataType dataType, string? defaultValue, Guid allowedLevelId)
    {
        var dto = new CreateSettingDefinitionDto(
            Key: key,
            DataType: dataType,
            DefaultValue: defaultValue,
            GroupId: null,
            DisplayName: key,
            Description: null,
            Placeholder: null,
            Category: null,
            DisplayOrder: 0,
            IsVisible: true,
            IsReadOnly: false,
            AllowMultiple: false,
            IsEncrypted: false,
            IsSecret: false,
            IsRequired: false,
            MinValue: null,
            MaxValue: null,
            MinLength: null,
            MaxLength: null,
            RegexPattern: null,
            ValidationMessage: null,
            DependsOnKey: null,
            DependsOnOperator: null,
            DependsOnValue: null,
            CustomValidatorType: null,
            Options: null,
            AllowedLevelIds: new List<Guid> { allowedLevelId });

        var response = await _client.PostAsJsonAsync("/api/settings/definitions", dto);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OperationResult<SettingDefinitionDto>>();
        result!.Success.Should().BeTrue();
        return result.Data!;
    }

    #endregion
}
