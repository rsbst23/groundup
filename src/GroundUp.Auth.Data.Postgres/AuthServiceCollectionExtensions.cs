using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Repositories;
using GroundUp.Core.Abstractions;
using GroundUp.Data.Postgres.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GroundUp.Auth.Data.Postgres;

/// <summary>
/// Extension methods for registering GroundUp Auth Postgres infrastructure
/// with the dependency injection container.
/// </summary>
public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GroundUp Auth Postgres infrastructure: AuthDbContext with Npgsql,
    /// audit and soft delete interceptors, and all auth repository implementations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The Postgres connection string for the auth database.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddGroundUpAuthPostgres(
        this IServiceCollection services,
        string connectionString)
    {
        // Register interceptors as singletons (idempotent — may already be registered by core)
        services.TryAddSingleton<AuditableInterceptor>();
        services.TryAddSingleton<SoftDeleteInterceptor>();

        // Register AuthDbContext with Npgsql and interceptors
        services.AddDbContext<AuthDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(
                sp.GetRequiredService<AuditableInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>());
        });

        // Register all repository implementations as scoped using factory pattern.
        // Repositories take DbContext as their constructor parameter, so we resolve
        // AuthDbContext specifically and pass it to avoid conflicts with other DbContexts.
        services.AddScoped<IUserRepository>(sp =>
            new UserRepository(sp.GetRequiredService<AuthDbContext>()));

        services.AddScoped<ITenantRepository>(sp =>
            new TenantRepository(
                sp.GetRequiredService<AuthDbContext>(),
                sp.GetRequiredService<ITenantContext>()));

        services.AddScoped<IRoleRepository>(sp =>
            new RoleRepository(
                sp.GetRequiredService<AuthDbContext>(),
                sp.GetRequiredService<ITenantContext>()));

        services.AddScoped<IPolicyRepository>(sp =>
            new PolicyRepository(
                sp.GetRequiredService<AuthDbContext>(),
                sp.GetRequiredService<ITenantContext>()));

        services.AddScoped<IPermissionRepository>(sp =>
            new PermissionRepository(sp.GetRequiredService<AuthDbContext>()));

        services.AddScoped<IUserTenantRepository>(sp =>
            new UserTenantRepository(
                sp.GetRequiredService<AuthDbContext>(),
                sp.GetRequiredService<ITenantContext>()));

        services.AddScoped<IUserRoleRepository>(sp =>
            new UserRoleRepository(
                sp.GetRequiredService<AuthDbContext>(),
                sp.GetRequiredService<ITenantContext>()));

        return services;
    }
}
