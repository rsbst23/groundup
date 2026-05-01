using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Unit tests for <see cref="DefaultScopeChainProvider"/>.
/// </summary>
public sealed class DefaultScopeChainProviderTests : IDisposable
{
    private readonly SettingsTestFixture _fixture;
    private readonly TestSettingsDbContext _context;

    public DefaultScopeChainProviderTests()
    {
        _fixture = new SettingsTestFixture();
        _context = _fixture.CreateContext();
    }

    [Fact]
    public async Task GetScopeChainAsync_TenantIdSetAndTenantLevelExists_ReturnsSingleEntryScopeChain()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var tenantLevel = new SettingLevel
        {
            Id = Guid.NewGuid(),
            Name = "Tenant",
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow
        };
        _context.Set<SettingLevel>().Add(tenantLevel);
        await _context.SaveChangesAsync();

        var provider = new DefaultScopeChainProvider(tenantContext, _context);

        // Act
        var result = await provider.GetScopeChainAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].LevelId.Should().Be(tenantLevel.Id);
        result[0].ScopeId.Should().Be(tenantId);
    }

    [Fact]
    public async Task GetScopeChainAsync_TenantIdIsEmpty_ReturnsEmptyList()
    {
        // Arrange
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(Guid.Empty);

        var provider = new DefaultScopeChainProvider(tenantContext, _context);

        // Act
        var result = await provider.GetScopeChainAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetScopeChainAsync_TenantLevelNotFound_ReturnsEmptyList()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        // Seed a level with a different name (not "Tenant")
        var systemLevel = new SettingLevel
        {
            Id = Guid.NewGuid(),
            Name = "System",
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow
        };
        _context.Set<SettingLevel>().Add(systemLevel);
        await _context.SaveChangesAsync();

        var provider = new DefaultScopeChainProvider(tenantContext, _context);

        // Act
        var result = await provider.GetScopeChainAsync();

        // Assert
        result.Should().BeEmpty();
    }

    public void Dispose()
    {
        _context.Dispose();
        _fixture.Dispose();
    }
}
