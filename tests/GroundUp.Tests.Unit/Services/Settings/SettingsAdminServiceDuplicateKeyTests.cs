using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Enums;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Tests what happens when creating settings entities with duplicate keys/names.
/// The SettingsAdminService does NOT check for duplicates before inserting — it relies
/// on database unique constraints. These tests document the current behavior.
/// </summary>
public sealed class SettingsAdminServiceDuplicateKeyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestSettingsDbContext _context;
    private readonly SettingsAdminService _adminService;

    public SettingsAdminServiceDuplicateKeyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestSettingsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TestSettingsDbContext(options);
        _context.Database.EnsureCreated();

        // SettingsAdminService takes GroundUpDbContext — TestSettingsDbContext inherits from it
        _adminService = new SettingsAdminService(_context);
    }

    [Fact]
    public async Task CreateLevelAsync_DuplicateName_BehaviorTest()
    {
        // Arrange — SettingLevel does NOT have a unique index on Name,
        // so duplicate names are allowed at the database level.
        var dto = new CreateSettingLevelDto("System", null, null, 0);

        // Act — create first
        var first = await _adminService.CreateLevelAsync(dto);
        first.Success.Should().BeTrue();

        // Act — create second with same name
        var second = await _adminService.CreateLevelAsync(dto);

        // Assert — both succeed because there is no unique constraint on SettingLevel.Name.
        // NOTE: This means the application allows duplicate level names.
        // If this is undesirable, a unique index or application-level check should be added.
        second.Success.Should().BeTrue();
        second.Data!.Id.Should().NotBe(first.Data!.Id);
    }

    [Fact]
    public async Task CreateGroupAsync_DuplicateKey_BehaviorTest()
    {
        // Arrange — SettingGroup HAS a unique index on Key.
        // The SettingsAdminService does NOT check for duplicates before inserting,
        // so the DB constraint will throw a DbUpdateException.
        var dto = new CreateSettingGroupDto("MyGroup", "My Group", null, null, 0);

        // Act — create first
        var first = await _adminService.CreateGroupAsync(dto);
        first.Success.Should().BeTrue();

        // Act — create second with same key
        // NOTE: This throws an unhandled DbUpdateException because the admin service
        // does not catch unique constraint violations. This is a potential improvement area —
        // the service could catch the exception and return a BadRequest/Conflict result.
        var act = () => _adminService.CreateGroupAsync(dto);

        // Assert — unhandled exception from DB unique constraint violation
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CreateDefinitionAsync_DuplicateKey_BehaviorTest()
    {
        // Arrange — SettingDefinition HAS a unique index on Key.
        // Same behavior as groups: no application-level duplicate check.
        var dto = new CreateSettingDefinitionDto(
            Key: "MySetting",
            DataType: SettingDataType.String,
            DefaultValue: "default",
            GroupId: null,
            DisplayName: "My Setting",
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
            AllowedLevelIds: null);

        // Act — create first
        var first = await _adminService.CreateDefinitionAsync(dto);
        first.Success.Should().BeTrue();

        // Act — create second with same key
        // NOTE: This throws an unhandled DbUpdateException because the admin service
        // does not catch unique constraint violations. This is a potential improvement area —
        // the service could catch the exception and return a BadRequest/Conflict result.
        var act = () => _adminService.CreateDefinitionAsync(dto);

        // Assert — unhandled exception from DB unique constraint violation
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }
}
