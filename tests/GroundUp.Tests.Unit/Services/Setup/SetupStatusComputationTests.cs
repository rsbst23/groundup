using FluentAssertions;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Configuration;
using GroundUp.Core.Entities;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Results;
using GroundUp.Data.Postgres;
using GroundUp.Services.Setup;
using GroundUp.Services.Setup.Keycloak;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Setup;

/// <summary>
/// Unit tests for SetupWizardService.GetStatusAsync — verifying all flag combinations,
/// currentStep transitions, and firstAdminPending detection.
/// _Requirements: 15.3–15.9_
/// </summary>
public sealed class SetupStatusComputationTests : IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IBootstrapStateService _bootstrapStateService;
    private readonly IIdentityBootstrapService _identityBootstrapService;
    private readonly IIdentityProviderAdminService _identityProviderAdminService;
    private readonly string _dbName;

    public SetupStatusComputationTests()
    {
        _settingsService = Substitute.For<ISettingsService>();
        _bootstrapStateService = Substitute.For<IBootstrapStateService>();
        _identityBootstrapService = Substitute.For<IIdentityBootstrapService>();
        _identityProviderAdminService = Substitute.For<IIdentityProviderAdminService>();
        _dbName = Guid.NewGuid().ToString();
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last context referencing them is disposed
    }

    #region currentStep Transitions (Requirement 15.9)

    [Fact]
    public async Task GetStatusAsync_NoStepsCompleted_CurrentStepIsAppIdentity()
    {
        // Arrange
        ConfigureFlags(appIdentity: false, idp: false, keycloak: false, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.CurrentStep.Should().Be("app-identity");
    }

    [Fact]
    public async Task GetStatusAsync_AppIdentityCompleted_CurrentStepIsIdentityProvider()
    {
        // Arrange
        ConfigureFlags(appIdentity: true, idp: false, keycloak: false, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.CurrentStep.Should().Be("identity-provider");
    }

    [Fact]
    public async Task GetStatusAsync_AppIdentityAndIdpCompleted_CurrentStepIsKeycloakBootstrap()
    {
        // Arrange
        ConfigureFlags(appIdentity: true, idp: true, keycloak: false, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.CurrentStep.Should().Be("keycloak-bootstrap");
    }

    [Fact]
    public async Task GetStatusAsync_FirstThreeStepsCompleted_CurrentStepIsFirstAdmin()
    {
        // Arrange
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.CurrentStep.Should().Be("first-admin");
    }

    [Fact]
    public async Task GetStatusAsync_AllStepsCompletedButNotFinalized_CurrentStepIsComplete()
    {
        // Arrange
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: true, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.CurrentStep.Should().Be("complete");
    }

    [Fact]
    public async Task GetStatusAsync_SetupIsComplete_CurrentStepIsDone()
    {
        // Arrange
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: true, isComplete: true);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.CurrentStep.Should().Be("done");
    }

    [Fact]
    public async Task GetStatusAsync_IsCompleteTrue_OverridesAllOtherFlags_CurrentStepIsDone()
    {
        // Even if flags are false, isComplete=true means currentStep="done"
        ConfigureFlags(appIdentity: false, idp: false, keycloak: false, superAdmin: false, isComplete: true);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.CurrentStep.Should().Be("done");
        result.Data!.IsComplete.Should().BeTrue();
    }

    #endregion

    #region Flag Computation (Requirements 15.4–15.8)

    [Fact]
    public async Task GetStatusAsync_AppIdentitySettingsPresent_AppIdentityCompletedTrue()
    {
        // Arrange — Requirement 15.4
        ConfigureFlags(appIdentity: true, idp: false, keycloak: false, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.AppIdentityCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_AppIdentitySettingsMissing_AppIdentityCompletedFalse()
    {
        // Arrange — Requirement 15.4 (negative)
        ConfigureFlags(appIdentity: false, idp: false, keycloak: false, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.AppIdentityCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_IdentityProviderSettingsPresent_IdentityProviderCompletedTrue()
    {
        // Arrange — Requirement 15.5
        ConfigureFlags(appIdentity: true, idp: true, keycloak: false, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.IdentityProviderCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_IdentityProviderSettingsMissing_IdentityProviderCompletedFalse()
    {
        // Arrange — Requirement 15.5 (negative)
        ConfigureFlags(appIdentity: true, idp: false, keycloak: false, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.IdentityProviderCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_KeycloakBootstrapSettingsPresent_KeycloakBootstrapCompletedTrue()
    {
        // Arrange — Requirement 15.6
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.KeycloakBootstrapCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_KeycloakBootstrapSettingsMissing_KeycloakBootstrapCompletedFalse()
    {
        // Arrange — Requirement 15.6 (negative)
        ConfigureFlags(appIdentity: true, idp: true, keycloak: false, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.KeycloakBootstrapCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_SuperAdminExistsNoPending_FirstAdminCompletedTrue()
    {
        // Arrange — Requirement 15.7
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: true, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.FirstAdminCompleted.Should().BeTrue();
        result.Data!.FirstAdminPending.Should().BeFalse();
        result.Data!.FirstAdminPendingTransactionLogId.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_NoSuperAdmin_FirstAdminCompletedFalse()
    {
        // Arrange — Requirement 15.7 (negative: no super admin)
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.FirstAdminCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_IsCompleteFlag_ReflectsBootstrapState()
    {
        // Arrange — Requirement 15.8
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: true, isComplete: true);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.IsComplete.Should().BeTrue();
    }

    #endregion

    #region firstAdminPending Detection (Requirement 15.7)

    [Fact]
    public async Task GetStatusAsync_PendingKeycloakRow_FirstAdminPendingTrue()
    {
        // Arrange — Requirement 15.7: pending transaction log row makes firstAdminCompleted=false
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: true, isComplete: false);
        var pendingLogId = Guid.NewGuid();
        var sut = CreateServiceWithPendingTransactionLog(pendingLogId, "keycloak-pending");

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.FirstAdminCompleted.Should().BeFalse();
        result.Data!.FirstAdminPending.Should().BeTrue();
        result.Data!.FirstAdminPendingTransactionLogId.Should().Be(pendingLogId.ToString());
        result.Data!.CurrentStep.Should().Be("first-admin");
    }

    [Fact]
    public async Task GetStatusAsync_PendingDbRow_FirstAdminPendingTrue()
    {
        // Arrange — "db-pending" stage also triggers pending detection
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: true, isComplete: false);
        var pendingLogId = Guid.NewGuid();
        var sut = CreateServiceWithPendingTransactionLog(pendingLogId, "db-pending");

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.FirstAdminCompleted.Should().BeFalse();
        result.Data!.FirstAdminPending.Should().BeTrue();
        result.Data!.FirstAdminPendingTransactionLogId.Should().Be(pendingLogId.ToString());
    }

    [Fact]
    public async Task GetStatusAsync_FailedRow_FirstAdminPendingTrue()
    {
        // Arrange — "failed" stage also triggers pending detection
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: true, isComplete: false);
        var pendingLogId = Guid.NewGuid();
        var sut = CreateServiceWithPendingTransactionLog(pendingLogId, "failed");

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.FirstAdminCompleted.Should().BeFalse();
        result.Data!.FirstAdminPending.Should().BeTrue();
        result.Data!.FirstAdminPendingTransactionLogId.Should().Be(pendingLogId.ToString());
    }

    [Fact]
    public async Task GetStatusAsync_CompletedTransactionLog_DoesNotTriggerPending()
    {
        // Arrange — "completed" stage should NOT trigger pending detection
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: true, isComplete: false);
        var sut = CreateServiceWithCompletedTransactionLog();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.FirstAdminCompleted.Should().BeTrue();
        result.Data!.FirstAdminPending.Should().BeFalse();
        result.Data!.FirstAdminPendingTransactionLogId.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_SuperAdminExistsButPendingRow_FirstAdminCompletedFalse()
    {
        // Arrange — Requirement 15.7: even if SuperAdmin exists, pending row means NOT completed
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: true, isComplete: false);
        var pendingLogId = Guid.NewGuid();
        var sut = CreateServiceWithPendingTransactionLog(pendingLogId, "db-pending");

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.FirstAdminCompleted.Should().BeFalse();
        result.Data!.FirstAdminPending.Should().BeTrue();
        result.Data!.CurrentStep.Should().Be("first-admin");
    }

    [Fact]
    public async Task GetStatusAsync_NoSuperAdminNoPending_FirstAdminPendingFalse()
    {
        // Arrange — No super admin and no pending row: firstAdminPending=false
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: false, isComplete: false);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.FirstAdminCompleted.Should().BeFalse();
        result.Data!.FirstAdminPending.Should().BeFalse();
        result.Data!.FirstAdminPendingTransactionLogId.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_MultiplePendingRows_ReturnsMostRecent()
    {
        // Arrange — When multiple pending rows exist, the most recent (by CreatedAt) is returned
        ConfigureFlags(appIdentity: true, idp: true, keycloak: true, superAdmin: true, isComplete: false);

        var olderLogId = Guid.NewGuid();
        var newerLogId = Guid.NewGuid();
        var sut = CreateServiceWithMultiplePendingLogs(olderLogId, newerLogId);

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Data!.FirstAdminPending.Should().BeTrue();
        result.Data!.FirstAdminPendingTransactionLogId.Should().Be(newerLogId.ToString());
    }

    #endregion

    #region All Flag Combinations — Comprehensive (Requirement 15.3)

    [Theory]
    [InlineData(false, false, false, false, false, "app-identity")]
    [InlineData(true, false, false, false, false, "identity-provider")]
    [InlineData(true, true, false, false, false, "keycloak-bootstrap")]
    [InlineData(true, true, true, false, false, "first-admin")]
    [InlineData(true, true, true, true, false, "complete")]
    [InlineData(true, true, true, true, true, "done")]
    [InlineData(false, true, true, true, false, "app-identity")]   // Gap: app-identity missing
    [InlineData(true, false, true, true, false, "identity-provider")] // Gap: idp missing
    [InlineData(true, true, false, true, false, "keycloak-bootstrap")] // Gap: keycloak missing
    public async Task GetStatusAsync_FlagCombinations_ProducesCorrectCurrentStep(
        bool appIdentity, bool idp, bool keycloak, bool superAdmin, bool isComplete, string expectedStep)
    {
        // Arrange
        ConfigureFlags(appIdentity, idp, keycloak, superAdmin, isComplete);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.CurrentStep.Should().Be(expectedStep);
    }

    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, true, true, true, true)]
    public async Task GetStatusAsync_FlagCombinations_ReturnsCorrectBooleanFlags(
        bool appIdentity, bool idp, bool keycloak, bool superAdmin, bool isComplete)
    {
        // Arrange
        ConfigureFlags(appIdentity, idp, keycloak, superAdmin, isComplete);
        var sut = CreateService();

        // Act
        var result = await sut.GetStatusAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.AppIdentityCompleted.Should().Be(appIdentity);
        result.Data!.IdentityProviderCompleted.Should().Be(idp);
        result.Data!.KeycloakBootstrapCompleted.Should().Be(keycloak);
        result.Data!.FirstAdminCompleted.Should().Be(superAdmin);
        result.Data!.IsComplete.Should().Be(isComplete);
    }

    #endregion

    #region Helpers

    private void ConfigureFlags(bool appIdentity, bool idp, bool keycloak, bool superAdmin, bool isComplete)
    {
        _bootstrapStateService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(isComplete));

        // App identity: app.identity.name must be non-null/non-empty; auth.application.default-domain must succeed
        if (appIdentity)
        {
            _settingsService.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok("Test App")));
            _settingsService.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok("example.com")));
        }
        else
        {
            _settingsService.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok((string)null!)));
            _settingsService.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok((string)null!)));
        }

        // Identity provider: public-base-url and shared-realm-name must be non-null/non-empty
        if (idp)
        {
            _settingsService.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok("https://keycloak.example.com")));
            _settingsService.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok("groundup")));
        }
        else
        {
            _settingsService.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok((string)null!)));
            _settingsService.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok((string)null!)));
        }

        // Keycloak bootstrap: admin-client-id and admin-client-secret must be non-null/non-empty
        if (keycloak)
        {
            _settingsService.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok("groundup-admin-client")));
            _settingsService.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok("secret-value")));
        }
        else
        {
            _settingsService.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok((string)null!)));
            _settingsService.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(OperationResult<string>.Ok((string)null!)));
        }

        // Super admin
        _identityBootstrapService.HasSuperAdminAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(superAdmin));
    }

    private SetupWizardService CreateService()
    {
        var dbContext = CreateDbContext(_dbName);
        return CreateWizardService(dbContext);
    }

    private SetupWizardService CreateServiceWithPendingTransactionLog(Guid logId, string stage)
    {
        var dbContext = CreateDbContext(_dbName);
        dbContext.SetupTransactionLogs.Add(new SetupTransactionLog
        {
            Id = logId,
            Operation = "first-admin-create",
            Stage = stage,
            Email = "admin@example.com",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "setup-wizard"
        });
        dbContext.SaveChanges();

        return CreateWizardService(dbContext);
    }

    private SetupWizardService CreateServiceWithCompletedTransactionLog()
    {
        var dbContext = CreateDbContext(_dbName);
        dbContext.SetupTransactionLogs.Add(new SetupTransactionLog
        {
            Id = Guid.NewGuid(),
            Operation = "first-admin-create",
            Stage = "completed",
            Email = "admin@example.com",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "setup-wizard"
        });
        dbContext.SaveChanges();

        return CreateWizardService(dbContext);
    }

    private SetupWizardService CreateServiceWithMultiplePendingLogs(Guid olderLogId, Guid newerLogId)
    {
        var dbContext = CreateDbContext(_dbName);
        dbContext.SetupTransactionLogs.Add(new SetupTransactionLog
        {
            Id = olderLogId,
            Operation = "first-admin-create",
            Stage = "keycloak-pending",
            Email = "admin@example.com",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            CreatedBy = "setup-wizard"
        });
        dbContext.SetupTransactionLogs.Add(new SetupTransactionLog
        {
            Id = newerLogId,
            Operation = "first-admin-create",
            Stage = "db-pending",
            Email = "admin@example.com",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "setup-wizard"
        });
        dbContext.SaveChanges();

        return CreateWizardService(dbContext);
    }

    private SetupWizardService CreateWizardService(StatusTestDbContext dbContext)
    {
        var transactionLogOptions = Options.Create(new SetupTransactionLogOptions { MaxRowCount = 1000 });
        var keycloakClient = new KeycloakAdminHttpClient(
            new HttpClient(), NullLogger<KeycloakAdminHttpClient>.Instance);
        var bootstrapOptions = Options.Create(new BootstrapOptions());

        return new SetupWizardService(
            _settingsService,
            _bootstrapStateService,
            dbContext,
            transactionLogOptions,
            keycloakClient,
            bootstrapOptions,
            _identityBootstrapService,
            _identityProviderAdminService,
            NullLogger<SetupWizardService>.Instance);
    }

    private static StatusTestDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<StatusTestDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new StatusTestDbContext(options);
    }

    #endregion

    #region Test DbContext

    private sealed class StatusTestDbContext(DbContextOptions<StatusTestDbContext> options)
        : GroundUpDbContext(options)
    {
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
