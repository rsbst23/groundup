using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using GroundUp.Api.Authentication;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Configuration;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Dtos.Setup;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Results;
using GroundUp.Sample.Data;
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

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// Integration tests for first-admin endpoint idempotency behavior.
/// Verifies: re-run with same email/displayName → 200 with same userId;
/// conflicting attributes → 409.
/// Requirements: 12.14, 12.18, 12.19
/// </summary>
[Collection("FirstAdminIdempotencyApi")]
public sealed class FirstAdminIdempotencyIntegrationTests : IAsyncLifetime
{
    private readonly FirstAdminIdempotencyApiFactory _factory;
    private HttpClient _client = null!;

    public FirstAdminIdempotencyIntegrationTests(FirstAdminIdempotencyApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", FirstAdminIdempotencyApiFactory.TestBootstrapToken);

        // Seed the "system" setting level required by SetupWizardService.
        // The factory removes hosted services (including DataSeederRunner) to avoid interference,
        // so we seed the minimum required data here.
        await EnsureSystemLevelExistsAsync();

        // Reset stateful fakes to ensure test isolation
        var bootstrapService = _factory.Services.GetRequiredService<StatefulFakeIdentityBootstrapService>();
        bootstrapService.Reset();
        var idpService = _factory.Services.GetRequiredService<StatefulFakeIdentityProviderAdminService>();
        idpService.Reset();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    #region Idempotent Re-Run → 200 with Same UserId (Req 12.18)

    [Fact]
    public async Task CreateFirstAdmin_ReRunWithSameEmailAndDisplayName_Returns200WithSameUserId()
    {
        // Arrange — complete prerequisite steps
        await CompletePrerequisiteStepsAsync();

        var request = new CreateFirstAdminRequest(
            "admin@example.com",
            "Admin User",
            "P@ssw0rd!2345");

        // Act — call first-admin twice with the same data
        var response1 = await _client.PostAsJsonAsync("/setup/first-admin", request);
        var response2 = await _client.PostAsJsonAsync("/setup/first-admin", request);

        // Assert — both should succeed
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var body1 = await response1.Content.ReadFromJsonAsync<FirstAdminResponse>();
        var body2 = await response2.Content.ReadFromJsonAsync<FirstAdminResponse>();

        body1.Should().NotBeNull();
        body2.Should().NotBeNull();
        body1!.Step.Should().Be("first-admin");
        body1.Completed.Should().BeTrue();
        body2!.Step.Should().Be("first-admin");
        body2.Completed.Should().BeTrue();

        // The same userId should be returned on both calls (idempotent)
        body1.UserId.Should().Be(body2.UserId);
        body1.Email.Should().Be("admin@example.com");
        body2.Email.Should().Be("admin@example.com");
    }

    [Fact]
    public async Task CreateFirstAdmin_ReRunWithSameData_ReturnsExistingUserId()
    {
        // Arrange — complete prerequisite steps
        await CompletePrerequisiteStepsAsync();

        var request = new CreateFirstAdminRequest(
            "idempotent@example.com",
            "Idempotent Admin",
            "Str0ng!Pass#99");

        // Act — first call creates the user
        var response1 = await _client.PostAsJsonAsync("/setup/first-admin", request);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        var body1 = await response1.Content.ReadFromJsonAsync<FirstAdminResponse>();
        body1.Should().NotBeNull();
        var originalUserId = body1!.UserId;

        // Act — second call should be idempotent
        var response2 = await _client.PostAsJsonAsync("/setup/first-admin", request);

        // Assert
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await response2.Content.ReadFromJsonAsync<FirstAdminResponse>();
        body2.Should().NotBeNull();
        body2!.UserId.Should().Be(originalUserId, "idempotent re-run should return the same userId");
    }

    #endregion

    #region Conflicting Attributes → 409 (Req 12.19)

    [Fact]
    public async Task CreateFirstAdmin_ConflictingDisplayName_Returns409()
    {
        // Arrange — complete prerequisite steps
        await CompletePrerequisiteStepsAsync();

        var firstRequest = new CreateFirstAdminRequest(
            "conflict@example.com",
            "Original Admin",
            "P@ssw0rd!2345");

        // First call creates the user
        var response1 = await _client.PostAsJsonAsync("/setup/first-admin", firstRequest);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second call with same email but different display name
        var conflictingRequest = new CreateFirstAdminRequest(
            "conflict@example.com",
            "Different Display Name",
            "P@ssw0rd!2345");

        // Act
        var response2 = await _client.PostAsJsonAsync("/setup/first-admin", conflictingRequest);

        // Assert
        response2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response2.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("conflict");
        body.Message.Should().Contain("conflicting attributes");
    }

    [Fact]
    public async Task CreateFirstAdmin_DifferentEmailWhenAdminExists_Returns409()
    {
        // Arrange — complete prerequisite steps
        await CompletePrerequisiteStepsAsync();

        var firstRequest = new CreateFirstAdminRequest(
            "first-admin@example.com",
            "First Admin",
            "P@ssw0rd!2345");

        // First call creates the user
        var response1 = await _client.PostAsJsonAsync("/setup/first-admin", firstRequest);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second call with different email
        var conflictingRequest = new CreateFirstAdminRequest(
            "different-email@example.com",
            "First Admin",
            "P@ssw0rd!2345");

        // Act
        var response2 = await _client.PostAsJsonAsync("/setup/first-admin", conflictingRequest);

        // Assert
        response2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response2.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("conflict");
        body.Message.Should().Contain("conflicting attributes");
    }

    #endregion

    #region Helper Methods

    private async Task EnsureSystemLevelExistsAsync()
    {
        using var scope = _factory.Services.CreateScope();
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
    }

    private async Task CompletePrerequisiteStepsAsync()
    {
        // Step 1: App Identity
        var appIdentityRequest = new SetAppIdentityRequest("Test App", "test.example.com");
        var appIdentityResponse = await _client.PostAsJsonAsync("/setup/app-identity", appIdentityRequest);
        appIdentityResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "app-identity step should succeed as a prerequisite");

        // Step 2: Identity Provider
        var idpRequest = new SetIdentityProviderRequest(
            "https://keycloak.example.com",
            "https://keycloak-internal.example.com",
            "groundup");
        var idpResponse = await _client.PostAsJsonAsync("/setup/identity-provider", idpRequest);
        idpResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "identity-provider step should succeed as a prerequisite");

