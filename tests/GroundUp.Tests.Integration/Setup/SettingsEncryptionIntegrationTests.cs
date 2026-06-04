using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Data.Postgres;
using GroundUp.Events;
using GroundUp.Services.Security;
using GroundUp.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// Integration tests verifying the full encryption round-trip against a real Postgres
/// database: write an encrypted setting via SettingsService.SetAsync, read it back
/// decrypted via SettingsService.GetAsync, and verify the raw DB row contains the
/// <c>aes-gcm-v1:</c> ciphertext prefix.
/// </summary>
public sealed class SettingsEncryptionIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private string _connectionString = null!;

    /// <summary>
    /// A deterministic 32-byte test master key (base64-encoded).
    /// </summary>
    private static readonly byte[] TestMasterKey = Convert.FromBase64String(
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        // Create schema
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task SetAsync_EncryptedSetting_StoresAesGcmCiphertextInDatabase()
    {
        // Arrange
        await using var context = CreateContext();
        var (service, levelId) = await SetupServiceWithEncryptedDefinition(
            context, "test.encrypted.key", "default-value");

        // Act — write a plaintext value
        var setResult = await service.SetAsync("test.encrypted.key", "my-secret-value", levelId, null);

        // Assert — SetAsync succeeds
        setResult.Success.Should().BeTrue("SetAsync should succeed for encrypted setting");

        // Verify the raw DB row contains aes-gcm-v1: prefix (ciphertext, not plaintext)
        await using var verifyContext = CreateContext();
        var storedValue = await verifyContext.Set<SettingValue>()
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.LevelId == levelId);

        storedValue.Should().NotBeNull();
        storedValue!.Value.Should().NotBeNull();
        storedValue.Value.Should().StartWith("aes-gcm-v1:",
            "the stored value must be encrypted with the aes-gcm-v1 format");
        storedValue.Value.Should().NotContain("my-secret-value",
            "the plaintext must never appear in the database");
    }

    [Fact]
    public async Task GetAsync_EncryptedSetting_ReturnsDecryptedPlaintext()
    {
        // Arrange
        await using var context = CreateContext();
        var (service, levelId) = await SetupServiceWithEncryptedDefinition(
            context, "test.decrypt.key", "fallback");

        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Write an encrypted value
        var setResult = await service.SetAsync("test.decrypt.key", "sensitive-data-123", levelId, null);
        setResult.Success.Should().BeTrue();

        // Act — read it back (should be decrypted transparently)
        var getResult = await service.GetAsync<string>("test.decrypt.key", scopeChain);

        // Assert
        getResult.Success.Should().BeTrue("GetAsync should succeed for encrypted setting");
        getResult.Data.Should().Be("sensitive-data-123",
            "the decrypted value must match the original plaintext");
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTrip_PreservesOriginalValue()
    {
        // Arrange
        await using var context = CreateContext();
        var (service, levelId) = await SetupServiceWithEncryptedDefinition(
            context, "test.roundtrip.key", null);

        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };
        const string originalValue = "Hello, World! 🔐 Special chars: <>&\"'";

        // Act — write then read
        var setResult = await service.SetAsync("test.roundtrip.key", originalValue, levelId, null);
        setResult.Success.Should().BeTrue();

        var getResult = await service.GetAsync<string>("test.roundtrip.key", scopeChain);

        // Assert
        getResult.Success.Should().BeTrue();
        getResult.Data.Should().Be(originalValue,
            "encryption round-trip must preserve the original value including special characters");

        // Also verify the DB row is encrypted
        await using var verifyContext = CreateContext();
        var storedValue = await verifyContext.Set<SettingValue>()
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.LevelId == levelId);

        storedValue!.Value.Should().StartWith("aes-gcm-v1:");
        storedValue.Value.Should().NotBe(originalValue);
    }

    #region Helper Methods

    private TestSettingsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestSettingsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new TestSettingsDbContext(options);
    }

    private async Task<(SettingsService Service, Guid LevelId)> SetupServiceWithEncryptedDefinition(
        TestSettingsDbContext context,
        string key,
        string? defaultValue)
    {
        // Create a setting level
        var level = new SettingLevel
        {
            Id = Guid.NewGuid(),
            Name = $"System_{Guid.NewGuid():N}"[..20],
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow
        };
        context.Set<SettingLevel>().Add(level);

        // Create an encrypted setting definition
        var definition = new SettingDefinition
        {
            Id = Guid.NewGuid(),
            Key = key,
            DataType = SettingDataType.String,
            DefaultValue = defaultValue,
            DisplayName = key,
            IsEncrypted = true,
            IsSecret = false,
            IsVisible = true,
            IsReadOnly = false,
            AllowMultiple = false,
            IsRequired = false,
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow
        };
        context.Set<SettingDefinition>().Add(definition);

        // Create the allowed level association
        var allowedLevel = new SettingDefinitionLevel
        {
            Id = Guid.NewGuid(),
            SettingDefinitionId = definition.Id,
            SettingLevelId = level.Id
        };
        context.Set<SettingDefinitionLevel>().Add(allowedLevel);

        await context.SaveChangesAsync();

        // Build the SettingsService with real encryption
        var masterKeyProvider = new TestMasterKeyProvider(TestMasterKey);
        var encryptionProvider = new AesGcmSettingEncryptionProvider(masterKeyProvider);
        var eventBus = new InProcessEventBus(
            new ServiceProviderStub(),
            NullLogger<InProcessEventBus>.Instance);
        var scopeChainProvider = new TestScopeChainProvider(level.Id);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var cacheOptions = Options.Create(new SettingsCacheOptions { CacheDuration = TimeSpan.Zero });
        var cacheKeyTracker = new SettingsCacheKeyTracker();

        var service = new SettingsService(
            context,
            eventBus,
            scopeChainProvider,
            cache,
            cacheOptions,
            cacheKeyTracker,
            encryptionProvider);

        return (service, level.Id);
    }

    #endregion

    #region Test Doubles

    /// <summary>
    /// Concrete DbContext for settings encryption integration tests.
    /// Inherits from GroundUpDbContext so all entity configurations are applied.
    /// </summary>
    private sealed class TestSettingsDbContext : GroundUpDbContext
    {
        public TestSettingsDbContext(DbContextOptions<TestSettingsDbContext> options)
            : base(options) { }
    }

    /// <summary>
    /// Test master key provider that returns a fixed key.
    /// </summary>
    private sealed class TestMasterKeyProvider : IMasterKeyProvider
    {
        private readonly byte[] _key;

        public TestMasterKeyProvider(byte[] key) => _key = key;

        public byte[] GetKey() => _key;
    }

    /// <summary>
    /// Test scope chain provider that returns a single-level scope chain.
    /// </summary>
    private sealed class TestScopeChainProvider : IScopeChainProvider
    {
        private readonly Guid _levelId;

        public TestScopeChainProvider(Guid levelId) => _levelId = levelId;

        public Task<IReadOnlyList<SettingScopeEntry>> GetScopeChainAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SettingScopeEntry> chain = new List<SettingScopeEntry>
            {
                new(_levelId, null)
            };
            return Task.FromResult(chain);
        }
    }

    /// <summary>
    /// Minimal IServiceProvider stub for InProcessEventBus (no handlers needed for these tests).
    /// </summary>
    private sealed class ServiceProviderStub : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    #endregion
}
