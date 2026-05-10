using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Authorization;
using GroundUp.Auth.Services.Identity;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Tests.Unit.Auth.DI;

public sealed class AuthServiceRegistrationTests
{

    [Fact]
    public void AddGroundUpAuth_RegistersIPermissionService()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        services.AddGroundUpAuth(configuration);

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPermissionService));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddGroundUpAuth_RegistersICurrentUser_AsJwtCurrentUser()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        services.AddGroundUpAuth(configuration);

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICurrentUser));
        Assert.NotNull(descriptor);
        Assert.Equal(typeof(JwtCurrentUser), descriptor.ImplementationType);
    }

    [Fact]
    public void AddGroundUpAuth_RegistersITenantContext_AsJwtTenantContext()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        services.AddGroundUpAuth(configuration);

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ITenantContext));
        Assert.NotNull(descriptor);
        Assert.Equal(typeof(JwtTenantContext), descriptor.ImplementationType);
    }

    [Fact]
    public void AddGroundUpAuth_RegistersUserRoleChangedEventHandlers()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        services.AddGroundUpAuth(configuration);

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler<EntityCreatedEvent<UserRoleDto>>));
        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler<EntityDeletedEvent<UserRoleDto>>));
    }

    [Fact]
    public void AddGroundUpAuth_RegistersRolePolicyChangedEventHandlers()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        services.AddGroundUpAuth(configuration);

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler<EntityCreatedEvent<RolePolicyDto>>));
        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler<EntityDeletedEvent<RolePolicyDto>>));
    }

    [Fact]
    public void AddGroundUpAuth_RegistersPolicyPermissionChangedEventHandlers()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        services.AddGroundUpAuth(configuration);

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler<EntityCreatedEvent<PolicyPermissionDto>>));
        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler<EntityDeletedEvent<PolicyPermissionDto>>));
    }

    [Fact]
    public void AddAuthorized_WrapsServiceWithProxy()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        services.AddGroundUpAuth(configuration);

        // Register mock repositories needed by PermissionService
        services.AddScoped<GroundUp.Auth.Data.Abstractions.IUserRoleRepository>(_ =>
            NSubstitute.Substitute.For<GroundUp.Auth.Data.Abstractions.IUserRoleRepository>());
        services.AddScoped<GroundUp.Auth.Data.Abstractions.IRoleRepository>(_ =>
            NSubstitute.Substitute.For<GroundUp.Auth.Data.Abstractions.IRoleRepository>());
        services.AddScoped<GroundUp.Auth.Data.Abstractions.IPolicyRepository>(_ =>
            NSubstitute.Substitute.For<GroundUp.Auth.Data.Abstractions.IPolicyRepository>());

        services.AddAuthorized<ITestAuthService, TestAuthService>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var resolved = scope.ServiceProvider.GetRequiredService<ITestAuthService>();

        // Assert — the resolved instance should be a proxy, not the direct implementation
        Assert.NotNull(resolved);
        Assert.IsNotType<TestAuthService>(resolved);
    }

    // --- Helpers ---

    public interface ITestAuthService
    {
        Task<OperationResult<string>> GetDataAsync();
    }

    public class TestAuthService : ITestAuthService
    {
        public Task<OperationResult<string>> GetDataAsync()
            => Task.FromResult(OperationResult<string>.Ok("data"));
    }
}
