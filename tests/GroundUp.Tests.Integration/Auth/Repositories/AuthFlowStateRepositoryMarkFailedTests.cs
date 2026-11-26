using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests for AuthFlowStateRepository.MarkFailedAsync against real Postgres.
/// Validates failure marking semantics and terminal state rejection.
/// </summary>
[Collection("AuthPostgres")]
public sealed class AuthFlowStateRepositoryMarkFailedTests : AuthIntegrationTestBase
{
    public AuthFlowStateRepositoryMarkFailedTests(AuthPostgresFixture fixture) : base(fixture) { }

    private AuthFlowStateRepository CreateRepository() => new(DbContext);

    private static AuthFlowStateDto CreatePendingDto() => new(
        Id: Guid.Empty,
        FlowType: FlowType.JoinLink,
        Status: FlowStatus.Pending,
        TenantId: Guid.NewGuid(),
        InvitationId: null,
        JoinLinkId: Guid.NewGuid(),
        Realm: null,
        ReturnUrl: null,
        StateToken: Guid.NewGuid().ToString("N"),
        CodeVerifier: "integration-test-code-verifier-1234567890123",
        RedirectUri: "https://example.com/auth/callback",
        OrganizationName: null,
        Nonce: Guid.NewGuid().ToString("N"),
        CreatedByIp: "172.16.0.1",
        CreatedByUserAgent: "TestAgent/3.0",
        ExpiresAt: DateTime.UtcNow.AddMinutes(15),
        ConsumedAt: null,
        TerminatedAt: null,
        FailureReason: null,
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: null);

    [Fact]
    public async Task MarkFailedAsync_PendingRow_ReturnsFailedWithReason()
    {
        // Arrange
        var repo = CreateRepository();
        var created = await repo.AddAsync(CreatePendingDto());
        var id = created.Data!.Id;

        // Act
        var result = await repo.MarkFailedAsync(id, "Token exchange failed");

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be(FlowStatus.Failed);
        result.Data.FailureReason.Should().Be("Token exchange failed");
        result.Data.TerminatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkFailedAsync_AlreadyConsumed_ReturnsConflict409()
    {
        // Arrange
        var repo = CreateRepository();
        var created = await repo.AddAsync(CreatePendingDto());
        var id = created.Data!.Id;
        await repo.MarkConsumedAsync(id);

        // Act
        var result = await repo.MarkFailedAsync(id, "Should not work");

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task MarkFailedAsync_AlreadyConsumed_LeavesStoredRowConsumed()
    {
        // Arrange: same repository and context throughout, so a tracked copy of the row exists
        var repo = CreateRepository();
        var created = await repo.AddAsync(CreatePendingDto());
        var id = created.Data!.Id;
        await repo.MarkConsumedAsync(id);

        // Act
        await repo.MarkFailedAsync(id, "Should not work");

        // Assert: read from a fresh context so the database row is checked, not a tracked copy
        await using var verify = Fixture.CreateContext();
        var stored = await verify.AuthFlowStates.AsNoTracking().SingleAsync(x => x.Id == id);
        stored.Status.Should().Be(FlowStatus.Consumed);
        stored.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task MarkFailedAsync_AlreadyExpired_ReturnsConflict409AndLeavesRowExpired()
    {
        // Arrange: a Pending row past its expiry, swept to Expired by the bulk operation
        var repo = CreateRepository();
        var dto = CreatePendingDto() with { ExpiresAt = DateTime.UtcNow.AddMinutes(-5) };
        var created = await repo.AddAsync(dto);
        var id = created.Data!.Id;
        await repo.MarkExpiredOlderThanAsync(DateTime.UtcNow);

        // Act
        var result = await repo.MarkFailedAsync(id, "Should not work");

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);

        await using var verify = Fixture.CreateContext();
        var stored = await verify.AuthFlowStates.AsNoTracking().SingleAsync(x => x.Id == id);
        stored.Status.Should().Be(FlowStatus.Expired);
        stored.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task MarkFailedAsync_AlreadyFailed_ReturnsConflict409()
    {
        // Arrange
        var repo = CreateRepository();
        var created = await repo.AddAsync(CreatePendingDto());
        var id = created.Data!.Id;
        await repo.MarkFailedAsync(id, "First failure");

        // Act
        var result = await repo.MarkFailedAsync(id, "Second failure");

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task MarkFailedAsync_NonExistentId_ReturnsNotFound404()
    {
        // Arrange
        var repo = CreateRepository();

        // Act
        var result = await repo.MarkFailedAsync(Guid.NewGuid(), "Does not exist");

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task MarkFailedAsync_LongReason_TruncatesTo1024Chars()
    {
        // Arrange
        var repo = CreateRepository();
        var created = await repo.AddAsync(CreatePendingDto());
        var id = created.Data!.Id;
        var longReason = new string('x', 2000);

        // Act
        var result = await repo.MarkFailedAsync(id, longReason);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.FailureReason!.Length.Should().Be(1024);
    }
}

