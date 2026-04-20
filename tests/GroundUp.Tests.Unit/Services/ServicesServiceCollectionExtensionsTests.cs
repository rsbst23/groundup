using FluentAssertions;
using FluentValidation;
using GroundUp.Services;
using GroundUp.Tests.Unit.Services.TestHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="ServicesServiceCollectionExtensions"/>.
/// </summary>
public sealed class ServicesServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGroundUpServices_RegistersValidatorsFromAssembly()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceTestDtoValidator).Assembly;

        // Act
        services.AddGroundUpServices(assembly);
        var provider = services.BuildServiceProvider();
        var validator = provider.GetService<IValidator<ServiceTestDto>>();

        // Assert
        validator.Should().NotBeNull();
        validator.Should().BeOfType<ServiceTestDtoValidator>();
    }

    [Fact]
    public void AddGroundUpServices_ReturnsServiceCollectionForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceTestDtoValidator).Assembly;

        // Act
        var result = services.AddGroundUpServices(assembly);

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddGroundUpServices_NoValidatorsInAssembly_CompletesWithoutError()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(object).Assembly;

        // Act
        var act = () => services.AddGroundUpServices(assembly);

        // Assert
        act.Should().NotThrow();
    }
}
