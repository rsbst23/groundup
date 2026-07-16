using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Repositories;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests for AuthFlowStateRepository bulk operations
/// (MarkExpiredOlderThanAsync, DeleteTerminalOlderThanAsync) against real Postgres.
/// </summary>
[Collection("AuthPostgres")]
public sealed class AuthFlowStateRepositoryBulkOperationsTests : AuthIntegrationTestBase
{
    public AuthFlowStateRepositoryBulkOperationsTests(AuthPostgresFixture fixture) : base(fixture) { }

    private AuthFlowStateRepository CreateRepository() => new(DbContext);

    private static AuthFlowStateDto CreateDto(FlowStatus status, DateTime expiresAt) => new(
        Id: Guid.Empty,
        FlowType: FlowType.NewOrganization,
        Status: status,
        TenantId: null,
        InvitationId: null,
        JoinLinkId: null,
        Realm: null,
        ReturnUrl: null,
        StateToken: Guid.NewGuid().ToString("N"),
        CodeVerifier: "integration-test-code-verifier-1234567890123",
        RedirectUri: "https://example.com/auth/callback",
        OrganizationName: null,
        Nonce: Guid.NewGuid().ToString("N"),
        CreatedByIp: "10.0.0.1",
        CreatedByUserAgent: "TestAgent/1.0",
        ExpiresAt: expiresAt,
        ConsumedAt: null,
        TerminatedAt: null,
        FailureReason: null,
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: null);

    #region MarkExpiredOlderThanAsync

    [Fact]
    public async Task MarkExpiredOlderThanAsync_PendingRowsPastCutoff_TransitionsToExpired()
    {
        // Arrange
        var repo = CreateRepository();
        var cutoff = DateTime.UtcNow;

        // Expired row (ExpiresAt in the past)
        var expired = await repo.AddAsync(CreateDto(FlowStatus.Pending, cutoff.AddMinutes(-10)));
        // Still valid row (ExpiresAt in the future)
        var valid = await repo.AddAsync(CreateDto(FlowStatus.Pending, cutoff.AddMinutes(10)));

        // Act
        var result = await repo.MarkExpiredOlderThanAsync(cutoff);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(1);

        var expiredRow = await repo.GetByIdAsync(expired.Data!.Id);
        expiredRow.Data!.Status.Should().Be(FlowStatus.Expired);
        expiredRow.Data.TerminatedAt.Should().NotBeNull();

        var validRow = await repo.GetByIdAsync(valid.Data!.Id);
        validRow.Data!.Status.Should().Be(FlowStatus.Pending);
    }

    [Fact]
    public async Task MarkExpiredOlderThanAsync_NonPendingRows_AreUntouched()
    {
        // Arrange
        var repo = CreateRepository();

        // Use a far-future cutoff so only our specific row is in play
        var cutoff = DateTime.UtcNow.AddHours(1);

        // Create a row with ExpiresAt before cutoff, then consume it (terminal state)
        var consumed = await repo.AddAsync(CreateDto(FlowStatus.Pending, cutoff.AddMinutes(-10)));
        await repo.MarkConsumedAsync(consumed.Data!.Id);

        // Count existing pending expired rows before our act (from other tests in same DB)
        // (This line is just for understanding — we don't actually need the baseline count)

        // Act — sweep with our cutoff
        var result = await repo.MarkExpiredOlderThanAsync(cutoff);

        // Assert — no new pending rows should have been swept (our row was already consumed)
        // Any rows swept were pending leftovers from other tests, so just verify our consumed row wasn't touched
        var row = await repo.GetByIdAsync(consumed.Data!.Id);
        row.Data!.Status.Should().Be(FlowStatus.Consumed);
    }

    #endregion

    #region DeleteTerminalOlderThanAsync

    [Fact]
    public async Task DeleteTerminalOlderThanAsync_TerminalRowsPastCutoff_AreDeleted()
    {
        // Arrange
        var repo = CreateRepository();

        // Create and consume a row (makes it terminal)
        var row = await repo.AddAsync(CreateDto(FlowStatus.Pending, DateTime.UtcNow.AddMinutes(15)));
        await repo.MarkConsumedAsync(row.Data!.Id);

        // Use a cutoff in the future to ensure the row is "old enough"
        var cutoff = DateTime.UtcNow.AddMinutes(1);

        // Act
        var result = await repo.DeleteTerminalOlderThanAsync(cutoff);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(1);

        var getResult = await repo.GetByIdAsync(row.Data!.Id);
        getResult.Success.Should().BeFalse();
        getResult.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteTerminalOlderThanAsync_PendingRows_AreUntouched()
    {
        // Arrange
        var repo = CreateRepository();

        // Create a Pending row (not terminal)
        var pending = await repo.AddAsync(CreateDto(FlowStatus.Pending, DateTime.UtcNow.AddMinutes(-10)));

        // Use a cutoff in the future
        var cutoff = DateTime.UtcNow.AddMinutes(1);

        // Act
        var result = await repo.DeleteTerminalOlderThanAsync(cutoff);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(0);

        var getResult = await repo.GetByIdAsync(pending.Data!.Id);
        getResult.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteTerminalOlderThanAsync_RecentTerminalRows_AreUntouched()
    {
        // Arrange
        var repo = CreateRepository();

        // Create and consume a row (makes it terminal)
        var row = await repo.AddAsync(CreateDto(FlowStatus.Pending, DateTime.UtcNow.AddMinutes(15)));
        await repo.MarkConsumedAsync(row.Data!.Id);

        // Use a cutoff in the past (row is too recent)
        var cutoff = DateTime.UtcNow.AddMinutes(-10);

        // Act
        var result = await repo.DeleteTerminalOlderThanAsync(cutoff);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(0);

        var getResult = await repo.GetByIdAsync(row.Data!.Id);
        getResult.Success.Should().BeTrue();
    }

    #endregion
}

