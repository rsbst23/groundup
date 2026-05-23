using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Repositories;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests for AuthFlowStateRepository.MarkConsumedAsync against real Postgres.
/// Validates atomic one-shot consumption semantics.
/// </summary>
public sealed class AuthFlowStateRepositoryMarkConsumedTests : AuthIntegrationTestBase
{
    private AuthFlowStateRepository CreateRepository() => new(DbContext);

    private static AuthFlowStateDto CreatePendingDto(TimeSpan? lifetime = null) => new(
        Id: Guid.Empty,
        FlowType: FlowType.Invitation,
        Status: FlowStatus.Pending,
        TenantId: Guid.NewGuid(),
        InvitationId: Guid.NewGuid(),
        JoinLinkId: null,
        Realm: "groundup",
        ReturnUrl: null,
        Nonce: Guid.NewGuid().ToString("N"),
        CreatedByIp: "10.0.0.1",
        CreatedByUserAgent: "TestAgent/2.0",
        ExpiresAt: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(15)),
        ConsumedAt: null,
        TerminatedAt: null,
        FailureReason: null,
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: null);

    [Fact]
    public async Task MarkConsumedAsync_PendingAndNotExpired_ReturnsConsumed()
    {
        // Arrange
        var repo = CreateRepository();
        var created = await repo.AddAsync(CreatePendingDto());
        var id = created.Data!.Id;

        // Act
        var result = await repo.MarkConsumedAsync(id);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be(FlowStatus.Consumed);
        result.Data.ConsumedAt.Should().NotBeNull();
        result.Data.TerminatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkConsumedAsync_AlreadyConsumed_ReturnsConflict409()
    {
        // Arrange
        var repo = CreateRepository();
        var created = await repo.AddAsync(CreatePendingDto());
        var id = created.Data!.Id;
        await repo.MarkConsumedAsync(id); // First consumption

        // Act
        var result = await repo.MarkConsumedAsync(id); // Second attempt

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task MarkConsumedAsync_Expired_ReturnsBadRequest400()
    {
        // Arrange
        var repo = CreateRepository();
        // Create with ExpiresAt in the past
        var expiredDto = CreatePendingDto(TimeSpan.FromMinutes(-1));
        var created = await repo.AddAsync(expiredDto);
        var id = created.Data!.Id;

        // Act
        var result = await repo.MarkConsumedAsync(id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("expired");
    }

    [Fact]
    public async Task MarkConsumedAsync_NonExistentId_ReturnsNotFound404()
    {
        // Arrange
        var repo = CreateRepository();

        // Act
        var result = await repo.MarkConsumedAsync(Guid.NewGuid());

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task MarkConsumedAsync_AlreadyFailed_ReturnsConflict409()
    {
        // Arrange
        var repo = CreateRepository();
        var created = await repo.AddAsync(CreatePendingDto());
        var id = created.Data!.Id;
        await repo.MarkFailedAsync(id, "test failure");

        // Act
        var result = await repo.MarkConsumedAsync(id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }
}
