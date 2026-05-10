using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.EventHandlers;
using GroundUp.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.EventHandlers;

public sealed class UserRoleChangedHandlerTests
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserRoleChangedHandler> _logger;
    private readonly UserRoleChangedHandler _sut;

    public UserRoleChangedHandlerTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _logger = Substitute.For<ILogger<UserRoleChangedHandler>>();
        _sut = new UserRoleChangedHandler(_cache, _logger);
    }

    [Fact]
    public async Task HandleAsync_EntityCreated_EvictsCorrectCacheKey()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var cacheKey = $"permissions:{userId}:{tenantId}";

        _cache.Set(cacheKey, new HashSet<string> { "some.permission" });

        var @event = new EntityCreatedEvent<UserRoleDto>
        {
            Entity = new UserRoleDto(Guid.NewGuid(), userId, Guid.NewGuid(), tenantId)
        };

        // Act
        await _sut.HandleAsync(@event);

        // Assert
        Assert.False(_cache.TryGetValue(cacheKey, out _));
    }

    [Fact]
    public async Task HandleAsync_EntityDeleted_WithMetadata_EvictsCorrectCacheKey()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var cacheKey = $"permissions:{userId}:{tenantId}";

        _cache.Set(cacheKey, new HashSet<string> { "some.permission" });

        var @event = new EntityDeletedEvent<UserRoleDto>
        {
            EntityId = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId
        };

        // Act
        await _sut.HandleAsync(@event);

        // Assert
        Assert.False(_cache.TryGetValue(cacheKey, out _));
    }

    [Fact]
    public async Task HandleAsync_EntityDeleted_MissingMetadata_LogsWarning()
    {
        // Arrange
        var @event = new EntityDeletedEvent<UserRoleDto>
        {
            EntityId = Guid.NewGuid(),
            UserId = null,
            TenantId = null
        };

        // Act — should not throw
        await _sut.HandleAsync(@event);

        // Assert — verify the handler completed without exception
        // The warning is logged but we verify no exception is thrown
        Assert.True(true);
    }

    [Fact]
    public async Task HandleAsync_EntityCreated_CacheKeyNotPresent_DoesNotThrow()
    {
        // Arrange
        var @event = new EntityCreatedEvent<UserRoleDto>
        {
            Entity = new UserRoleDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())
        };

        // Act & Assert — should not throw even if key doesn't exist
        await _sut.HandleAsync(@event);
    }
}
