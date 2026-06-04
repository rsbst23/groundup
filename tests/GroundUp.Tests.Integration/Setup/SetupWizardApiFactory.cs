using GroundUp.Api.Authentication;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Configuration;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Data;
using GroundUp.Services.Bootstrap;
using GroundUp.Services.Security;
using GroundUp.Services.Setup;
using GroundUp.Services.Setup.Keycloak;
using GroundUp.Tests.Common.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// WebApplicationFactory for setup wizard integration tests.
/// Registers the full setup wizard service stack with mocked external dependencies
/// (Keycloak, identity provider admin) and a controllable bootstrap state.
/// Uses a real Postgres database for settings persistence.
/// </summary>
public sealed class SetupWizardApiFactory : GroundUpWebApplicationFactory<Program, SampleDbContext>
{
    /// <summary>
    /// The bootstrap admin token used for authenticating setup requests in tests.
    /// </summary>
    public const string TestBootstrapToken = "test-bootstrap-admin-token-that-is-at-least-32-chars-long";

    /// <summary>
    /// A deterministic 32-byte test master key.
    /// </summary>
    private static readonly byte[] TestMasterKey = Convert.FromBase64String(
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

    /// <summary>
    /// Indicates whether the "system" setting level has been seeded.
    /// </summary>
    private static bool _systemLevelSeeded;

    /// <summary>
    /// Ensures the "system" setting level exists in the database.
    /// Called by test classes in their InitializeAsync to seed required data.
    /// Thread-safe and idempotent.
    /// </summary>
    public async Task EnsureSystemLevelSeededAsync()
    {
        if (_systemLevelSeeded) return;

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SampleDbContext>();

        var exists = await dbContext.Set<SettingLevel>()
            .AnyAsync(l => l.Name == "system");

        if (!exists)
        {
            dbContext.Set<SettingLevel>().Add(new SettingLevel
            {
                Id = Guid.NewGuid(),
                Name = "system",
                DisplayOrder = 0,
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        _systemLevelSeeded = true;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GroundUp:BootstrapAdminToken"] = TestBootstrapToken,
                ["GroundUp:MasterKey"] = Convert.ToBase64String(TestMasterKey),
                ["GroundUp:Auth:JwtSigningKey"] = "integration-test-signing-key-must-be-at-least-32-bytes-long",
                ["GroundUp:Setup:MaxRequestBodyBytes"] = "65536",
                ["GroundUp:SetupTransactionLog:MaxRowCount"] = "100"
            });
        });

        base.ConfigureWebHost(builder);
    }

    /// <inheritdoc />
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Remove hosted services that would interfere with tests
        services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

        // Replace IBootstrapStateService with a test-controllable implementation
        services.RemoveAll<IBootstrapStateService>();
        services.AddScoped<IBootstrapStateService, SetupWizardTestBootstrapStateService>();

        // Register IMasterKeyProvider with a test key
        services.RemoveAll<IMasterKeyProvider>();
        services.AddSingleton<IMasterKeyProvider>(new TestMasterKeyProvider(TestMasterKey));

        // Register ISettingEncryptionProvider
        services.RemoveAll<ISettingEncryptionProvider>();
        services.AddSingleton<ISettingEncryptionProvider, AesGcmSettingEncryptionProvider>();

        // Register ISetupWizardService (the real one — internal, so register via factory)
        services.RemoveAll<ISetupWizardService>();
        services.AddScoped<ISetupWizardService>(sp =>
            (ISetupWizardService)ActivatorUtilities.CreateInstance(sp,
                Type.GetType("GroundUp.Services.Setup.SetupWizardService, GroundUp.Services")!));

        // Register KeycloakAdminHttpClient (will not be called in ordering tests)
        services.AddHttpClient<KeycloakAdminHttpClient>();

        // Register options
        services.AddOptions<SetupTransactionLogOptions>()
            .Configure(o => o.MaxRowCount = 100);
        services.AddOptions<BootstrapOptions>()
            .Configure(o =>
            {
                o.MasterKey = Convert.ToBase64String(TestMasterKey);
                o.BootstrapAdminToken = TestBootstrapToken;
            });

        // Register SetupCurrentUser as ICurrentUser so that the AuditableInterceptor
        // writes "setup-wizard" to CreatedBy/UpdatedBy during setup mode (Cross-Cutting 8)
        services.RemoveAll<ICurrentUser>();
        services.AddScoped<ICurrentUser>(_ => new SetupCurrentUser());

        // Register mock IIdentityBootstrapService
        services.RemoveAll<IIdentityBootstrapService>();
        services.AddScoped<IIdentityBootstrapService, FakeIdentityBootstrapService>();

        // Register mock IIdentityProviderAdminService
        services.RemoveAll<IIdentityProviderAdminService>();
        services.AddScoped<IIdentityProviderAdminService, FakeIdentityProviderAdminService>();

        // Register the BootstrapAdminToken authentication scheme
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, BootstrapAdminTokenAuthenticationHandler>(
                BootstrapAdminTokenAuthenticationHandler.SchemeName, _ => { });

        // Memory cache for bootstrap state
        services.AddMemoryCache();

        // Disable settings caching to avoid stale reads in integration tests
        services.Configure<GroundUp.Core.Models.SettingsCacheOptions>(options =>
        {
            options.CacheDuration = TimeSpan.Zero;
        });

