using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Events;
using GroundUp.Services.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Settings.TestHelpers;

/// <summary>
/// Helper class that creates an in-memory SQLite DbContext and mocks for testing SettingsService.
/// Each test should call CreateContext() to get a fresh context with the connection kept open.
/// </summary>
public sealed class SettingsTestFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public IEventBus EventBus { get; }
    public ISettingEncryptionProvider EncryptionProvider { get; }

    public SettingsTestFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        EventBus = Substitute.For<IEventBus>();
        EncryptionProvider = Substitute.For<ISettingEncryptionProvider>();
    }

    /// <summary>
    /// Creates a fresh TestSettingsDbContext backed by the open SQLite connection.
    /// The schema is created on first call via EnsureCreated().
    /// </summary>
    public TestSettingsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestSettingsDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new TestSettingsDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Creates a SettingsService with the given context and optional encryption provider.
    /// </summary>
    public SettingsService CreateService(TestSettingsDbContext context, ISettingEncryptionProvider? encryptionProvider = null)
    {
        return new SettingsService(context, EventBus, encryptionProvider);
    }

    /// <summary>
    /// Creates a SettingsService with the default mock encryption provider.
    /// </summary>
    public SettingsService CreateServiceWithEncryption(TestSettingsDbContext context)
    {
        return new SettingsService(context, EventBus, EncryptionProvider);
    }

    /// <summary>
    /// Seeds a SettingLevel and returns its ID.
    /// </summary>
    public static async Task<Guid> SeedLevelAsync(TestSettingsDbContext context, string name = "System", int displayOrder = 0)
    {
        var level = new SettingLevel
        {
            Id = Guid.NewGuid(),
            Name = name,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };
        context.Set<SettingLevel>().Add(level);
        await context.SaveChangesAsync();
        return level.Id;
    }

    /// <summary>
    /// Seeds a SettingDefinition with allowed levels and returns the definition.
    /// </summary>
    public static async Task<SettingDefinition> SeedDefinitionAsync(
        TestSettingsDbContext context,
        string key = "TestSetting",
        SettingDataType dataType = SettingDataType.String,
        string? defaultValue = null,
        bool isEncrypted = false,
        bool isSecret = false,
        bool isReadOnly = false,
        bool isRequired = false,
        string? minValue = null,
        string? maxValue = null,
        int? minLength = null,
        int? maxLength = null,
        string? regexPattern = null,
        string? validationMessage = null,
        int displayOrder = 0,
        Guid? groupId = null,
        params Guid[] allowedLevelIds)
    {
        var definition = new SettingDefinition
        {
            Id = Guid.NewGuid(),
            Key = key,
            DataType = dataType,
            DefaultValue = defaultValue,
            IsEncrypted = isEncrypted,
            IsSecret = isSecret,
            IsReadOnly = isReadOnly,
            IsRequired = isRequired,
            MinValue = minValue,
            MaxValue = maxValue,
            MinLength = minLength,
            MaxLength = maxLength,
            RegexPattern = regexPattern,
            ValidationMessage = validationMessage,
            DisplayName = key,
            DisplayOrder = displayOrder,
            GroupId = groupId,
            CreatedAt = DateTime.UtcNow
        };

        context.Set<SettingDefinition>().Add(definition);
        await context.SaveChangesAsync();

        foreach (var levelId in allowedLevelIds)
        {
            var defLevel = new SettingDefinitionLevel
            {
                Id = Guid.NewGuid(),
                SettingDefinitionId = definition.Id,
                SettingLevelId = levelId
            };
            context.Set<SettingDefinitionLevel>().Add(defLevel);
        }

        await context.SaveChangesAsync();
        return definition;
    }

    /// <summary>
    /// Seeds a SettingValue and returns its ID.
    /// </summary>
    public static async Task<Guid> SeedValueAsync(
        TestSettingsDbContext context,
        Guid definitionId,
        Guid levelId,
        Guid? scopeId,
        string? value)
    {
        var settingValue = new SettingValue
        {
            Id = Guid.NewGuid(),
            SettingDefinitionId = definitionId,
            LevelId = levelId,
            ScopeId = scopeId,
            Value = value,
            CreatedAt = DateTime.UtcNow
        };
        context.Set<SettingValue>().Add(settingValue);
        await context.SaveChangesAsync();
        return settingValue.Id;
    }

    /// <summary>
    /// Seeds a SettingGroup and returns its ID.
    /// </summary>
    public static async Task<Guid> SeedGroupAsync(
        TestSettingsDbContext context,
        string key = "TestGroup",
        string displayName = "Test Group")
    {
        var group = new SettingGroup
        {
            Id = Guid.NewGuid(),
            Key = key,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow
        };
        context.Set<SettingGroup>().Add(group);
        await context.SaveChangesAsync();
        return group.Id;
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
