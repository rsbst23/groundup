using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Auth.Data.Postgres.Seeders;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Auth.Seeders;

public sealed class DefaultSystemRoleSeederTests : IDisposable
{
    private readonly AuthDbContext _dbContext;

    public DefaultSystemRoleSeederTests()
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
    public void Order_Is20()
    {
        var sut = new DefaultSystemRoleSeeder(_dbContext);
        Assert.Equal(20, sut.Order);
    }

    [Fact]
    public async Task SeedAsync_CreatesSuperAdminRole()
    {
        // Arrange — seed permissions first (dependency)
        await new DefaultPermissionSeeder(_dbContext).SeedAsync();
        var sut = new DefaultSystemRoleSeeder(_dbContext);

        // Act
        await sut.SeedAsync();

        // Assert
        var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Name == AuthRoleNames.SuperAdmin);
        Assert.NotNull(role);
        Assert.Equal(RoleType.System, role.RoleType);
        Assert.Equal(AuthRoleNames.SystemTenantId, role.TenantId);
        Assert.True(role.IsSystem);
    }

    [Fact]
    public async Task SeedAsync_CreatesFullAccessPolicy()
    {
        // Arrange
        await new DefaultPermissionSeeder(_dbContext).SeedAsync();
        var sut = new DefaultSystemRoleSeeder(_dbContext);

        // Act
        await sut.SeedAsync();

        // Assert
        var policy = await _dbContext.Policies.FirstOrDefaultAsync(p => p.Name == "FullAccess");
        Assert.NotNull(policy);
        Assert.Equal(AuthRoleNames.SystemTenantId, policy.TenantId);
    }

    [Fact]
    public async Task SeedAsync_LinksRoleToPolicy()
    {
        // Arrange
        await new DefaultPermissionSeeder(_dbContext).SeedAsync();
        var sut = new DefaultSystemRoleSeeder(_dbContext);

        // Act
        await sut.SeedAsync();

        // Assert
        var role = await _dbContext.Roles.FirstAsync(r => r.Name == AuthRoleNames.SuperAdmin);
        var policy = await _dbContext.Policies.FirstAsync(p => p.Name == "FullAccess");
        var link = await _dbContext.RolePolicies
            .FirstOrDefaultAsync(rp => rp.RoleId == role.Id && rp.PolicyId == policy.Id);
        Assert.NotNull(link);
    }

    [Fact]
    public async Task SeedAsync_LinksAllPermissionsToPolicy()
    {
        // Arrange
        await new DefaultPermissionSeeder(_dbContext).SeedAsync();
        var sut = new DefaultSystemRoleSeeder(_dbContext);

        // Act
        await sut.SeedAsync();

        // Assert
        var policy = await _dbContext.Policies.FirstAsync(p => p.Name == "FullAccess");
        var linkedCount = await _dbContext.PolicyPermissions
            .CountAsync(pp => pp.PolicyId == policy.Id);
        Assert.Equal(PermissionKeysAuth.All.Count, linkedCount);
    }

    [Fact]
    public async Task SeedAsync_Idempotent_RunningTwiceDoesNotDuplicate()
    {
        // Arrange
        await new DefaultPermissionSeeder(_dbContext).SeedAsync();
        var sut = new DefaultSystemRoleSeeder(_dbContext);

        // Act
        await sut.SeedAsync();
        await sut.SeedAsync();

        // Assert
        var roles = await _dbContext.Roles
            .Where(r => r.Name == AuthRoleNames.SuperAdmin)
            .ToListAsync();
        Assert.Single(roles);

        var policies = await _dbContext.Policies
            .Where(p => p.Name == "FullAccess")
            .ToListAsync();
        Assert.Single(policies);

        var policy = policies[0];
        var linkedCount = await _dbContext.PolicyPermissions
            .CountAsync(pp => pp.PolicyId == policy.Id);
        Assert.Equal(PermissionKeysAuth.All.Count, linkedCount);
    }

    [Fact]
    public async Task SeedAsync_NewPermissionsAdded_LinksThemOnRerun()
    {
        // Arrange — seed everything, then add a new permission manually
        await new DefaultPermissionSeeder(_dbContext).SeedAsync();
        var sut = new DefaultSystemRoleSeeder(_dbContext);
        await sut.SeedAsync();

        // Simulate a new permission being added (e.g., by a future version)
        _dbContext.Permissions.Add(new Permission
        {
            Key = "auth.future.action",
            Name = "Future Action",
            Description = "A permission added in a future version",
            Module = "auth"
        });
        await _dbContext.SaveChangesAsync();

        // Act — re-run the seeder
        await sut.SeedAsync();

        // Assert — the new permission should now be linked to the FullAccess policy
        var policy = await _dbContext.Policies.FirstAsync(p => p.Name == "FullAccess");
        var totalPermissions = await _dbContext.Permissions.CountAsync();
        var linkedCount = await _dbContext.PolicyPermissions
            .CountAsync(pp => pp.PolicyId == policy.Id);
        Assert.Equal(totalPermissions, linkedCount);
    }
}
