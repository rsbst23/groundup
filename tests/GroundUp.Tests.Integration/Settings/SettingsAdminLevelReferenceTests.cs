using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Results;

namespace GroundUp.Tests.Integration.Settings;

/// <summary>
/// Integration tests for setting level deletion when references exist.
/// Verifies that levels with setting values or child levels cannot be deleted.
/// Uses <see cref="SettingsApiFactory"/> with Testcontainers Postgres.
/// </summary>
[Collection("SettingsApi")]
public sealed class SettingsAdminLevelReferenceTests : IAsyncLifetime
{
    private readonly SettingsApiFactory _factory;
    private HttpClient _client = null!;

    public SettingsAdminLevelReferenceTests(SettingsApiFactory factory)
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

    [Fact]
    public async Task DeleteLevel_WithSettingValues_Returns400()
    {
        // Arrange — create a level
        var level = await CreateLevelAsync("ValueLevel");

        // Create a definition allowed at that level
        var defKey = $"Def_{Guid.NewGuid():N}"[..20];
        var defDto = new CreateSettingDefinitionDto(
            Key: defKey,
            DataType: SettingDataType.String,
            DefaultValue: "default",
            GroupId: null,
            DisplayName: defKey,
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
            AllowedLevelIds: new List<Guid> { level.Id });

        var defResponse = await _client.PostAsJsonAsync("/api/settings/definitions", defDto);
        defResponse.EnsureSuccessStatusCode();

        // Set a value at that level
        SettingsApiFactory.TestScopeChain.Clear();
        SettingsApiFactory.TestScopeChain.Add(new Core.Models.SettingScopeEntry(level.Id, null));

        var setDto = new SetSettingValueDto("some-value", level.Id, null);
        var setResponse = await _client.PutAsJsonAsync($"/api/settings/{defKey}", setDto);
        setResponse.EnsureSuccessStatusCode();

        // Act — try to delete the level
        var response = await _client.DeleteAsync($"/api/settings/levels/{level.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteLevel_WithChildLevels_Returns400()
    {
        // Arrange — create a parent level
        var parent = await CreateLevelAsync("Parent");

        // Create a child level with parentId pointing to the parent
        var childName = $"Child_{Guid.NewGuid():N}"[..20];
        var childDto = new CreateSettingLevelDto(childName, null, parent.Id, 0);
        var childResponse = await _client.PostAsJsonAsync("/api/settings/levels", childDto);
        childResponse.EnsureSuccessStatusCode();

        // Act — try to delete the parent
        var response = await _client.DeleteAsync($"/api/settings/levels/{parent.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

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

    #endregion
}