        // Step 3: Keycloak Bootstrap — simulate by writing settings directly.
        // The keycloak-bootstrap step requires a real Keycloak HTTP client, so we write
        // the settings that the step would produce via the settings service.
        using var scope = _factory.Services.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        await settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.keycloak.admin-client-id",
            DataType: SettingDataType.String,
            DefaultValue: "groundup-admin-client",
            DisplayName: "Admin Client ID",
            Description: null,
            Category: null,
            GroupKey: "groundup.setup",
            GroupDisplayName: "Setup",
            AllowedLevelNames: new[] { "system" },
            IsRequired: true,
            IsSecret: false,
            IsEncrypted: false));

        await settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.keycloak.admin-client-secret",
            DataType: SettingDataType.String,
            DefaultValue: "",
            DisplayName: "Admin Client Secret",
            Description: null,
            Category: null,
            GroupKey: "groundup.setup",
            GroupDisplayName: "Setup",
            AllowedLevelNames: new[] { "system" },
            IsRequired: true,
            IsSecret: true,
            IsEncrypted: true));

        // Get the system level ID to persist values
        var dbContext = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        var systemLevel = await dbContext.Set<SettingLevel>()
            .FirstAsync(l => l.Name == "system");

        await settingsService.SetAsync(
            "auth.keycloak.admin-client-id", "groundup-admin-client", systemLevel.Id, null);
        await settingsService.SetAsync(
            "auth.keycloak.admin-client-secret", "test-client-secret-value", systemLevel.Id, null);
    }

    #endregion

    #region Response DTOs

    private sealed record FirstAdminResponse(string Step, bool Completed, Guid UserId, string Email);
    private sealed record ErrorResponse(string Code, string Message);

    #endregion
}

