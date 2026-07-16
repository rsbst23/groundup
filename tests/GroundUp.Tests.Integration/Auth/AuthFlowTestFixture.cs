using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Auth.Services;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Data.Postgres.Interceptors;
using GroundUp.Sample.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// WebApplicationFactory-based fixture for Phase 10C auth flow integration tests.
/// Provides:
/// <list type="bullet">
///   <item>Full auth middleware pipeline via the Sample app (UseGroundUpAuth with all 6 middleware)</item>
///   <item>Testcontainers Postgres with migrations applied</item>
///   <item>Controllable <see cref="FakeTimeProvider"/> for refresh/session-cap tests</item>
///   <item>Mocked <see cref="IIdentityProviderService"/> for simulating Keycloak callback responses</item>
///   <item>Test JWT signing key for token validation</item>
/// </list>
/// </summary>
public sealed class AuthFlowTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    /// <summary>
    /// The controllable time provider injected into the application for time-sensitive tests
    /// (token refresh, session lifetime cap).
    /// </summary>
    public FakeTimeProvider TimeProvider { get; } = new();

    /// <summary>
    /// The mocked identity provider service. Tests configure return values on this mock
    /// to simulate Keycloak code exchange and userinfo responses without a live Keycloak instance.
    /// </summary>
    public IIdentityProviderService MockIdentityProviderService { get; } = Substitute.For<IIdentityProviderService>();

    /// <summary>
    /// The JWT signing key used for test token generation and validation.
    /// </summary>
    public const string TestSigningKey = "integration-test-signing-key-that-is-at-least-32-bytes-long!";

    /// <summary>
    /// The default domain used for host-based tenant resolution in tests.
    /// </summary>
    public const string TestDefaultDomain = "testapp.local";

    /// <summary>
    /// The Postgres connection string for direct database access in test assertions.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Apply migrations directly BEFORE the host starts,
        // so that DataSeederRunner finds existing tables when it runs.
        var authOptions = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        await using (var authCtx = new AuthDbContext(authOptions))
        {
            await authCtx.Database.MigrateAsync();
        }

        var sampleOptions = new DbContextOptionsBuilder<SampleDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        await using (var sampleCtx = new SampleDbContext(sampleOptions))
        {
            await sampleCtx.Database.MigrateAsync();
        }

        // Seed the default domain setting so that HostTenantResolver can resolve
        // subdomains in tests. This must happen before any request touches ISettingsService
        // (whose cache would otherwise cache a "not found" result).
        await SeedDefaultDomainSettingAsync(_container.GetConnectionString());

        // Force host build — now the DataSeederRunner will find the tables
        _ = Services;
    }

    public new async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("GroundUp:Auth:JwtSigningKey", TestSigningKey);
        builder.UseSetting("GroundUp:Auth:CleanupIntervalMinutes", "60");
        builder.UseSetting("GroundUp:Auth:TokenExpirationMinutes", "60");
        builder.UseSetting("GroundUp:Auth:AbsoluteSessionLifetimeMinutes", "480");
        builder.UseSetting("GroundUp:Auth:CookieSecure", "false"); // Allow HTTP in test server
        builder.UseSetting("GroundUp:Auth:StateCookieName", "AuthState");
        builder.UseSetting("GroundUp:Auth:CallbackPath", "/auth/callback");

        builder.ConfigureTestServices(services =>
        {
            // Remove hosted services that run before migrations are applied in tests.
            // The DataSeederRunner queries auth tables that don't exist until MigrateAsync() runs.
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

            // Replace DbContext registrations with Testcontainers Postgres
            services.RemoveAll<DbContextOptions<SampleDbContext>>();
            services.RemoveAll<DbContextOptions<AuthDbContext>>();

            services.AddDbContext<SampleDbContext>((sp, options) =>
            {
                options.UseNpgsql(_container.GetConnectionString());
                options.AddInterceptors(
                    sp.GetRequiredService<AuditableInterceptor>(),
                    sp.GetRequiredService<SoftDeleteInterceptor>());
            });

            services.AddDbContext<AuthDbContext>(options =>
            {
                options.UseNpgsql(_container.GetConnectionString());
            });

            // Replace IIdentityProviderService with a mock so we can simulate
            // Keycloak code exchange and userinfo without a live Keycloak container.
            services.RemoveAll<IIdentityProviderService>();
            services.AddSingleton(MockIdentityProviderService);

            // Register IUnitOfWork backed by AuthDbContext for transactional flow handlers
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork, AuthDbContextUnitOfWork>();

            // Replace the default scope chain provider with one that always includes the
            // System level. The DefaultScopeChainProvider returns empty when TenantId is
            // Guid.Empty (which is the case during auth callbacks), preventing system-level
            // settings like auth.application.default-domain from being resolved.
            services.RemoveAll<GroundUp.Core.Abstractions.IScopeChainProvider>();
            services.AddScoped<GroundUp.Core.Abstractions.IScopeChainProvider, AuthFlowTestScopeChainProvider>();

            // Register IHttpClientFactory required by AuthController for Keycloak logout
            services.AddHttpClient();

            // Inject controllable TimeProvider for refresh/session tests
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(TimeProvider);
        });
    }

    /// <summary>
    /// Creates an <see cref="AuthDbContext"/> for direct database operations in tests.
    /// </summary>
    public AuthDbContext CreateAuthDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AuthDbContext(options);
    }

    /// <summary>
    /// Creates an HTTP client that includes cookies in requests (for auth cookie testing).
    /// </summary>
    public HttpClient CreateAuthClient()
    {
        var handler = Server.CreateHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        return client;
    }

    /// <summary>
    /// Generates a test JWT token with the specified claims, suitable for setting as
    /// the authentication cookie in test requests.
    /// </summary>
    /// <param name="userId">The user ID (sub claim).</param>
    /// <param name="tenantId">The tenant ID (tid claim). Null for pending-selection tokens.</param>
    /// <param name="email">The email claim.</param>
    /// <param name="additionalClaims">Optional additional claims.</param>
    /// <returns>A signed JWT token string.</returns>
    public string GenerateTestToken(
        Guid userId,
        Guid? tenantId = null,
        string email = "test@example.com",
        IEnumerable<Claim>? additionalClaims = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("email", email),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (tenantId.HasValue)
        {
            claims.Add(new Claim("tid", tenantId.Value.ToString()));
        }

        if (additionalClaims is not null)
        {
            claims.AddRange(additionalClaims);
        }

        var token = new JwtSecurityToken(
            issuer: "GroundUp",
            audience: "GroundUp",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Generates a fake Keycloak-style id_token containing the specified nonce claim.
    /// Used for testing nonce validation in flow handlers.
    /// </summary>
    /// <param name="sub">The subject claim (Keycloak user ID).</param>
    /// <param name="nonce">The nonce claim value.</param>
    /// <param name="email">The email claim.</param>
    /// <returns>A JWT string with the specified claims (unsigned — validation is mocked).</returns>
    public static string GenerateFakeIdToken(string sub, string nonce, string email = "user@example.com")
    {
        var claims = new List<Claim>
        {
            new("sub", sub),
            new("nonce", nonce),
            new("email", email),
            new("name", "Test User")
        };

        // Create a minimal JWT structure (unsigned — the IDP service is mocked)
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("fake-key-for-id-token-testing-minimum-32-bytes!"));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "http://keycloak/realms/groundup",
            audience: "groundup-app",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Generates a cryptographically random state token matching the format used by AuthUrlBuilderService.
    /// </summary>
    public static string GenerateStateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncoder.Encode(bytes);
    }

    /// <summary>
    /// Generates a cryptographically random nonce matching the format used by AuthUrlBuilderService.
    /// </summary>
    public static string GenerateNonce()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncoder.Encode(bytes);
    }

    /// <summary>
    /// Generates a PKCE code verifier (43–128 URL-safe characters).
    /// </summary>
    public static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncoder.Encode(bytes); // yields 43 chars
    }

    /// <summary>
    /// Seeds the <c>auth.application.default-domain</c> setting into the database
    /// during fixture initialization so the HostTenantResolver works for all tests.
    /// </summary>
    private static async Task SeedDefaultDomainSettingAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var dbContext = new SampleDbContext(options);

        // Ensure a System level exists
        var systemLevel = await dbContext.Set<GroundUp.Core.Entities.Settings.SettingLevel>()
            .FirstOrDefaultAsync(l => l.Name == "System");

        if (systemLevel is null)
        {
            systemLevel = new GroundUp.Core.Entities.Settings.SettingLevel
            {
                Name = "System",
                ParentId = null,
                DisplayOrder = 0,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Set<GroundUp.Core.Entities.Settings.SettingLevel>().Add(systemLevel);
            await dbContext.SaveChangesAsync();
        }

        // Ensure the definition exists
        const string settingKey = "auth.application.default-domain";
        var definition = await dbContext.Set<GroundUp.Core.Entities.Settings.SettingDefinition>()
            .FirstOrDefaultAsync(d => d.Key == settingKey);

        if (definition is null)
        {
            definition = new GroundUp.Core.Entities.Settings.SettingDefinition
            {
                Key = settingKey,
                DataType = GroundUp.Core.Enums.SettingDataType.String,
                DefaultValue = null,
                DisplayName = "Default Domain",
                Description = "The default domain for host-based tenant resolution",
                DisplayOrder = 0,
                IsVisible = true,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Set<GroundUp.Core.Entities.Settings.SettingDefinition>().Add(definition);

            var defLevel = new GroundUp.Core.Entities.Settings.SettingDefinitionLevel
            {
                SettingDefinitionId = definition.Id,
                SettingLevelId = systemLevel.Id
            };
            dbContext.Set<GroundUp.Core.Entities.Settings.SettingDefinitionLevel>().Add(defLevel);
            await dbContext.SaveChangesAsync();
        }

        // Set the value at system level
        var existingValue = await dbContext.Set<GroundUp.Core.Entities.Settings.SettingValue>()
            .FirstOrDefaultAsync(v =>
                v.SettingDefinitionId == definition.Id &&
                v.LevelId == systemLevel.Id &&
                v.ScopeId == null);

        if (existingValue is null)
        {
            var settingValue = new GroundUp.Core.Entities.Settings.SettingValue
            {
                SettingDefinitionId = definition.Id,
                LevelId = systemLevel.Id,
                ScopeId = null,
                Value = TestDefaultDomain,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Set<GroundUp.Core.Entities.Settings.SettingValue>().Add(settingValue);
            await dbContext.SaveChangesAsync();
        }
    }
}

/// <summary>
/// Simple EF Core-based <see cref="IUnitOfWork"/> implementation for integration tests.
/// Wraps the operation in a database transaction using <see cref="AuthDbContext"/>.
/// </summary>
internal sealed class AuthDbContextUnitOfWork : IUnitOfWork
{
    private readonly AuthDbContext _context;

    public AuthDbContextUnitOfWork(AuthDbContext context)
    {
        _context = context;
    }

    public async Task<OperationResult> ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        // Each repository AddAsync already calls SaveChangesAsync internally,
        // so we just run the operation and return success.
        // For true atomicity, a proper UoW would defer saves.
        try
        {
            await operation(cancellationToken);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (
            ex.Message.Contains("23505") ||
            ex.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
            (ex.InnerException is not null && (
                ex.InnerException.Message.Contains("23505") ||
                ex.InnerException.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
                ex.InnerException.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))))
        {
            return OperationResult.Fail("Unique constraint violation", 409);
        }
    }
}

/// <summary>
/// Test scope chain provider that always includes the System level for setting resolution.
/// The default provider returns empty when TenantId is Guid.Empty (during auth callbacks),
/// preventing system-level settings from being resolved. This provider ensures
/// auth.application.default-domain (and similar settings) are accessible during tests.
/// </summary>
internal sealed class AuthFlowTestScopeChainProvider : GroundUp.Core.Abstractions.IScopeChainProvider
{
    private readonly GroundUp.Data.Postgres.GroundUpDbContext _dbContext;

    public AuthFlowTestScopeChainProvider(GroundUp.Data.Postgres.GroundUpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<GroundUp.Core.Models.SettingScopeEntry>> GetScopeChainAsync(
        CancellationToken cancellationToken = default)
    {
        var systemLevel = await _dbContext.Set<GroundUp.Core.Entities.Settings.SettingLevel>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Name == "System", cancellationToken);

        if (systemLevel is null)
        {
            return Array.Empty<GroundUp.Core.Models.SettingScopeEntry>();
        }

        // Return system level with null scope (system-wide setting)
        return new[] { new GroundUp.Core.Models.SettingScopeEntry(systemLevel.Id, null) };
    }
}
