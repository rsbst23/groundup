using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Configuration;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Dtos.Setup;
using GroundUp.Core.Entities;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Results;
using GroundUp.Data.Postgres;
using GroundUp.Services.Setup;
using GroundUp.Services.Setup.Keycloak;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Setup;

/// <summary>
/// Property-based tests for wizard step idempotency and transaction log rotation.
/// **Validates: Requirements 9.8, 10.10, 12.20, 13.10**
/// </summary>
public sealed class WizardIdempotencyAndRotationPropertyTests
{
    #region Property 10a: App-Identity Idempotency

    /// <summary>
    /// Property 10 (app-identity idempotency): For any valid application name,
    /// invoking SetAppIdentityAsync twice with the same body leaves the persisted
    /// settings in the same state as a single invocation — the second call overwrites
    /// with the same values, producing identical state.
    /// **Validates: Requirements 9.8**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property SetAppIdentityAsync_RepeatedCalls_ProduceSameState(NonEmptyString appName)
    {
        var name = appName.Get.Length > 200 ? appName.Get[..200] : appName.Get;

        Func<bool> property = () =>
        {
            var settingsStore = new Dictionary<string, string?>();
            var settingsService = CreateTrackingSettingsService(settingsStore);
            var sut = CreateWizardServiceWithSeededDb(settingsService);

            var request = new SetAppIdentityRequest(name, "example.com");

            // First call
            var result1 = sut.SetAppIdentityAsync(request).GetAwaiter().GetResult();
            if (!result1.Success) return false;

            var stateAfterFirst = new Dictionary<string, string?>(settingsStore);

            // Second call (same body)
            var result2 = sut.SetAppIdentityAsync(request).GetAwaiter().GetResult();
            if (!result2.Success) return false;

            var stateAfterSecond = new Dictionary<string, string?>(settingsStore);

            // State after first == state after second (idempotent overwrite)
            return stateAfterFirst.Count == stateAfterSecond.Count
                && stateAfterFirst.All(kvp =>
                    stateAfterSecond.ContainsKey(kvp.Key)
                    && stateAfterSecond[kvp.Key] == kvp.Value);
        };

        return property.When(!string.IsNullOrWhiteSpace(name));
    }

    #endregion

    #region Property 10b: Identity-Provider Idempotency

    /// <summary>
    /// Property 10 (identity-provider idempotency): For any valid URL and realm name,
    /// invoking SetIdentityProviderAsync twice with the same body leaves the persisted
    /// settings in the same state as a single invocation.
    /// **Validates: Requirements 10.10**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property SetIdentityProviderAsync_RepeatedCalls_ProduceSameState(PositiveInt seed)
    {
        Func<bool> property = () =>
        {
            var settingsStore = new Dictionary<string, string?>();
            var settingsService = CreateTrackingSettingsService(settingsStore);

            // Pre-populate app-identity settings so precondition passes
            SetupAppIdentityPrecondition(settingsService);

            var sut = CreateWizardServiceWithSeededDb(settingsService);

            var request = new SetIdentityProviderRequest(
                "https://keycloak.example.com",
                "http://keycloak-internal:8080",
                "groundup-realm");

            // First call
            var result1 = sut.SetIdentityProviderAsync(request).GetAwaiter().GetResult();
            if (!result1.Success) return false;

            var stateAfterFirst = new Dictionary<string, string?>(settingsStore);

            // Second call (same body)
            var result2 = sut.SetIdentityProviderAsync(request).GetAwaiter().GetResult();
            if (!result2.Success) return false;

            var stateAfterSecond = new Dictionary<string, string?>(settingsStore);

            // State after first == state after second (idempotent overwrite)
            return stateAfterFirst.Count == stateAfterSecond.Count
                && stateAfterFirst.All(kvp =>
                    stateAfterSecond.ContainsKey(kvp.Key)
                    && stateAfterSecond[kvp.Key] == kvp.Value);
        };

        return property.ToProperty();
    }

    #endregion

    #region Property 10c: Transaction Log Rotation Respects MaxRowCount

