using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Auth.Data.Postgres.Seeders;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Auth.Seeders;

public sealed class DefaultPermissionSeederTests : IDisposable
{
    private readonly AuthDbContext _dbContext;

    public DefaultPermissionSeederTests()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new AuthDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public void Order_Is10()
    {
        var sut = new DefaultPermissionSeeder(_dbContext);
        Assert.Equal(10, sut.Order);
    }

    [Fact]
    public async Task SeedAsync_EmptyDatabase_CreatesAllPermissions()
    {
        // Arrange
        var sut = new DefaultPermissionSeeder(_dbContext);

        // Act
        await sut.SeedAsync();

        // Assert
        var permissions = await _dbContext.Permissions.ToListAsync();
        Assert.Equal(PermissionKeysAuth.All.Count, permissions.Count);
    }

    [Fact]
    public async Task SeedAsync_AllPermissionsHaveCorrectKeys()
    {
        // Arrange
        var sut = new DefaultPermissionSeeder(_dbContext);

        // Act
        await sut.SeedAsync();

        // Assert
        var keys = await _dbContext.Permissions.Select(p => p.Key).ToListAsync();
        foreach (var expected in PermissionKeysAuth.All)
        {
            Assert.Contains(expected.Key, keys);
        }
    }

    [Fact]
    public async Task SeedAsync_SetsModuleCorrectly()
    {
        // Arrange
        var sut = new DefaultPermissionSeeder(_dbContext);

        // Act
        await sut.SeedAsync();

        // Assert
        var authPermissions = await _dbContext.Permissions
            .Where(p => p.Module == "auth")
            .ToListAsync();
        Assert.Equal(12, authPermissions.Count); // 12 auth.* permissions

        var settingsPermissions = await _dbContext.Permissions
            .Where(p => p.Module == "settings")
            .ToListAsync();
        Assert.Equal(3, settingsPermissions.Count); // 3 settings.* permissions
    }

    [Fact]
    public async Task SeedAsync_Idempotent_RunningTwiceDoesNotDuplicate()
    {
        // Arrange
        var sut = new DefaultPermissionSeeder(_dbContext);

        // Act
        await sut.SeedAsync();
        await sut.SeedAsync();

        // Assert
        var permissions = await _dbContext.Permissions.ToListAsync();
        Assert.Equal(PermissionKeysAuth.All.Count, permissions.Count);
    }

    [Fact]
    public async Task SeedAsync_PartiallySeeded_OnlyAddsNew()
    {
        // Arrange — pre-seed one permission
        _dbContext.Permissions.Add(new Permission
        {
            Key = PermissionKeysAuth.UsersRead,
            Name = "Read Users",
            Description = "Existing",
            Module = "auth"
        });
        await _dbContext.SaveChangesAsync();

        var sut = new DefaultPermissionSeeder(_dbContext);

        // Act
        await sut.SeedAsync();

        // Assert — total should be all permissions, not all + 1
        var permissions = await _dbContext.Permissions.ToListAsync();
        Assert.Equal(PermissionKeysAuth.All.Count, permissions.Count);

        // The pre-existing one should not be overwritten
        var existing = await _dbContext.Permissions.FirstAsync(p => p.Key == PermissionKeysAuth.UsersRead);
        Assert.Equal("Existing", existing.Description);
    }
}
