using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using GroundUp.Events;

namespace GroundUp.Tests.Unit.Events;

/// <summary>
/// Unit tests for <see cref="InProcessEventBus"/>.
/// </summary>
public sealed class InProcessEventBusTests
{
    private readonly ILogger<InProcessEventBus> _logger = Substitute.For<ILogger<InProcessEventBus>>();

    private InProcessEventBus CreateBus(IServiceProvider serviceProvider) =>
        new(serviceProvider, _logger);

    private static IServiceProvider BuildProvider(params IEventHandler<EntityCreatedEvent<string>>[] handlers)
    {
        var services = new ServiceCollection();
        foreach (var handler in handlers)
        {
            services.AddSingleton<IEventHandler<EntityCreatedEvent<string>>>(handler);
        }
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PublishAsync_WithRegisteredHandler_HandlerReceivesEvent()
    {
        var handler = Substitute.For<IEventHandler<EntityCreatedEvent<string>>>();
        var provider = BuildProvider(handler);
        var bus = CreateBus(provider);
        var evt = new EntityCreatedEvent<string> { Entity = "test-entity" };

        await bus.PublishAsync(evt);

        await handler.Received(1).HandleAsync(evt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_HandlerThrows_CompletesWithoutThrowingAndLogsError()
    {
        var handler = Substitute.For<IEventHandler<EntityCreatedEvent<string>>>();
        handler.HandleAsync(Arg.Any<EntityCreatedEvent<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler failed"));
        var provider = BuildProvider(handler);
        var bus = CreateBus(provider);
        var evt = new EntityCreatedEvent<string> { Entity = "test-entity" };

        // Should not throw
        await bus.PublishAsync(evt);

        // Verify logger was called (LogError is an extension method, so we check the underlying Log call)
        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<InvalidOperationException>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task PublishAsync_MultipleHandlers_AllReceiveEvent()
    {
        var handler1 = Substitute.For<IEventHandler<EntityCreatedEvent<string>>>();
        var handler2 = Substitute.For<IEventHandler<EntityCreatedEvent<string>>>();
        var handler3 = Substitute.For<IEventHandler<EntityCreatedEvent<string>>>();
        var provider = BuildProvider(handler1, handler2, handler3);
        var bus = CreateBus(provider);
        var evt = new EntityCreatedEvent<string> { Entity = "test-entity" };

        await bus.PublishAsync(evt);

        await handler1.Received(1).HandleAsync(evt, Arg.Any<CancellationToken>());
        await handler2.Received(1).HandleAsync(evt, Arg.Any<CancellationToken>());
        await handler3.Received(1).HandleAsync(evt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_NoHandlers_CompletesWithoutError()
    {
        var provider = BuildProvider(); // no handlers
        var bus = CreateBus(provider);
        var evt = new EntityCreatedEvent<string> { Entity = "test-entity" };

        // Should complete without error
        await bus.PublishAsync(evt);
    }
}
