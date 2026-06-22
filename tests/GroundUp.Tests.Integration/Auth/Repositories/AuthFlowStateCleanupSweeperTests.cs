using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Repositories;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests for the AuthFlowState cleanup sweeper logic.
/// Tests the repository methods that the sweeper calls (MarkExpiredOlderThanAsync
/// and DeleteTerminalOlderThanAsync) in combination, simulating sweeper behavior
/// without starting the BackgroundService timer.
/// </summary>
[Collection("AuthPostgres")]
public sealed class AuthFlowStateCleanupSweeperTests : AuthIntegrationTestBase
{
    public AuthFlowStateCleanupSweeperTests(AuthPostgresFixture fixture) : base(fixture) { }

    private AuthFlowStateRepository CreateRepository() => new(DbContext);

    private static AuthFlowStateDto CreateDto(DateTime expiresAt) => new(
        Id: Guid.Empty,
        FlowType: FlowType.TokenRefresh,
        Status: FlowStatus.Pending,
        TenantId: null,
        InvitationId: null,
        JoinLinkId: null,
        Realm: null,
        ReturnUrl: null,
        Nonce: Guid.NewGuid().ToString("N"),
        CreatedByIp: "10.0.0.1",
        CreatedByUserAgent: "Sweeper/Test",
        ExpiresAt: expiresAt,
        ConsumedAt: null,
        TerminatedAt: null,
        FailureReason: null,
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: null);

    [Fact]
    public async Task SweeperLogic_ExpiredPendingRows_TransitionToExpired()
    {
        // Arrange — seed rows with ExpiresAt in the past (simulating stale flows)
        var repo = CreateRepository();
        var stale1 = await repo.AddAsync(CreateDto(DateTime.UtcNow.AddMinutes(-5)));
        var stale2 = await repo.AddAsync(CreateDto(DateTime.UtcNow.AddMinutes(-10)));
        var fresh = await repo.AddAsync(CreateDto(DateTime.UtcNow.AddMinutes(10)));

        // Act — simulate sweeper step 1: expire stale Pending rows
        var expireResult = await repo.MarkExpiredOlderThanAsync(DateTime.UtcNow);

        // Assert
        expireResult.Success.Should().BeTrue();
        expireResult.Data.Should().Be(2);

        var row1 = await repo.GetByIdAsync(stale1.Data!.Id);
        row1.Data!.Status.Should().Be(FlowStatus.Expired);

        var row2 = await repo.GetByIdAsync(stale2.Data!.Id);
        row2.Data!.Status.Should().Be(FlowStatus.Expired);

        var freshRow = await repo.GetByIdAsync(fresh.Data!.Id);
        freshRow.Data!.Status.Should().Be(FlowStatus.Pending);
    }

    [Fact]
    public async Task SweeperLogic_RetentionDaysZero_DeletesTerminalImmediately()
    {
        // Arrange — create and consume a row (makes it terminal)
        var repo = CreateRepository();
        var row = await repo.AddAsync(CreateDto(DateTime.UtcNow.AddMinutes(15)));
        await repo.MarkConsumedAsync(row.Data!.Id);

        // Act — simulate sweeper step 2 with RetentionDays=0 (cutoff = now)
        var retentionCutoff = DateTime.UtcNow.AddSeconds(1); // Slightly in the future
        var deleteResult = await repo.DeleteTerminalOlderThanAsync(retentionCutoff);

        // Assert
        deleteResult.Success.Should().BeTrue();
        deleteResult.Data.Should().Be(1);

        var getResult = await repo.GetByIdAsync(row.Data!.Id);
        getResult.Success.Should().BeFalse();
        getResult.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task SweeperLogic_FullCycle_ExpiresAndThenDeletes()
    {
        // Arrange — seed a stale Pending row
        var repo = CreateRepository();
        var stale = await repo.AddAsync(CreateDto(DateTime.UtcNow.AddMinutes(-5)));

        // Act — Step 1: expire
        await repo.MarkExpiredOlderThanAsync(DateTime.UtcNow);

        // Verify it's now Expired
        var afterExpire = await repo.GetByIdAsync(stale.Data!.Id);
        afterExpire.Data!.Status.Should().Be(FlowStatus.Expired);

        // Act — Step 2: delete with cutoff in the future (simulating RetentionDays=0)
        var deleteResult = await repo.DeleteTerminalOlderThanAsync(DateTime.UtcNow.AddSeconds(1));

        // Assert
        deleteResult.Success.Should().BeTrue();
        deleteResult.Data.Should().Be(1);

        var getResult = await repo.GetByIdAsync(stale.Data!.Id);
        getResult.Success.Should().BeFalse();
    }
}

