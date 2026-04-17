using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Events;

namespace GroundUp.Tests.Unit.Events;

/// <summary>
/// Property-based tests for <see cref="InProcessEventBus"/>.
/// Validates correctness properties from the Phase 2 design document.
/// </summary>
public sealed class InProcessEventBusPropertyTests
{
    /// <summary>
    /// Property 2: All registered handlers are invoked.
    /// For any N handlers (0-5), PublishAsync invokes HandleAsync on every handler exactly once.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AllRegisteredHandlers_AreInvoked(byte handlerCountRaw)
    {
        // Clamp to 0-5 to keep tests fast
        var handlerCount = handlerCountRaw % 6;
        var logger = Substitute.For<ILogger<InProcessEventBus>>();
        var handlers = Enumerable.Range(0, handlerCount)
            .Select(_ => Substitute.For<IEventHandler<EntityCreatedEvent<string>>>())
            .ToList();

        var services = new ServiceCollection();
        foreach (var h in handlers)
            services.AddSingleton<IEventHandler<EntityCreatedEvent<string>>>(h);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var bus = new InProcessEventBus(provider, logger);
        var evt = new EntityCreatedEvent<string> { Entity = "test" };

        bus.PublishAsync(evt).GetAwaiter().GetResult();

        var allInvoked = handlers.All(h =>
        {
            h.Received(1).HandleAsync(evt, Arg.Any<CancellationToken>());
            return true;
        });

        return allInvoked.ToProperty();
    }

    /// <summary>
    /// Property 3: Handler fault isolation.
    /// For any set of handlers where the first throws, all remaining handlers
    /// still receive the event and PublishAsync completes without throwing.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property HandlerFaultIsolation_AllHandlersInvokedDespiteFailures(byte extraHandlerCountRaw)
    {
        // 1 failing handler + 0-4 succeeding handlers
        var extraCount = extraHandlerCountRaw % 5;
        var logger = Substitute.For<ILogger<InProcessEventBus>>();

        var failingHandler = Substitute.For<IEventHandler<EntityCreatedEvent<string>>>();
        failingHandler.HandleAsync(Arg.Any<EntityCreatedEvent<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var succeedingHandlers = Enumerable.Range(0, extraCount)
            .Select(_ => Substitute.For<IEventHandler<EntityCreatedEvent<string>>>())
            .ToList();

        var services = new ServiceCollection();
        services.AddSingleton<IEventHandler<EntityCreatedEvent<string>>>(failingHandler);
        foreach (var h in succeedingHandlers)
            services.AddSingleton<IEventHandler<EntityCreatedEvent<string>>>(h);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var bus = new InProcessEventBus(provider, logger);
        var evt = new EntityCreatedEvent<string> { Entity = "test" };

        // Should not throw
        bus.PublishAsync(evt).GetAwaiter().GetResult();

        // All succeeding handlers should have been invoked
        var allInvoked = succeedingHandlers.All(h =>
        {
            h.Received(1).HandleAsync(evt, Arg.Any<CancellationToken>());
            return true;
        });

        return allInvoked.ToProperty();
    }
}
