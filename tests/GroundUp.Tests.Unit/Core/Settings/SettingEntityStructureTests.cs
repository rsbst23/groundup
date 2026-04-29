using FluentAssertions;
using GroundUp.Core.Entities;
using GroundUp.Core.Entities.Settings;

namespace GroundUp.Tests.Unit.Core.Settings;

/// <summary>
/// Reflection-based tests verifying the structural correctness of all 6 settings entities.
/// Validates sealed modifiers, base class inheritance, IAuditable implementation,
/// and navigation collection initialization.
/// </summary>
public sealed class SettingEntityStructureTests
{
    // ── SettingLevel ──────────────────────────────────────────

    [Fact]
    public void SettingLevel_IsSealed_ReturnsTrue()
    {
        typeof(SettingLevel).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void SettingLevel_ExtendsBaseEntity_ReturnsTrue()
    {
        typeof(BaseEntity).IsAssignableFrom(typeof(SettingLevel)).Should().BeTrue();
    }

    [Fact]
    public void SettingLevel_ImplementsIAuditable_ReturnsTrue()
    {
        typeof(IAuditable).IsAssignableFrom(typeof(SettingLevel)).Should().BeTrue();
    }

    [Fact]
    public void SettingLevel_ChildrenCollection_IsInitialized()
    {
        var entity = new SettingLevel();
        entity.Children.Should().NotBeNull();
    }

    // ── SettingGroup ─────────────────────────────────────────

    [Fact]
    public void SettingGroup_IsSealed_ReturnsTrue()
    {
        typeof(SettingGroup).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void SettingGroup_ExtendsBaseEntity_ReturnsTrue()
    {
        typeof(BaseEntity).IsAssignableFrom(typeof(SettingGroup)).Should().BeTrue();
    }

    [Fact]
    public void SettingGroup_ImplementsIAuditable_ReturnsTrue()
    {
        typeof(IAuditable).IsAssignableFrom(typeof(SettingGroup)).Should().BeTrue();
    }

    [Fact]
    public void SettingGroup_SettingsCollection_IsInitialized()
    {
        var entity = new SettingGroup();
        entity.Settings.Should().NotBeNull();
    }

    // ── SettingDefinition ────────────────────────────────────

    [Fact]
    public void SettingDefinition_IsSealed_ReturnsTrue()
    {
        typeof(SettingDefinition).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void SettingDefinition_ExtendsBaseEntity_ReturnsTrue()
    {
        typeof(BaseEntity).IsAssignableFrom(typeof(SettingDefinition)).Should().BeTrue();
    }

    [Fact]
    public void SettingDefinition_ImplementsIAuditable_ReturnsTrue()
    {
        typeof(IAuditable).IsAssignableFrom(typeof(SettingDefinition)).Should().BeTrue();
    }

    [Fact]
    public void SettingDefinition_NavigationCollections_AreInitialized()
    {
        var entity = new SettingDefinition();
        entity.Options.Should().NotBeNull();
        entity.Values.Should().NotBeNull();
        entity.AllowedLevels.Should().NotBeNull();
    }

    // ── SettingOption ────────────────────────────────────────

    [Fact]
    public void SettingOption_IsSealed_ReturnsTrue()
    {
        typeof(SettingOption).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void SettingOption_ExtendsBaseEntity_ReturnsTrue()
    {
        typeof(BaseEntity).IsAssignableFrom(typeof(SettingOption)).Should().BeTrue();
    }

    [Fact]
    public void SettingOption_DoesNotImplementIAuditable_ReturnsTrue()
    {
        typeof(IAuditable).IsAssignableFrom(typeof(SettingOption)).Should().BeFalse();
    }

    // ── SettingValue ─────────────────────────────────────────

    [Fact]
    public void SettingValue_IsSealed_ReturnsTrue()
    {
        typeof(SettingValue).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void SettingValue_ExtendsBaseEntity_ReturnsTrue()
    {
        typeof(BaseEntity).IsAssignableFrom(typeof(SettingValue)).Should().BeTrue();
    }

    [Fact]
    public void SettingValue_ImplementsIAuditable_ReturnsTrue()
    {
        typeof(IAuditable).IsAssignableFrom(typeof(SettingValue)).Should().BeTrue();
    }

    // ── SettingDefinitionLevel ───────────────────────────────

    [Fact]
    public void SettingDefinitionLevel_IsSealed_ReturnsTrue()
    {
        typeof(SettingDefinitionLevel).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void SettingDefinitionLevel_ExtendsBaseEntity_ReturnsTrue()
    {
        typeof(BaseEntity).IsAssignableFrom(typeof(SettingDefinitionLevel)).Should().BeTrue();
    }

    [Fact]
    public void SettingDefinitionLevel_DoesNotImplementIAuditable_ReturnsTrue()
    {
        typeof(IAuditable).IsAssignableFrom(typeof(SettingDefinitionLevel)).Should().BeFalse();
    }
}
