using System.Reflection;
using FluentAssertions;
using GroundUp.Core.Attributes;

namespace GroundUp.Tests.Unit.Authentication;

/// <summary>
/// Verifies AttributeUsage, constructor behavior, and property types for security attributes.
/// </summary>
public sealed class SecurityAttributeTests
{
    [Fact]
    public void RequiresPermissionAttribute_HasCorrectAttributeUsage()
    {
        var usage = typeof(RequiresPermissionAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(AttributeTargets.Method);
        usage.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void RequiresRoleAttribute_HasCorrectAttributeUsage()
    {
        var usage = typeof(RequiresRoleAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(AttributeTargets.Method);
        usage.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void RequiresPermissionAttribute_EmptyArray_StoresEmptyList()
    {
        var attr = new RequiresPermissionAttribute();
        attr.Permissions.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void RequiresPermissionAttribute_SinglePermission_StoresCorrectly()
    {
        var attr = new RequiresPermissionAttribute("settings.read");
        attr.Permissions.Should().ContainSingle().Which.Should().Be("settings.read");
    }

    [Fact]
    public void RequiresPermissionAttribute_MultiplePermissions_StoresInOrder()
    {
        var attr = new RequiresPermissionAttribute("settings.read", "settings.write", "users.manage");
        attr.Permissions.Should().HaveCount(3);
        attr.Permissions.Should().ContainInOrder("settings.read", "settings.write", "users.manage");
    }

    [Fact]
    public void RequiresPermissionAttribute_Permissions_IsIReadOnlyListOfString()
    {
        var property = typeof(RequiresPermissionAttribute).GetProperty(nameof(RequiresPermissionAttribute.Permissions));
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(IReadOnlyList<string>));
    }

    [Fact]
    public void RequiresRoleAttribute_EmptyArray_StoresEmptyList()
    {
        var attr = new RequiresRoleAttribute();
        attr.Roles.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void RequiresRoleAttribute_SingleRole_StoresCorrectly()
    {
        var attr = new RequiresRoleAttribute("Admin");
        attr.Roles.Should().ContainSingle().Which.Should().Be("Admin");
    }

    [Fact]
    public void RequiresRoleAttribute_MultipleRoles_StoresInOrder()
    {
        var attr = new RequiresRoleAttribute("Admin", "Manager", "User");
        attr.Roles.Should().HaveCount(3);
        attr.Roles.Should().ContainInOrder("Admin", "Manager", "User");
    }

    [Fact]
    public void RequiresRoleAttribute_Roles_IsIReadOnlyListOfString()
    {
        var property = typeof(RequiresRoleAttribute).GetProperty(nameof(RequiresRoleAttribute.Roles));
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(IReadOnlyList<string>));
    }
}
