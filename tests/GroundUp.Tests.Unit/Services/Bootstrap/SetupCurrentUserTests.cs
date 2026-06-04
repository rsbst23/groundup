using FluentAssertions;
using GroundUp.Auth.Services.Identity;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Constants;
using GroundUp.Core.Entities;
using GroundUp.Data.Postgres;
using GroundUp.Data.Postgres.Interceptors;
using GroundUp.Services.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Tests.Unit.Services.Bootstrap;

/// <summary>
/// Unit tests for <see cref="SetupCurrentUser"/> and its integration with the
/// <see cref="AuditableInterceptor"/> to produce the "setup-wizard" sentinel
/// in CreatedBy/UpdatedBy fields during setup mode.
/// Requirements: Cross-Cutting 8
/// </summary>
public sealed class SetupCurrentUserTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly TestAuditDbContext _dbContext;

    public SetupCurrentUserTests()
    {
        // Default setup: register SetupCurrentUser as ICurrentUser (setup mode)
        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser>(_ => new SetupCurrentUser());

        _serviceProvider = services.BuildServiceProvider();

        var interceptor = new AuditableInterceptor(_serviceProvider);
        var options = new DbContextOptionsBuilder<TestAuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        _dbContext = new TestAuditDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }

    #region SetupCurrentUser Property Tests

    [Fact]
    public void UserId_ReturnsSentinelGuid()
    {
        // Arrange
        var sut = new SetupCurrentUser();

        // Act & Assert
        sut.UserId.Should().Be(BootstrapConstants.SetupSentinelUserId);
        sut.UserId.Should().Be(new Guid("00000000-0000-0000-0000-00000000ABCD"));
    }

    [Fact]
    public void Email_ReturnsNull()
    {
        // Arrange
        var sut = new SetupCurrentUser();

        // Act & Assert
        sut.Email.Should().BeNull();
    }

    [Fact]
    public void DisplayName_ReturnsSetupWizardSentinel()
    {
        // Arrange
        var sut = new SetupCurrentUser();

        // Act & Assert
        sut.DisplayName.Should().Be("setup-wizard");
        sut.DisplayName.Should().Be(SetupCurrentUser.Sentinel);
        sut.DisplayName.Should().Be(BootstrapConstants.SetupWizardSentinel);
    }

    [Fact]
    public void Sentinel_MatchesBootstrapConstants()
    {
        // Assert — SetupCurrentUser.Sentinel is the same as BootstrapConstants.SetupWizardSentinel
        SetupCurrentUser.Sentinel.Should().Be(BootstrapConstants.SetupWizardSentinel);
        SetupCurrentUser.SetupSentinelUserId.Should().Be(BootstrapConstants.SetupSentinelUserId);
    }

    #endregion

    #region AuditableInterceptor Integration — Setup Mode

    [Fact]
    public async Task SetupMode_CreatedBy_WritesSetupWizardSentinel()
    {
        // Arrange — SetupCurrentUser is registered (constructor default)
        var entity = new TestAuditableEntity { Id = Guid.NewGuid(), Name = "test" };
        _dbContext.TestEntities.Add(entity);

        // Act
        await _dbContext.SaveChangesAsync();

        // Assert
        entity.CreatedBy.Should().Be("setup-wizard");
    }

    [Fact]
    public async Task SetupMode_UpdatedBy_WritesSetupWizardSentinel()
    {
        // Arrange — insert first
        var entity = new TestAuditableEntity { Id = Guid.NewGuid(), Name = "original" };
        _dbContext.TestEntities.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act — modify
        entity.Name = "modified";
        _dbContext.Entry(entity).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();

        // Assert
        entity.UpdatedBy.Should().Be("setup-wizard");
    }

    [Fact]
    public async Task SetupMode_CreatedAt_IsPopulated()
    {
        // Arrange
        var entity = new TestAuditableEntity { Id = Guid.NewGuid(), Name = "test" };
        _dbContext.TestEntities.Add(entity);

        // Act
        await _dbContext.SaveChangesAsync();

        // Assert
        entity.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region AuditableInterceptor Integration — Complete Mode (JwtCurrentUser / normal user)

    [Fact]
    public async Task CompleteMode_CreatedBy_WritesUserGuidString()
    {
        // Arrange — build a separate DI container with a normal user
        var normalUserId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(normalUserId, "user@test.com", "Test User"));

        using var sp = services.BuildServiceProvider();
        var interceptor = new AuditableInterceptor(sp);
        var options = new DbContextOptionsBuilder<TestAuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        using var dbContext = new TestAuditDbContext(options);
        var entity = new TestAuditableEntity { Id = Guid.NewGuid(), Name = "test" };
        dbContext.TestEntities.Add(entity);

        // Act
        await dbContext.SaveChangesAsync();

        // Assert — should write the Guid string, NOT "setup-wizard"
        entity.CreatedBy.Should().Be(normalUserId.ToString());
        entity.CreatedBy.Should().NotBe("setup-wizard");
    }

    [Fact]
    public async Task CompleteMode_UpdatedBy_WritesUserGuidString()
    {
        // Arrange — build a separate DI container with a normal user
        var normalUserId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(normalUserId, "user@test.com", "Test User"));

        using var sp = services.BuildServiceProvider();
        var interceptor = new AuditableInterceptor(sp);
        var options = new DbContextOptionsBuilder<TestAuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        using var dbContext = new TestAuditDbContext(options);
        var entity = new TestAuditableEntity { Id = Guid.NewGuid(), Name = "original" };
        dbContext.TestEntities.Add(entity);
        await dbContext.SaveChangesAsync();

        // Act — modify
        entity.Name = "modified";
        dbContext.Entry(entity).State = EntityState.Modified;
        await dbContext.SaveChangesAsync();

        // Assert
        entity.UpdatedBy.Should().Be(normalUserId.ToString());
        entity.UpdatedBy.Should().NotBe("setup-wizard");
    }

    [Fact]
    public async Task CompleteMode_NoCurrentUser_CreatedByIsNull()
    {
        // Arrange — no ICurrentUser registered
        var services = new ServiceCollection();
        using var sp = services.BuildServiceProvider();
        var interceptor = new AuditableInterceptor(sp);
        var options = new DbContextOptionsBuilder<TestAuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        using var dbContext = new TestAuditDbContext(options);
        var entity = new TestAuditableEntity { Id = Guid.NewGuid(), Name = "test" };
        dbContext.TestEntities.Add(entity);

        // Act
        await dbContext.SaveChangesAsync();

        // Assert — no user registered, so CreatedBy should be null
        entity.CreatedBy.Should().BeNull();
    }

    #endregion

    #region Test Helpers

    /// <summary>
    /// Simple auditable entity for testing the interceptor behavior.
    /// </summary>
    public sealed class TestAuditableEntity : IAuditable
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    /// <summary>
    /// Minimal DbContext for testing the AuditableInterceptor with IAuditable entities.
    /// </summary>
    private sealed class TestAuditDbContext : DbContext
    {
        public TestAuditDbContext(DbContextOptions<TestAuditDbContext> options)
            : base(options) { }

        public DbSet<TestAuditableEntity> TestEntities => Set<TestAuditableEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TestAuditableEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
            });
        }
    }

    #endregion
}
