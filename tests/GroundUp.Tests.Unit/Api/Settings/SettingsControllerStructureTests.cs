using System.Reflection;
using FluentAssertions;
using GroundUp.Api.Controllers.Settings;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Tests.Unit.Api.Settings;

/// <summary>
/// Reflection-based tests verifying the SettingsController is wired correctly
/// with proper attributes, inheritance, and endpoint structure.
/// </summary>
public sealed class SettingsControllerStructureTests
{
    private readonly Type _controllerType = typeof(SettingsController);

    [Fact]
    public void SettingsController_InheritsFromControllerBase()
    {
        _controllerType.BaseType.Should().Be(typeof(ControllerBase));
    }

    [Fact]
    public void SettingsController_HasApiControllerAttribute()
    {
        var attribute = _controllerType.GetCustomAttribute<ApiControllerAttribute>();
        attribute.Should().NotBeNull();
    }

    [Fact]
    public void SettingsController_HasCorrectRouteAttribute()
    {
        var attribute = _controllerType.GetCustomAttribute<RouteAttribute>();
        attribute.Should().NotBeNull();
        attribute!.Template.Should().Be("api/settings");
    }

    [Fact]
    public void SettingsController_AcceptsISettingsService()
    {
        var constructors = _controllerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        constructors.Should().HaveCount(1);

        var parameters = constructors[0].GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(ISettingsService));
    }

    [Fact]
    public void SettingsController_HasGetByKeyEndpoint()
    {
        var method = _controllerType.GetMethod("GetByKey", BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull();

        var httpGetAttr = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGetAttr.Should().NotBeNull();
        httpGetAttr!.Template.Should().Be("{key}");
    }

    [Fact]
    public void SettingsController_HasGetAllEndpoint()
    {
        var method = _controllerType.GetMethod("GetAll", BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull();

        var httpGetAttr = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGetAttr.Should().NotBeNull();
        httpGetAttr!.Template.Should().BeNullOrEmpty();
    }

    [Fact]
    public void SettingsController_HasGetGroupEndpoint()
    {
        var method = _controllerType.GetMethod("GetGroup", BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull();

        var httpGetAttr = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGetAttr.Should().NotBeNull();
        httpGetAttr!.Template.Should().Be("groups/{groupKey}");
    }

    [Fact]
    public void SettingsController_HasSetValueEndpoint()
    {
        var method = _controllerType.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull();

        var httpPutAttr = method!.GetCustomAttribute<HttpPutAttribute>();
        httpPutAttr.Should().NotBeNull();
        httpPutAttr!.Template.Should().Be("{key}");
    }

    [Fact]
    public void SettingsController_HasDeleteValueEndpoint()
    {
        var method = _controllerType.GetMethod("DeleteValue", BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull();

        var httpDeleteAttr = method!.GetCustomAttribute<HttpDeleteAttribute>();
        httpDeleteAttr.Should().NotBeNull();
        httpDeleteAttr!.Template.Should().Be("values/{id}");
    }

    [Fact]
    public void SettingsController_IsSealed()
    {
        _controllerType.IsSealed.Should().BeTrue();
    }
}
