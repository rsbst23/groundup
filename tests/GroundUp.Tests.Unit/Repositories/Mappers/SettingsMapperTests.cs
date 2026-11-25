using FluentAssertions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Repositories.Mappers;

namespace GroundUp.Tests.Unit.Repositories.Mappers;

/// <summary>
/// Unit tests for the settings Mapperly mappers. The definition test asserts every DTO field, so it
/// shows the generated mapping carries the same values as the hand-written mapping it replaces,
/// plus the two new lists.
/// </summary>
public sealed class SettingsMapperTests
{
    [Fact]
    public void SettingDefinitionMapper_ToDto_FullyPopulated_MapsEveryField()
    {
        // Arrange
        var definition = CreateFullDefinition();
        var levelA = Guid.NewGuid();
        var levelB = Guid.NewGuid();
        definition.AllowedLevels.Add(new SettingDefinitionLevel { Id = Guid.NewGuid(), SettingDefinitionId = definition.Id, SettingLevelId = levelA });
        definition.AllowedLevels.Add(new SettingDefinitionLevel { Id = Guid.NewGuid(), SettingDefinitionId = definition.Id, SettingLevelId = levelB });

        var optionLast = NewOption(definition.Id, "z", displayOrder: 2);
        var optionFirstB = NewOption(definition.Id, "b", displayOrder: 1, isDefault: true, parent: "p");
        var optionFirstA = NewOption(definition.Id, "a", displayOrder: 1);
        definition.Options.Add(optionLast);
        definition.Options.Add(optionFirstB);
        definition.Options.Add(optionFirstA);

        // Act
        var dto = SettingDefinitionMapper.ToDto(definition);

        // Assert
        var expected = new SettingDefinitionDto(
            definition.Id, "app.max-upload", SettingDataType.Int, "50", definition.GroupId,
            "Max upload", "Largest upload in MB", "e.g. 50", "Limits", 3,
            false, true, true, true, true, true,
            "1", "500", 1, 4, "^[0-9]+$", "Digits only",
            "app.uploads-enabled", "Equals", "true", "My.Validator, My.Assembly",
            new[] { levelA, levelB },
            new[]
            {
                new SettingOptionDto(optionFirstA.Id, definition.Id, "a", "Label a", 1, false, null),
                new SettingOptionDto(optionFirstB.Id, definition.Id, "b", "Label b", 1, true, "p"),
                new SettingOptionDto(optionLast.Id, definition.Id, "z", "Label z", 2, false, null)
            });

        dto.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering());
    }

    [Fact]
    public void SettingDefinitionMapper_ToDto_NoLevelsOrOptions_ReturnsEmptyLists()
    {
        // Arrange
        var definition = CreateFullDefinition();

        // Act
        var dto = SettingDefinitionMapper.ToDto(definition);

        // Assert
        dto.AllowedLevelIds.Should().NotBeNull().And.BeEmpty();
        dto.Options.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void SettingLevelMapper_ToDto_MapsEveryField()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var level = new SettingLevel { Id = Guid.NewGuid(), Name = "Tenant", Description = "Per tenant", ParentId = parentId, DisplayOrder = 1 };

        // Act
        var dto = SettingLevelMapper.ToDto(level);

        // Assert
        dto.Should().Be(new SettingLevelDto(level.Id, "Tenant", "Per tenant", parentId, 1));
    }

    [Fact]
    public void SettingGroupMapper_ToDto_MapsEveryField()
    {
        // Arrange
        var group = new SettingGroup { Id = Guid.NewGuid(), Key = "email", DisplayName = "Email", Description = "SMTP", Icon = "mail", DisplayOrder = 2 };

        // Act
        var dto = SettingGroupMapper.ToDto(group);

        // Assert
        dto.Should().Be(new SettingGroupDto(group.Id, "email", "Email", "SMTP", "mail", 2));
    }

    [Fact]
    public void SettingValueMapper_ToDto_MapsEveryField()
    {
        // Arrange
        var value = new SettingValue
        {
            Id = Guid.NewGuid(),
            SettingDefinitionId = Guid.NewGuid(),
            LevelId = Guid.NewGuid(),
            ScopeId = Guid.NewGuid(),
            Value = "42"
        };

        // Act
        var dto = SettingValueMapper.ToDto(value);

        // Assert
        dto.Should().Be(new SettingValueDto(value.Id, value.SettingDefinitionId, value.LevelId, value.ScopeId, "42"));
    }

    private static SettingDefinition CreateFullDefinition() => new()
    {
        Id = Guid.NewGuid(),
        Key = "app.max-upload",
        DataType = SettingDataType.Int,
        DefaultValue = "50",
        GroupId = Guid.NewGuid(),
        DisplayName = "Max upload",
        Description = "Largest upload in MB",
        Placeholder = "e.g. 50",
        Category = "Limits",
        DisplayOrder = 3,
        IsVisible = false,
        IsReadOnly = true,
        AllowMultiple = true,
        IsEncrypted = true,
        IsSecret = true,
        IsRequired = true,
        MinValue = "1",
        MaxValue = "500",
        MinLength = 1,
        MaxLength = 4,
        RegexPattern = "^[0-9]+$",
        ValidationMessage = "Digits only",
        DependsOnKey = "app.uploads-enabled",
        DependsOnOperator = "Equals",
        DependsOnValue = "true",
        CustomValidatorType = "My.Validator, My.Assembly"
    };

    private static SettingOption NewOption(Guid definitionId, string value, int displayOrder, bool isDefault = false, string? parent = null) => new()
    {
        Id = Guid.NewGuid(),
        SettingDefinitionId = definitionId,
        Value = value,
        Label = $"Label {value}",
        DisplayOrder = displayOrder,
        IsDefault = isDefault,
        ParentOptionValue = parent
    };
}
