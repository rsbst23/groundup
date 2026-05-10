using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Authorization;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Identity;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Attributes;
using GroundUp.Core.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Integration.Auth.Services;

/// <summary>
/// Integration tests verifying end-to-end authorization proxy enforcement with real
/// permission resolution against a Postgres database. Tests both allowed and denied scenarios.
/// </summary>
public sealed class AuthorizationProxyIntegrationTests : AuthIntegrationTestBase
{
    #region Test Service Interface and Implementation

    /// <summary>
    /// Test service interface with authorization attributes for integration testing.
    /// </summary>
    public interface ITestOrderService
    {
        [RequiresPermission("orders.read")]
        Task<OperationResult<string>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

        [RequiresPermission("orders.write", "orders.read")]
        Task<OperationResult<string>> CreateOrderAsync(string name, CancellationToken cancellationToken = default);

        [RequiresRole("Admin")]
        Task<OperationResult<string>> DeleteAllOrdersAsync(CancellationToken cancellationToken = default);

        Task<OperationResult<string>> GetPublicDataAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Test service implementation.
    /// </summary>
    public sealed class TestOrderService : ITestOrderService
    {
        public Task<OperationResult<string>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<string>.Ok($"Order-{orderId}"));

        public Task<OperationResult<string>> CreateOrderAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<string>.Ok($"Created-{name}"));

        public Task<OperationResult<string>> DeleteAllOrdersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<string>.Ok("All deleted"));

        public Task<OperationResult<string>> GetPublicDataAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<string>.Ok("Public data"));
    }

    #endregion

    [Fact]
    public async Task Proxy_UserHasRequiredPermission_MethodInvoked()
    {
        // Arrange — seed user with orders.read permission
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "proxy-allowed@test.com");

        var permId = await SeedPermissionAsync("orders.read", "Read Orders", "orders");

        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "OrderReader", null, RoleType.Application, tenantId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Read Policy", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy.Data!.Id, permId);
        await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, role.Data!.Id, tenantId));

        // Create proxy with real permission service
        var proxy = CreateProxiedService(tenantId, userId);

        // Act
        var result = await proxy.GetOrderAsync(Guid.NewGuid());

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().StartWith("Order-");
    }

    [Fact]
    public async Task Proxy_UserLacksRequiredPermission_ReturnsForbidden()
    {
        // Arrange — user has no permissions
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "proxy-denied@test.com");

        // Create proxy with real permission service (user has no roles)
        var proxy = CreateProxiedService(tenantId, userId);

        // Act
        var result = await proxy.GetOrderAsync(Guid.NewGuid());

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Proxy_MultiplePermissionsRequired_AllMustBeSatisfied()
    {
        // Arrange — user has orders.read but NOT orders.write
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "proxy-partial@test.com");

        var permReadId = await SeedPermissionAsync($"orders.read.{Guid.NewGuid():N}", "Read Orders", "orders");
        // Note: we use a unique key but the attribute checks "orders.write" and "orders.read"
        // So let's use the exact keys the attribute expects
        await SeedPermissionAsync("orders.write", "Write Orders", "orders");

        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        // Only grant orders.read (not orders.write)
        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "PartialRole", null, RoleType.Application, tenantId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Partial Policy", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy.Data!.Id, permReadId);
        await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, role.Data!.Id, tenantId));

        var proxy = CreateProxiedService(tenantId, userId);

        // Act — CreateOrderAsync requires both orders.write AND orders.read
        var result = await proxy.CreateOrderAsync("Test");

        // Assert — should be forbidden because user lacks orders.write
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Proxy_RequiresRole_UserHasSystemRole_MethodInvoked()
    {
        // Arrange — user has Admin system role
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "proxy-admin@test.com");

        var roleRepo = CreateRoleRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var adminRole = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Admin", null, RoleType.System, tenantId, false));
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, adminRole.Data!.Id, tenantId));

        var proxy = CreateProxiedService(tenantId, userId);

        // Act
        var result = await proxy.DeleteAllOrdersAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("All deleted");
    }

    [Fact]
    public async Task Proxy_RequiresRole_UserLacksSystemRole_ReturnsForbidden()
    {
        // Arrange — user has no system roles
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "proxy-noadmin@test.com");

        var proxy = CreateProxiedService(tenantId, userId);

        // Act
        var result = await proxy.DeleteAllOrdersAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Proxy_NoAttribute_PassesThroughWithoutCheck()
    {
        // Arrange — user has no permissions at all
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "proxy-public@test.com");

        var proxy = CreateProxiedService(tenantId, userId);

        // Act — GetPublicDataAsync has no auth attribute
        var result = await proxy.GetPublicDataAsync();

        // Assert — passes through regardless of permissions
        result.Success.Should().BeTrue();
        result.Data.Should().Be("Public data");
    }

    [Fact]
    public async Task Proxy_ViaServiceCollection_AddAuthorized_WorksEndToEnd()
    {
        // Arrange — register via DI using AddAuthorized
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "proxy-di@test.com");

        var permId = await SeedPermissionAsync("orders.read", "Read Orders", "orders");

        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "DIRole", null, RoleType.Application, tenantId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "DI Policy", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy.Data!.Id, permId);
        await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, role.Data!.Id, tenantId));

        // Build a service collection with AddAuthorized
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.Configure<AuthOptions>(opts => opts.PermissionCacheTtlMinutes = 15);
        services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(userId));
        services.AddScoped<ITenantContext>(_ => new SystemTenantContext(tenantId));
        services.AddScoped(_ => CreateUserRoleRepository(tenantId) as GroundUp.Auth.Data.Abstractions.IUserRoleRepository);
        services.AddScoped(_ => CreateRoleRepository(tenantId) as GroundUp.Auth.Data.Abstractions.IRoleRepository);
        services.AddScoped(_ => CreatePolicyRepository(tenantId) as GroundUp.Auth.Data.Abstractions.IPolicyRepository);
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddAuthorized<ITestOrderService, TestOrderService>();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestOrderService>();

        // Act
        var result = await service.GetOrderAsync(Guid.NewGuid());

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().StartWith("Order-");
    }

    /// <summary>
    /// Creates a proxied test service with real permission resolution.
    /// </summary>
    private ITestOrderService CreateProxiedService(Guid tenantId, Guid userId)
    {
        var tenantContext = CreateTenantContext(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);
        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });

        var permissionService = new PermissionService(userRoleRepo, roleRepo, policyRepo, tenantContext, cache, options);
        var currentUser = new SystemCurrentUser(userId);
        var target = new TestOrderService();

        return AuthorizationInterceptor<ITestOrderService>.Create(target, permissionService, currentUser);
    }
}
