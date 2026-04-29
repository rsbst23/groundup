using System.Reflection;
using FluentAssertions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Enums;

namespace GroundUp.Tests.Unit.Core.Settings;

/// <summary>
/// Tests verifying the structural correctness of all 5 settings DTO records.
/// Validates record type, expected properties, and absence of navigation/audit fields.
/// </summary>
public sealed class SettingDtoStructureTests
{
    // ── Record type verification ─────────────────────────────

    [Theory]
    [InlineData(typeof(SettingLevelDto))]
    [InlineData(typeof(SettingGroupDto))]
    [InlineData(typeof(SettingDefinitionDto))]
    [InlineData(typeof(SettingOptionDto))]
    [InlineData(typeof(SettingValueDto))]
    public void Dto_IsRecordType(Type dtoType)
    {
        // Records are classes with a compiler-generated <Clone>$ method
        dtoType.IsClass.Should().BeTrue();
        dtoType.GetMethod("<Clone>$").Should().NotBeNull();
    }

    // ── SettingLevelDto properties ───────────────────────────

    [Fact]
    public void SettingLevelDto_HasExpectedProperties()
    {
        var props = typeof(SettingLevelDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propDict = props.ToDictionary(p => p.Name, p => p.PropertyType);

        propDict.Should().ContainKey("Id").WhoseValue.Should().Be(typeof(Guid));
        propDict.Should().ContainKey("Name").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("Description").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("ParentId").WhoseValue.Should().Be(typeof(Guid?));
        propDict.Should().ContainKey("DisplayOrder").WhoseValue.Should().Be(typeof(int));
    }

    // ── SettingGroupDto properties ───────────────────────────

    [Fact]
    public void SettingGroupDto_HasExpectedProperties()
    {
        var props = typeof(SettingGroupDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propDict = props.ToDictionary(p => p.Name, p => p.PropertyType);

        propDict.Should().ContainKey("Id").WhoseValue.Should().Be(typeof(Guid));
        propDict.Should().ContainKey("Key").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("DisplayName").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("Description").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("Icon").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("DisplayOrder").WhoseValue.Should().Be(typeof(int));
    }

    // ── SettingDefinitionDto properties ──────────────────────

    [Fact]
    public void SettingDefinitionDto_HasExpectedProperties()
    {
        var props = typeof(SettingDefinitionDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propDict = props.ToDictionary(p => p.Name, p => p.PropertyType);

        propDict.Should().ContainKey("Id").WhoseValue.Should().Be(typeof(Guid));
        propDict.Should().ContainKey("Key").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("DataType").WhoseValue.Should().Be(typeof(SettingDataType));
        propDict.Should().ContainKey("DefaultValue").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("GroupId").WhoseValue.Should().Be(typeof(Guid?));
        propDict.Should().ContainKey("DisplayName").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("Description").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("Category").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("DisplayOrder").WhoseValue.Should().Be(typeof(int));
        propDict.Should().ContainKey("IsVisible").WhoseValue.Should().Be(typeof(bool));
        propDict.Should().ContainKey("IsReadOnly").WhoseValue.Should().Be(typeof(bool));
        propDict.Should().ContainKey("AllowMultiple").WhoseValue.Should().Be(typeof(bool));
        propDict.Should().ContainKey("IsEncrypted").WhoseValue.Should().Be(typeof(bool));
        propDict.Should().ContainKey("IsSecret").WhoseValue.Should().Be(typeof(bool));
        propDict.Should().ContainKey("IsRequired").WhoseValue.Should().Be(typeof(bool));
        propDict.Should().ContainKey("MinValue").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("MaxValue").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("MinLength").WhoseValue.Should().Be(typeof(int?));
        propDict.Should().ContainKey("MaxLength").WhoseValue.Should().Be(typeof(int?));
        propDict.Should().ContainKey("RegexPattern").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("ValidationMessage").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("DependsOnKey").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("DependsOnOperator").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("DependsOnValue").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("CustomValidatorType").WhoseValue.Should().Be(typeof(string));
    }

    // ── SettingOptionDto properties ──────────────────────────

    [Fact]
    public void SettingOptionDto_HasExpectedProperties()
    {
        var props = typeof(SettingOptionDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propDict = props.ToDictionary(p => p.Name, p => p.PropertyType);

        propDict.Should().ContainKey("Id").WhoseValue.Should().Be(typeof(Guid));
        propDict.Should().ContainKey("SettingDefinitionId").WhoseValue.Should().Be(typeof(Guid));
        propDict.Should().ContainKey("Value").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("Label").WhoseValue.Should().Be(typeof(string));
        propDict.Should().ContainKey("DisplayOrder").WhoseValue.Should().Be(typeof(int));
        propDict.Should().ContainKey("IsDefault").WhoseValue.Should().Be(typeof(bool));
        propDict.Should().ContainKey("ParentOptionValue").WhoseValue.Should().Be(typeof(string));
    }

    // ── SettingValueDto properties ───────────────────────────

    [Fact]
    public void SettingValueDto_HasExpectedProperties()
    {
        var props = typeof(SettingValueDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propDict = props.ToDictionary(p => p.Name, p => p.PropertyType);

        propDict.Should().ContainKey("Id").WhoseValue.Should().Be(typeof(Guid));
        propDict.Should().ContainKey("SettingDefinitionId").WhoseValue.Should().Be(typeof(Guid));
        propDict.Should().ContainKey("LevelId").WhoseValue.Should().Be(typeof(Guid));
        propDict.Should().ContainKey("ScopeId").WhoseValue.Should().Be(typeof(Guid?));
        propDict.Should().ContainKey("Value").WhoseValue.Should().Be(typeof(string));
    }

    // ── No navigation properties ─────────────────────────────

    [Theory]
    [InlineData(typeof(SettingLevelDto))]
    [InlineData(typeof(SettingGroupDto))]
    [InlineData(typeof(SettingDefinitionDto))]
    [InlineData(typeof(SettingOptionDto))]
    [InlineData(typeof(SettingValueDto))]
    public void Dto_DoesNotContainNavigationProperties(Type dtoType)
    {
        var props = dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        props.Should().NotContain(p =>
            p.PropertyType.IsGenericType &&
            p.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>),
            "DTOs should not contain ICollection<T> navigation properties");
    }

    // ── No audit fields ──────────────────────────────────────

    [Theory]
    [InlineData(typeof(SettingLevelDto))]
    [InlineData(typeof(SettingGroupDto))]
    [InlineData(typeof(SettingDefinitionDto))]
    [InlineData(typeof(SettingOptionDto))]
    [InlineData(typeof(SettingValueDto))]
    public void Dto_DoesNotContainAuditFields(Type dtoType)
    {
        var auditFields = new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" };
        var propNames = dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        propNames.Should().NotContain(auditFields);
    }
}