/// <summary>
/// WebApplicationFactory for first-admin idempotency integration tests.
/// Uses a stateful fake <see cref="IIdentityBootstrapService"/> that tracks
/// provisioned users to support idempotency and conflict detection testing.
/// </summary>
public sealed class FirstAdminIdempotencyApiFactory : GroundUpWebApplicationFactory<Program, SampleDbContext>
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

        // Replace IBootstrapStateService with a test implementation (setup incomplete)
        services.RemoveAll<IBootstrapStateService>();
        services.AddScoped<IBootstrapStateService, IdempotencyTestBootstrapStateService>();

        // Register IMasterKeyProvider with a test key
        services.RemoveAll<IMasterKeyProvider>();
        services.AddSingleton<IMasterKeyProvider>(new IdempotencyTestMasterKeyProvider(TestMasterKey));

        // Register ISettingEncryptionProvider
        services.RemoveAll<ISettingEncryptionProvider>();
        services.AddSingleton<ISettingEncryptionProvider, AesGcmSettingEncryptionProvider>();

        // Register ISetupWizardService (the real one)
        services.RemoveAll<ISetupWizardService>();
        services.AddScoped<ISetupWizardService>(sp =>
            (ISetupWizardService)ActivatorUtilities.CreateInstance(sp,
                Type.GetType("GroundUp.Services.Setup.SetupWizardService, GroundUp.Services")!));

        // Register KeycloakAdminHttpClient
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
        // writes "setup-wizard" to CreatedBy/UpdatedBy during setup mode
        services.RemoveAll<ICurrentUser>();
        services.AddScoped<ICurrentUser>(_ => new GroundUp.Services.Bootstrap.SetupCurrentUser());

        // Register stateful fake IIdentityBootstrapService for idempotency testing
        services.RemoveAll<IIdentityBootstrapService>();
        services.AddSingleton<StatefulFakeIdentityBootstrapService>();
        services.AddScoped<IIdentityBootstrapService>(sp =>
            sp.GetRequiredService<StatefulFakeIdentityBootstrapService>());

        // Register fake IIdentityProviderAdminService
        services.RemoveAll<IIdentityProviderAdminService>();
        services.AddSingleton<StatefulFakeIdentityProviderAdminService>();
        services.AddScoped<IIdentityProviderAdminService>(sp =>
            sp.GetRequiredService<StatefulFakeIdentityProviderAdminService>());

        // Register the BootstrapAdminToken authentication scheme
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, BootstrapAdminTokenAuthenticationHandler>(
                BootstrapAdminTokenAuthenticationHandler.SchemeName, _ => { });

        // Memory cache for bootstrap state — disable settings cache to avoid stale reads
        services.AddMemoryCache();
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
/// setup as incomplete for idempotency integration tests.
/// </summary>
internal sealed class IdempotencyTestBootstrapStateService : IBootstrapStateService
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
internal sealed class IdempotencyTestMasterKeyProvider : IMasterKeyProvider
{
    private readonly byte[] _key;

    public IdempotencyTestMasterKeyProvider(byte[] key) => _key = key;

    public byte[] GetKey() => _key;
}

