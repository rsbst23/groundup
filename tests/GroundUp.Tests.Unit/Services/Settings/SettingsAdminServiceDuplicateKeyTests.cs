using FluentAssertions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Enums;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Tests that creating settings entities with duplicate keys/names returns
/// a clean BadRequest result instead of throwing an unhandled exception.
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

        _adminService = new SettingsAdminService(_context);
    }

    [Fact]
    public async Task CreateLevelAsync_DuplicateName_ReturnsBadRequest()
    {
        // Arrange
        var dto = new CreateSettingLevelDto("System", null, null, 0);

        // Act — create first
        var first = await _adminService.CreateLevelAsync(dto);
        first.Success.Should().BeTrue();

        // Act — create second with same name
        var second = await _adminService.CreateLevelAsync(dto);

        // Assert — returns BadRequest with descriptive message
        second.Success.Should().BeFalse();
        second.StatusCode.Should().Be(400);
        second.Message.Should().Contain("System");
        second.Message.Should().Contain("already exists");
    }

    [Fact]
    public async Task CreateGroupAsync_DuplicateKey_ReturnsBadRequest()
    {
        // Arrange
        var dto = new CreateSettingGroupDto("MyGroup", "My Group", null, null, 0);

        // Act — create first
        var first = await _adminService.CreateGroupAsync(dto);
        first.Success.Should().BeTrue();

        // Act — create second with same key
        var second = await _adminService.CreateGroupAsync(dto);

        // Assert — returns BadRequest with descriptive message
        second.Success.Should().BeFalse();
        second.StatusCode.Should().Be(400);
        second.Message.Should().Contain("MyGroup");
        second.Message.Should().Contain("already exists");
    }

    [Fact]
    public async Task CreateDefinitionAsync_DuplicateKey_ReturnsBadRequest()
    {
        // Arrange
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
        var second = await _adminService.CreateDefinitionAsync(dto);

        // Assert — returns BadRequest with descriptive message
        second.Success.Should().BeFalse();
        second.StatusCode.Should().Be(400);
        second.Message.Should().Contain("MySetting");
        second.Message.Should().Contain("already exists");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }
}
