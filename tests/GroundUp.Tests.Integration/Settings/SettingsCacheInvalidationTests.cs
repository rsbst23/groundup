using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Tests.Integration.Settings;

/// <summary>
/// Cache invalidation tests: set→read→update→read verifies new value;
/// delete→read verifies fallback. Uses <see cref="SettingsApiFactory"/>
/// with Testcontainers Postgres.
/// </summary>
[Collection("SettingsApi")]
public sealed class SettingsCacheInvalidationTests : IAsyncLifetime
{
    private readonly SettingsApiFactory _factory;
    private HttpClient _client = null!;

    public SettingsCacheInvalidationTests(SettingsApiFactory factory)
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
    public async Task SetReadUpdateRead_ReturnsUpdatedValue()
    {
        // Arrange
        var level = await CreateLevelAsync("System");
        var key = UniqueKey("CacheUpd");

        await CreateDefinitionAsync(key, SettingDataType.String, "default",
            new[] { level.Id });

        // Configure scope chain to include system level
        SettingsApiFactory.TestScopeChain.Clear();
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(level.Id, null));

        // Set initial value
        await SetValueAsync(key, "initial-value", level.Id, null);

        // Read — populates cache
        var firstRead = await GetSettingAsync(key);
        firstRead!.Success.Should().BeTrue();
        firstRead.Data.Should().Be("initial-value");

        // Update the value
        await SetValueAsync(key, "updated-value", level.Id, null);

        // Act — read again (should return updated value, not stale cache)
        var secondRead = await GetSettingAsync(key);

        // Assert
        secondRead.Should().NotBeNull();
        secondRead!.Success.Should().BeTrue();
        secondRead.Data.Should().Be("updated-value");
    }

    [Fact]
    public async Task DeleteValue_ThenRead_FallsBackToDefault()
    {
        // Arrange
        var level = await CreateLevelAsync("System");
        var key = UniqueKey("CacheDel");

        await CreateDefinitionAsync(key, SettingDataType.String, "the-default",
            new[] { level.Id });

        // Configure scope chain
        SettingsApiFactory.TestScopeChain.Clear();
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(level.Id, null));

        // Set a value
        var setValue = await SetValueAsync(key, "override-value", level.Id, null);
        setValue.Should().NotBeNull();

        // Read — populates cache with override
        var firstRead = await GetSettingAsync(key);
        firstRead!.Data.Should().Be("override-value");

        // Delete the override
        await DeleteValueAsync(setValue!.Id);

        // Act — read again (should fall back to definition default)
        var secondRead = await GetSettingAsync(key);

        // Assert
        secondRead.Should().NotBeNull();
        secondRead!.Success.Should().BeTrue();
        secondRead.Data.Should().Be("the-default");
    }

    [Fact]
    public async Task MultipleReads_AfterSet_ReturnConsistentValue()
    {
        // Arrange
        var level = await CreateLevelAsync("System");
        var key = UniqueKey("CacheCons");

        await CreateDefinitionAsync(key, SettingDataType.String, "0",
            new[] { level.Id });

        // Configure scope chain
        SettingsApiFactory.TestScopeChain.Clear();
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(level.Id, null));

        await SetValueAsync(key, "42", level.Id, null);

        // Act — read multiple times
        var read1 = await GetSettingAsync(key);
        var read2 = await GetSettingAsync(key);
        var read3 = await GetSettingAsync(key);

        // Assert — all reads return the same value
        read1!.Data.Should().Be("42");
        read2!.Data.Should().Be("42");
        read3!.Data.Should().Be("42");
    }

    #region Helper Methods

    private static string UniqueKey(string prefix) =>
        $"{prefix}_{Guid.NewGuid():N}"[..32];

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

    private async Task<SettingDefinitionDto> CreateDefinitionAsync(
        string key, SettingDataType dataType, string? defaultValue, Guid[] allowedLevelIds)
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
            AllowedLevelIds: allowedLevelIds.ToList());

        var response = await _client.PostAsJsonAsync("/api/settings/definitions", dto);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OperationResult<SettingDefinitionDto>>();
        result!.Success.Should().BeTrue();
        return result.Data!;
    }

    private async Task<SettingValueDto?> SetValueAsync(
        string key, string value, Guid levelId, Guid? scopeId)
    {
        var dto = new SetSettingValueDto(value, levelId, scopeId);
        var response = await _client.PutAsJsonAsync($"/api/settings/{key}", dto);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OperationResult<SettingValueDto>>();
        result!.Success.Should().BeTrue();
        return result.Data;
    }

    private async Task<OperationResult<string>?> GetSettingAsync(string key)
    {
        var response = await _client.GetAsync($"/api/settings/{key}");
        return await response.Content.ReadFromJsonAsync<OperationResult<string>>();
    }

    private async Task DeleteValueAsync(Guid valueId)
    {
        var response = await _client.DeleteAsync($"/api/settings/values/{valueId}");
        response.EnsureSuccessStatusCode();
    }

    #endregion
}