/// <summary>
/// Stateful fake <see cref="IIdentityBootstrapService"/> that tracks provisioned users
/// to support idempotency and conflict detection testing.
/// On first provision: creates a user and remembers it.
/// On re-provision with same email/displayName: returns AlreadyExisted=true with same userId.
/// On re-provision with conflicting attributes: returns 409 Conflict.
/// </summary>
internal sealed class StatefulFakeIdentityBootstrapService : IIdentityBootstrapService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (Guid UserId, string DisplayName)> _provisionedUsers = new();
    private bool _hasSuperAdmin;

    /// <summary>
    /// Resets all state for test isolation.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _provisionedUsers.Clear();
            _hasSuperAdmin = false;
        }
    }

    public Task<OperationResult<BootstrapAdminResultDto>> ProvisionFirstSuperAdminAsync(
        ProvisionFirstSuperAdminRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_provisionedUsers.TryGetValue(request.Email, out var existing))
            {
                // User already exists — check if attributes match
                if (existing.DisplayName == request.DisplayName)
                {
                    // Idempotent: same email + same displayName → return existing
                    var idempotentResult = new BootstrapAdminResultDto(existing.UserId, request.Email, true);
                    return Task.FromResult(OperationResult<BootstrapAdminResultDto>.Ok(idempotentResult));
                }

                // Conflict: same email but different displayName
                return Task.FromResult(OperationResult<BootstrapAdminResultDto>.Fail(
                    "A super admin user already exists with conflicting attributes.", 409, "conflict"));
            }

            // Also check if any super admin exists with a different email
            if (_hasSuperAdmin && _provisionedUsers.Count > 0)
            {
                // A super admin exists but with a different email — conflict
                return Task.FromResult(OperationResult<BootstrapAdminResultDto>.Fail(
                    "A super admin user already exists with conflicting attributes.", 409, "conflict"));
            }

            // New user — provision
            var userId = Guid.NewGuid();
            _provisionedUsers[request.Email] = (userId, request.DisplayName);
            _hasSuperAdmin = true;

            var result = new BootstrapAdminResultDto(userId, request.Email, false);
            return Task.FromResult(OperationResult<BootstrapAdminResultDto>.Ok(result));
        }
    }

    public Task<bool> HasSuperAdminAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_hasSuperAdmin);
        }
    }

    public Task<Guid?> GetSuperAdminUserIdAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_provisionedUsers.Count > 0)
                return Task.FromResult<Guid?>(_provisionedUsers.Values.First().UserId);
            return Task.FromResult<Guid?>(null);
        }
    }

    public Task<bool> HasSystemTenantAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> HasSuperAdminRoleAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

/// <summary>
/// Stateful fake <see cref="IIdentityProviderAdminService"/> that tracks provisioned users
/// in Keycloak to support idempotency testing. Returns a consistent external user ID
/// for the same email across calls.
/// </summary>
internal sealed class StatefulFakeIdentityProviderAdminService : IIdentityProviderAdminService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _provisionedUsers = new(); // email → externalUserId

    /// <summary>
    /// Resets all state for test isolation.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _provisionedUsers.Clear();
        }
    }

    public Task<OperationResult<ProvisionedUserDto>> ProvisionUserAsync(
        string realmName, ProvisionUserRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_provisionedUsers.TryGetValue(request.Email, out var existingExternalId))
            {
                // User already exists — return the same external ID (simulates idempotent path)
                var existingResult = new ProvisionedUserDto(existingExternalId, request.Email, request.DisplayName, false);
                return Task.FromResult(OperationResult<ProvisionedUserDto>.Ok(existingResult));
            }

            var externalUserId = Guid.NewGuid().ToString();
            _provisionedUsers[request.Email] = externalUserId;

            var result = new ProvisionedUserDto(externalUserId, request.Email, request.DisplayName, false);
            return Task.FromResult(OperationResult<ProvisionedUserDto>.Ok(result));
        }
    }

    public Task<OperationResult<RealmDto>> CreateRealmAsync(
        CreateRealmRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RealmDto>.Ok(
            new RealmDto(request.RealmName, request.DisplayName ?? request.RealmName, true)));

    public Task<OperationResult<RealmDto>> GetRealmAsync(
        string realmName, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RealmDto>.Ok(new RealmDto(realmName, realmName, true)));

    public Task<OperationResult<RealmDto>> UpdateRealmAsync(
        string realmName, UpdateRealmRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RealmDto>.Ok(
            new RealmDto(realmName, request.DisplayName ?? realmName, true)));

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

    public Task<OperationResult> SetUserCredentialsAsync(
        string realmName, string externalUserId, IdentityProviderUserCredentialsDto credentials,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    public Task<OperationResult> DeleteUserAsync(
        string realmName, string externalUserId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());
}

/// <summary>
/// xUnit collection definition that shares a single <see cref="FirstAdminIdempotencyApiFactory"/>
/// across all first-admin idempotency integration test classes.
/// </summary>
[CollectionDefinition("FirstAdminIdempotencyApi")]
public sealed class FirstAdminIdempotencyApiCollection : ICollectionFixture<FirstAdminIdempotencyApiFactory>
{
}