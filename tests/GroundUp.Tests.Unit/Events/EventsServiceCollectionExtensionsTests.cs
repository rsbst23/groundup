using Microsoft.Extensions.DependencyInjection;
using GroundUp.Events;

namespace GroundUp.Tests.Unit.Events;

/// <summary>
/// Unit tests for <see cref="EventsServiceCollectionExtensions"/>.
/// </summary>
public sealed class EventsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGroundUpEvents_RegistersScopedInProcessEventBus()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGroundUpEvents();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IEventBus));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<InProcessEventBus>(scope.ServiceProvider.GetRequiredService<IEventBus>());
    }

    [Fact]
    public async Task PublishAsync_ScopedHandlerUnderScopeValidation_HandlerRunsInPublisherScope()
    {
        // Regression: a singleton bus resolved scoped handlers from the root provider, which throws
        // under scope validation (Development) and silently skipped settings cache invalidation.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGroundUpEvents();
        services.AddScoped<ScopeMarker>();
        services.AddScoped<IEventHandler<TestEvent>, RecordingHandler>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using var scope = provider.CreateScope();
        var marker = scope.ServiceProvider.GetRequiredService<ScopeMarker>();

        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(new TestEvent());

        Assert.Equal(1, marker.HandledCount);
    }

    [Fact]
    public async Task PublishAsync_SingletonHandler_StillRuns()
    {
        var handler = new SingletonHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGroundUpEvents();
        services.AddSingleton<IEventHandler<TestEvent>>(handler);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(new TestEvent());

        Assert.Equal(1, handler.HandledCount);
    }

    [Fact]
    public void AddGroundUpEvents_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddGroundUpEvents();

        Assert.Same(services, result);
    }

    private sealed record TestEvent : BaseEvent;

    private sealed class ScopeMarker
    {
        public int HandledCount { get; set; }
    }

    private sealed class RecordingHandler(ScopeMarker marker) : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            marker.HandledCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class SingletonHandler : IEventHandler<TestEvent>
    {
        public int HandledCount { get; private set; }

        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            HandledCount++;
            return Task.CompletedTask;
        }
    }
}
