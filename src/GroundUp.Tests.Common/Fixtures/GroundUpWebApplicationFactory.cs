using GroundUp.Data.Postgres;
using GroundUp.Data.Postgres.Interceptors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace GroundUp.Tests.Common.Fixtures;

/// <summary>
/// Generic abstract <see cref="WebApplicationFactory{TEntryPoint}"/> that starts a
/// Testcontainers Postgres instance, replaces the <typeparamref name="TContext"/>
/// registration with one pointing to the container, and runs EF Core migrations.
/// <para>
/// Consuming applications subclass this with their own entry point and DbContext types.
/// For example:
/// <code>
/// public sealed class MyApiFactory : GroundUpWebApplicationFactory&lt;Program, MyDbContext&gt; { }
/// </code>
/// </para>
/// <para>
/// The factory implements <see cref="IAsyncLifetime"/> so that xUnit manages the
/// Testcontainers lifecycle per test collection via <c>ICollectionFixture</c>.
/// </para>
/// </summary>
/// <typeparam name="TEntryPoint">
/// The consuming application's entry point class (typically <c>Program</c>).
/// Apps using top-level statements must add <c>public partial class Program { }</c>
/// at the bottom of <c>Program.cs</c> to make the generated class accessible.
/// </typeparam>
/// <typeparam name="TContext">
/// The consuming application's DbContext type. Must inherit from <see cref="GroundUpDbContext"/>.
/// </typeparam>
public abstract class GroundUpWebApplicationFactory<TEntryPoint, TContext>
    : WebApplicationFactory<TEntryPoint>, IAsyncLifetime
    where TEntryPoint : class
    where TContext : GroundUpDbContext
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    /// <summary>
    /// Starts the Testcontainers Postgres container, forces host creation
    /// (which triggers <see cref="ConfigureWebHost"/>), and runs EF Core migrations.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Accessing Services forces the host to build, which triggers ConfigureWebHost.
        // This ensures the DbContext is registered with the Testcontainers connection string
        // before we attempt to run migrations.
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// Configures the test host by replacing the <typeparamref name="TContext"/> registration
    /// with one pointing to the Testcontainers Postgres connection string, registering the
    /// <see cref="TestAuthHandler"/>, and calling the <see cref="ConfigureTestServices"/> hook.
    /// <para>
    /// This method removes only <see cref="DbContextOptions{TContext}"/> and re-registers
    /// the DbContext directly (not via <c>AddGroundUpPostgres</c>) to avoid double-registering
    /// interceptors and hosted services that the consuming app already registered.
    /// </para>
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // 1. Remove only the DbContextOptions<TContext> registration
            services.RemoveAll<DbContextOptions<TContext>>();

            // 2. Re-register TContext with the Testcontainers connection string,
            //    reusing the already-registered interceptors from the service provider
            services.AddDbContext<TContext>((sp, options) =>
            {
                options.UseNpgsql(_container.GetConnectionString());
                options.AddInterceptors(
                    sp.GetRequiredService<AuditableInterceptor>(),
                    sp.GetRequiredService<SoftDeleteInterceptor>());
            });

            // 3. Register TestAuthHandler as an authentication scheme (not enforced as default)
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // 4. Allow subclasses to add additional test service registrations
            ConfigureTestServices(services);
        });
    }

    /// <summary>
    /// Override this method in subclasses to register additional services
    /// specific to the consuming application's test needs.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    /// <summary>
    /// Stops and disposes the Testcontainers Postgres container.
    /// </summary>
    public new async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}
