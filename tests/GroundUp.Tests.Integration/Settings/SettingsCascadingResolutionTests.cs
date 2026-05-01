using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Tests.Integration.Settings;

/// <summary>
/// End-to-end cascading resolution tests. Seeds levels and definitions via admin
/// endpoints, sets values at various levels, and verifies resolution through
/// GET /api/settings/{key}. Configures the test scope chain provider to simulate
/// different tenant contexts.
/// </summary>
[Collection("SettingsApi")]
public sealed class SettingsCascadingResolutionTests : IAsyncLifetime
{
    private readonly SettingsApiFactory _factory;
    private HttpClient _client = null!;

    public SettingsCascadingResolutionTests(SettingsApiFactory factory)
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
        // Reset scope chain after each test class
        SettingsApiFactory.TestScopeChain.Clear();
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Resolve_SystemValueSet_ReturnsSystemValue()
    {
        // Arrange
        var systemLevel = await CreateLevelAsync("System");
        var key = UniqueKey("SysFallback");

        await CreateDefinitionAsync(key, SettingDataType.String, "default-val",
            new[] { systemLevel.Id });

        await SetValueAsync(key, "system-val", systemLevel.Id, null);

        // Configure scope chain to include system level
        SettingsApiFactory.TestScopeChain.Clear();
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(systemLevel.Id, null));

        // Act
        var result = await GetSettingAsync(key);

        // Assert
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().Be("system-val");
    }

    [Fact]
    public async Task Resolve_TenantOverrideExists_ReturnsTenantValue()
    {
        // Arrange
        var systemLevel = await CreateLevelAsync("System");
        var tenantLevel = await CreateLevelAsync("Tenant", systemLevel.Id);
        var tenantId = Guid.NewGuid();
        var key = UniqueKey("TenantOvr");

        await CreateDefinitionAsync(key, SettingDataType.String, "default-val",
            new[] { systemLevel.Id, tenantLevel.Id });

        // Set system-level value
        await SetValueAsync(key, "system-val", systemLevel.Id, null);

        // Set tenant-level override
        await SetValueAsync(key, "tenant-val", tenantLevel.Id, tenantId);

        // Configure scope chain: tenant (most specific) → system (least specific)
        SettingsApiFactory.TestScopeChain.Clear();
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(tenantLevel.Id, tenantId));
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(systemLevel.Id, null));

        // Act
        var result = await GetSettingAsync(key);

        // Assert — tenant override takes precedence
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().Be("tenant-val");
    }

    [Fact]
    public async Task Resolve_DifferentTenantWithoutOverride_ReturnsSystemValue()
    {
        // Arrange
        var systemLevel = await CreateLevelAsync("System");
        var tenantLevel = await CreateLevelAsync("Tenant", systemLevel.Id);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var key = UniqueKey("DiffTenant");

        await CreateDefinitionAsync(key, SettingDataType.String, "default-val",
            new[] { systemLevel.Id, tenantLevel.Id });

        // Set system-level value
        await SetValueAsync(key, "system-val", systemLevel.Id, null);

        // Set override only for tenant A
        await SetValueAsync(key, "tenant-a-val", tenantLevel.Id, tenantA);

        // Configure scope chain for tenant B (no override)
        SettingsApiFactory.TestScopeChain.Clear();
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(tenantLevel.Id, tenantB));
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(systemLevel.Id, null));

        // Act
        var result = await GetSettingAsync(key);

        // Assert — tenant B has no override, falls back to system value
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().Be("system-val");
    }

    [Fact]
    public async Task Resolve_DeleteTenantOverride_RevertsToSystemValue()
    {
        // Arrange
        var systemLevel = await CreateLevelAsync("System");
        var tenantLevel = await CreateLevelAsync("Tenant", systemLevel.Id);
        var tenantId = Guid.NewGuid();
        var key = UniqueKey("DelRevert");

        await CreateDefinitionAsync(key, SettingDataType.String, "default-val",
            new[] { systemLevel.Id, tenantLevel.Id });

        // Set system-level value
        await SetValueAsync(key, "system-val", systemLevel.Id, null);

        // Set tenant-level override
        var tenantValue = await SetValueAsync(key, "tenant-val", tenantLevel.Id, tenantId);
        tenantValue.Should().NotBeNull();

        // Configure scope chain: tenant → system
        SettingsApiFactory.TestScopeChain.Clear();
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(tenantLevel.Id, tenantId));
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(systemLevel.Id, null));

        // Verify tenant override is active
        var before = await GetSettingAsync(key);
        before!.Data.Should().Be("tenant-val");

        // Act — delete the tenant override
        await DeleteValueAsync(tenantValue!.Id);

        // Assert — should revert to system value
        var after = await GetSettingAsync(key);
        after.Should().NotBeNull();
        after!.Success.Should().BeTrue();
        after.Data.Should().Be("system-val");
    }

    [Fact]
    public async Task Resolve_NoValueSet_ReturnsDefaultValue()
    {
        // Arrange
        var systemLevel = await CreateLevelAsync("System");
        var key = UniqueKey("DefaultOnly");

        await CreateDefinitionAsync(key, SettingDataType.String, "the-default",
            new[] { systemLevel.Id });

        // Configure scope chain with system level (no values set)
        SettingsApiFactory.TestScopeChain.Clear();
        SettingsApiFactory.TestScopeChain.Add(new SettingScopeEntry(systemLevel.Id, null));

        // Act
        var result = await GetSettingAsync(key);

        // Assert — falls back to definition default
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().Be("the-default");
    }

    [Fact]
    public async Task Resolve_NonExistentKey_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/settings/NonExistent_{Guid.NewGuid():N}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #region Helper Methods

    private static string UniqueKey(string prefix) =>
        $"{prefix}_{Guid.NewGuid():N}"[..32];

    private async Task<SettingLevelDto> CreateLevelAsync(string name, Guid? parentId = null)
    {
        var dto = new CreateSettingLevelDto(
            $"{name}_{Guid.NewGuid():N}"[..20], null, parentId, 0);
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