        // Replace the default scope chain provider with one that returns the system level.
        // The SetupWizardService writes settings at the system level and reads them back
        // via the convenience GetAsync overload which uses IScopeChainProvider.
        services.RemoveAll<IScopeChainProvider>();
        services.AddScoped<IScopeChainProvider, SetupWizardTestScopeChainProvider>();
    }
}

/// <summary>
/// Test implementation of <see cref="IBootstrapStateService"/> that always reports
/// setup as incomplete (setup mode) for wizard integration tests.
/// </summary>
internal sealed class SetupWizardTestBootstrapStateService : IBootstrapStateService
{
    public Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<OperationResult> CompleteSetupAsync(Guid completedByUserId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    public void InvalidateCache() { }
}

/// <summary>
/// Test master key provider that returns a fixed key.
/// </summary>
internal sealed class TestMasterKeyProvider : IMasterKeyProvider
{
    private readonly byte[] _key;

    public TestMasterKeyProvider(byte[] key) => _key = key;

    public byte[] GetKey() => _key;
}

/// <summary>
/// Fake identity bootstrap service for integration tests.
/// Returns success for provisioning and reports no super admin exists.
/// </summary>
internal sealed class FakeIdentityBootstrapService : IIdentityBootstrapService
{
    public Task<OperationResult<BootstrapAdminResultDto>> ProvisionFirstSuperAdminAsync(
        ProvisionFirstSuperAdminRequest request, CancellationToken cancellationToken = default)
    {
        var result = new BootstrapAdminResultDto(Guid.NewGuid(), request.Email, false);
        return Task.FromResult(OperationResult<BootstrapAdminResultDto>.Ok(result));
    }

    public Task<bool> HasSuperAdminAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<Guid?> GetSuperAdminUserIdAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<Guid?>(null);

    public Task<bool> HasSystemTenantAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> HasSuperAdminRoleAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

/// <summary>
/// Fake identity provider admin service for integration tests.
/// Returns success for user provisioning.
/// </summary>
internal sealed class FakeIdentityProviderAdminService : IIdentityProviderAdminService
{
    public Task<OperationResult<RealmDto>> CreateRealmAsync(
        CreateRealmRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RealmDto>.Ok(new RealmDto(request.RealmName, request.DisplayName, true)));

    public Task<OperationResult<RealmDto>> GetRealmAsync(
        string realmName, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RealmDto>.Ok(new RealmDto(realmName, realmName, true)));

    public Task<OperationResult<RealmDto>> UpdateRealmAsync(
        string realmName, UpdateRealmRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RealmDto>.Ok(new RealmDto(realmName, request.DisplayName, true)));

    public Task<OperationResult> DeleteRealmAsync(
        string realmName, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    public Task<OperationResult<IdentityProviderClientDto>> CreateClientAsync(
        string realmName, CreateIdentityProviderClientRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<IdentityProviderClientDto>.Ok(
            new IdentityProviderClientDto(request.ClientId, null, Array.Empty<string>(), false)));

    public Task<OperationResult<IdentityProviderClientDto>> GetClientAsync(
        string realmName, string clientId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<IdentityProviderClientDto>.Ok(
            new IdentityProviderClientDto(clientId, null, Array.Empty<string>(), false)));

    public Task<OperationResult<IdentityProviderClientDto>> UpdateClientAsync(
        string realmName, string clientId, UpdateIdentityProviderClientRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<IdentityProviderClientDto>.Ok(
            new IdentityProviderClientDto(clientId, null, Array.Empty<string>(), false)));

    public Task<OperationResult> DeleteClientAsync(
        string realmName, string clientId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    public Task<OperationResult<ProvisionedUserDto>> ProvisionUserAsync(
        string realmName, ProvisionUserRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<ProvisionedUserDto>.Ok(
            new ProvisionedUserDto(Guid.NewGuid().ToString(), request.Email, request.DisplayName, false)));

    public Task<OperationResult> SetUserCredentialsAsync(
        string realmName, string externalUserId, IdentityProviderUserCredentialsDto credentials,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    public Task<OperationResult> DeleteUserAsync(
        string realmName, string externalUserId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());
}

/// <summary>
/// xUnit collection definition that shares a single <see cref="SetupWizardApiFactory"/>
/// across all setup wizard integration test classes.
/// </summary>
[CollectionDefinition("SetupWizardApi")]
public sealed class SetupWizardApiCollection : ICollectionFixture<SetupWizardApiFactory>
{
}

/// <summary>
/// Test scope chain provider that returns the "system" level from the database.
/// This allows the SetupWizardService's precondition checks (which use the convenience
/// GetAsync overload) to find settings stored at the system level.
/// </summary>
internal sealed class SetupWizardTestScopeChainProvider : IScopeChainProvider
{
    private readonly SampleDbContext _dbContext;

    public SetupWizardTestScopeChainProvider(SampleDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<SettingScopeEntry>> GetScopeChainAsync(
        CancellationToken cancellationToken = default)
    {
        var systemLevel = await _dbContext.Set<SettingLevel>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Name == "system", cancellationToken);

        if (systemLevel is null)
            return Array.Empty<SettingScopeEntry>();

        return new[] { new SettingScopeEntry(systemLevel.Id, null) };
    }
}
