using Microsoft.Extensions.DependencyInjection;
using GroundUp.Events;

namespace GroundUp.Tests.Unit.Events;

/// <summary>
/// Unit tests for <see cref="EventsServiceCollectionExtensions"/>.
/// </summary>
public sealed class EventsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGroundUpEvents_RegistersSingletonIEventBus()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGroundUpEvents();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<IEventBus>();

        Assert.IsType<InProcessEventBus>(eventBus);
    }

    [Fact]
    public void AddGroundUpEvents_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddGroundUpEvents();

        Assert.Same(services, result);
    }
}
