using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.EventHandlers;
using GroundUp.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.EventHandlers;

public sealed class RolePolicyChangedHandlerTests
{
    private readonly ILogger<RolePolicyChangedHandler> _logger;
    private readonly RolePolicyChangedHandler _sut;

    public RolePolicyChangedHandlerTests()
    {
        _logger = Substitute.For<ILogger<RolePolicyChangedHandler>>();
        _sut = new RolePolicyChangedHandler(_logger);
    }

    [Fact]
    public async Task HandleAsync_EntityCreated_LogsInformationWithoutException()
    {
        // Arrange
        var @event = new EntityCreatedEvent<RolePolicyDto>
        {
            Entity = new RolePolicyDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())
        };

        // Act & Assert — should complete without throwing
        await _sut.HandleAsync(@event);
    }

    [Fact]
    public async Task HandleAsync_EntityDeleted_LogsInformationWithoutException()
    {
        // Arrange
        var @event = new EntityDeletedEvent<RolePolicyDto>
        {
            EntityId = Guid.NewGuid()
        };

        // Act & Assert — should complete without throwing
        await _sut.HandleAsync(@event);
    }

    [Fact]
    public async Task HandleAsync_EntityCreated_ReturnsCompletedTask()
    {
        // Arrange
        var @event = new EntityCreatedEvent<RolePolicyDto>
        {
            Entity = new RolePolicyDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())
        };

        // Act
        var task = _sut.HandleAsync(@event);

        // Assert
        Assert.True(task.IsCompleted);
        await task; // ensure no exception
    }
}