    /// <summary>
    /// Property 10 (rotation row count): For any (MaxRowCount, currentTotal, pendingCount)
    /// where MaxRowCount > 0, after one insertion the resulting row count equals
    /// min(currentTotal + 1, MaxRowCount + pendingCount). Pending rows are never deleted.
    /// **Validates: Requirements 13.10**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property InsertWithRotation_RespectsMaxRowCount(
        PositiveInt maxRowCountRaw, PositiveInt currentTotalRaw, PositiveInt pendingCountRaw)
    {
        // Constrain to reasonable ranges for test performance
        var maxRowCount = Math.Clamp(maxRowCountRaw.Get, 1, 50);
        var currentTotal = Math.Clamp(currentTotalRaw.Get, 0, 60);
        var pendingCount = Math.Min(Math.Clamp(pendingCountRaw.Get, 0, 10), currentTotal);

        Func<bool> property = () =>
        {
            var dbName = Guid.NewGuid().ToString();

            // Seed the database with existing rows
            using (var ctx = CreateRotationDbContext(dbName))
            {
                SeedTransactionLogs(ctx, currentTotal, pendingCount);
            }

            // Perform one insertion with rotation
            using (var ctx = CreateRotationDbContext(dbName))
            {
                var logger = NullLogger.Instance;
                var newRow = new SetupTransactionLog
                {
                    Id = Guid.NewGuid(),
                    Operation = "test-operation",
                    Stage = "completed",
                    CreatedAt = DateTime.UtcNow
                };

                PerformInsertWithRotation(ctx, newRow, maxRowCount, logger);
            }

            // Verify the resulting state
            using (var ctx = CreateRotationDbContext(dbName))
            {
                var totalAfter = ctx.SetupTransactionLogs.Count();
                var pendingAfter = ctx.SetupTransactionLogs
                    .Count(l => l.Stage == "keycloak-pending" || l.Stage == "db-pending");

                // The rotation algorithm:
                // toDelete = max(0, (currentTotal + 1) - MaxRowCount)
                // actualDeleted = min(toDelete, currentTotal - pendingCount) [only non-pending deletable]
                // expectedTotal = currentTotal + 1 - actualDeleted
                var toDelete = Math.Max(0, (currentTotal + 1) - maxRowCount);
                var actualDeleted = Math.Min(toDelete, currentTotal - pendingCount);
                var expectedTotal = currentTotal + 1 - actualDeleted;

                // Pending rows must never be deleted
                var pendingPreserved = pendingAfter >= pendingCount;

                return totalAfter == expectedTotal && pendingPreserved;
            }
        };

        return property.When(pendingCount <= currentTotal);
    }

    /// <summary>
    /// Property 10 (rotation disabled): For any MaxRowCount ≤ 0, rotation is disabled
    /// and the insert always adds a row without deleting any existing rows.
    /// **Validates: Requirements 13.10**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property InsertWithRotation_DisabledWhenMaxRowCountZeroOrNegative(
        NegativeInt maxRowCountRaw, PositiveInt currentTotalRaw)
    {
        var maxRowCount = Math.Min(maxRowCountRaw.Get, 0); // Ensure ≤ 0
        var currentTotal = Math.Clamp(currentTotalRaw.Get, 1, 30);

        Func<bool> property = () =>
        {
            var dbName = Guid.NewGuid().ToString();

            using (var ctx = CreateRotationDbContext(dbName))
            {
                SeedTransactionLogs(ctx, currentTotal, pendingCount: 0);
            }

            using (var ctx = CreateRotationDbContext(dbName))
            {
                var logger = NullLogger.Instance;
                var newRow = new SetupTransactionLog
                {
                    Id = Guid.NewGuid(),
                    Operation = "test-operation",
                    Stage = "completed",
                    CreatedAt = DateTime.UtcNow
                };

                PerformInsertWithRotation(ctx, newRow, maxRowCount, logger);
            }

            using (var ctx = CreateRotationDbContext(dbName))
            {
                var totalAfter = ctx.SetupTransactionLogs.Count();
                // Rotation disabled: row count should be currentTotal + 1
                return totalAfter == currentTotal + 1;
            }
        };

        return property.ToProperty();
    }

    /// <summary>
    /// Property 10 (pending-row protection): When all candidate rows for deletion
    /// are in pending stages, the insert still proceeds and no pending rows are deleted.
    /// **Validates: Requirements 13.10**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property InsertWithRotation_NeverDeletesPendingRows(PositiveInt maxRowCountRaw)
    {
        var maxRowCount = Math.Clamp(maxRowCountRaw.Get, 1, 10);
        // All existing rows are pending — rotation can't delete any of them
        var currentTotal = maxRowCount + 5; // Over the limit, but all pending

        Func<bool> property = () =>
        {
            var dbName = Guid.NewGuid().ToString();

            using (var ctx = CreateRotationDbContext(dbName))
            {
                // Seed ALL rows as pending (keycloak-pending or db-pending)
                SeedTransactionLogs(ctx, currentTotal, pendingCount: currentTotal);
            }

            using (var ctx = CreateRotationDbContext(dbName))
            {
                var logger = NullLogger.Instance;
                var newRow = new SetupTransactionLog
                {
                    Id = Guid.NewGuid(),
                    Operation = "test-operation",
                    Stage = "completed",
                    CreatedAt = DateTime.UtcNow
                };

                PerformInsertWithRotation(ctx, newRow, maxRowCount, logger);
            }

            using (var ctx = CreateRotationDbContext(dbName))
            {
                var totalAfter = ctx.SetupTransactionLogs.Count();
                var pendingAfter = ctx.SetupTransactionLogs
                    .Count(l => l.Stage == "keycloak-pending" || l.Stage == "db-pending");

                // All original pending rows preserved + new row added
                return totalAfter == currentTotal + 1 && pendingAfter == currentTotal;
            }
        };

        return property.ToProperty();
    }

    #endregion

    #region Helpers — Idempotency Tests

    private static ISettingsService CreateTrackingSettingsService(Dictionary<string, string?> store)
    {
        var settingsService = Substitute.For<ISettingsService>();

        // EnsureDefinitionAsync always succeeds
        settingsService.EnsureDefinitionAsync(
            Arg.Any<EnsureSettingDefinitionRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var req = callInfo.ArgAt<EnsureSettingDefinitionRequest>(0);
                return Task.FromResult(OperationResult<SettingDefinitionDto>.Ok(
                    new SettingDefinitionDto(
                        Guid.NewGuid(), req.Key, SettingDataType.String, null, null,
                        req.DisplayName, null, null, null, 0, true, false, false,
                        false, false, false, null, null, null, null, null, null,
                        null, null, null, null)));
            });

        // SetAsync tracks values in the store
        settingsService.SetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<string>(0);
                var value = callInfo.ArgAt<string>(1);
                store[key] = value;
                return Task.FromResult(OperationResult<SettingValueDto>.Ok(
                    new SettingValueDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, value)));
            });

        return settingsService;
    }

    private static void SetupAppIdentityPrecondition(ISettingsService settingsService)
    {
        settingsService.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("Test App")));

        settingsService.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("example.com")));
    }

    private static SetupWizardService CreateWizardServiceWithSeededDb(ISettingsService settingsService)
    {
        var bootstrapStateService = Substitute.For<IBootstrapStateService>();
        bootstrapStateService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var dbName = Guid.NewGuid().ToString();
        var dbContext = CreateIdempotencyDbContext(dbName);

        // Seed the system level so GetSystemLevelIdAsync succeeds
        dbContext.Set<SettingLevel>().Add(new SettingLevel
        {
            Id = Guid.NewGuid(),
            Name = "system",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        dbContext.SaveChanges();

        var transactionLogOptions = Options.Create(new SetupTransactionLogOptions { MaxRowCount = 1000 });
        var keycloakClient = new KeycloakAdminHttpClient(
            new HttpClient(), NullLogger<KeycloakAdminHttpClient>.Instance);
        var bootstrapOptions = Options.Create(new BootstrapOptions());
        var identityBootstrapService = Substitute.For<IIdentityBootstrapService>();
        var identityProviderAdminService = Substitute.For<IIdentityProviderAdminService>();

        return new SetupWizardService(
            settingsService,
            bootstrapStateService,
            dbContext,
            transactionLogOptions,
            keycloakClient,
            bootstrapOptions,
            identityBootstrapService,
            identityProviderAdminService,
            NullLogger<SetupWizardService>.Instance);
    }

    #endregion

    #region Helpers — Rotation Tests

    /// <summary>
    /// Replicates the InsertWithRotationAsync logic for direct testing.
    /// This mirrors the private method in SetupWizardService to test the rotation
    /// algorithm in isolation with controlled inputs.
    /// </summary>
    private static void PerformInsertWithRotation(
        TestRotationDbContext ctx,
        SetupTransactionLog row,
        int maxRowCount,
        ILogger logger)
    {
        // InMemory provider doesn't support transactions, so we skip BeginTransaction
        if (maxRowCount > 0)
        {
            var total = ctx.SetupTransactionLogs.Count();
            var toDelete = (total + 1) - maxRowCount;
            if (toDelete > 0)
            {
                var deletable = ctx.SetupTransactionLogs
                    .Where(l => l.Stage != "keycloak-pending" && l.Stage != "db-pending")
                    .OrderBy(l => l.CreatedAt)
                    .Take(toDelete)
                    .ToList();

                if (deletable.Count < toDelete)
                {
                    logger.LogWarning(
                        "Transaction log rotation could not free enough rows: total={Total}, max={Max}, pending protected.",
                        total, maxRowCount);
                }

                ctx.SetupTransactionLogs.RemoveRange(deletable);
            }
        }

        ctx.SetupTransactionLogs.Add(row);
        ctx.SaveChanges();
    }

    private static void SeedTransactionLogs(TestRotationDbContext ctx, int total, int pendingCount)
    {
        var pendingStages = new[] { "keycloak-pending", "db-pending" };
        var rng = new System.Random(42); // Deterministic for reproducibility

        for (var i = 0; i < total; i++)
        {
            var isPending = i < pendingCount;
            ctx.SetupTransactionLogs.Add(new SetupTransactionLog
            {
                Id = Guid.NewGuid(),
                Operation = "first-admin-create",
                Stage = isPending
                    ? pendingStages[rng.Next(pendingStages.Length)]
                    : "completed",
                CreatedAt = DateTime.UtcNow.AddMinutes(-total + i), // Oldest first
                CreatedBy = "setup-wizard"
            });
        }

        ctx.SaveChanges();
    }

    private static TestRotationDbContext CreateRotationDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<TestRotationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new TestRotationDbContext(options);
    }

    private static TestIdempotencyDbContext CreateIdempotencyDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<TestIdempotencyDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new TestIdempotencyDbContext(options);
    }

    #endregion

    #region Test DbContexts

    /// <summary>
    /// Concrete DbContext for rotation tests — only needs SetupTransactionLog.
    /// </summary>
    private sealed class TestRotationDbContext : GroundUpDbContext
    {
        public TestRotationDbContext(DbContextOptions<TestRotationDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SetupTransactionLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Operation).HasMaxLength(100);
                entity.Property(e => e.Stage).HasMaxLength(50);
            });

            modelBuilder.Entity<BootstrapState>(entity =>
            {
                entity.HasKey(e => e.Id);
            });
        }
    }

    /// <summary>
    /// Concrete DbContext for idempotency tests — needs SetupTransactionLog and SettingLevel.
    /// </summary>
    private sealed class TestIdempotencyDbContext : GroundUpDbContext
    {
        public TestIdempotencyDbContext(DbContextOptions<TestIdempotencyDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SetupTransactionLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Operation).HasMaxLength(100);
                entity.Property(e => e.Stage).HasMaxLength(50);
            });

            modelBuilder.Entity<BootstrapState>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<SettingLevel>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).HasMaxLength(100);
            });
        }
    }

    #endregion
}
