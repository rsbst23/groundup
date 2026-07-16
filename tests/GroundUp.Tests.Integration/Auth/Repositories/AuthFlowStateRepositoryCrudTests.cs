using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Repositories;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests for AuthFlowStateRepository standard CRUD operations
/// (Add, GetById, Update, Delete) against a real Postgres database.
/// </summary>
[Collection("AuthPostgres")]
public sealed class AuthFlowStateRepositoryCrudTests : AuthIntegrationTestBase
{
    public AuthFlowStateRepositoryCrudTests(AuthPostgresFixture fixture) : base(fixture) { }

    private AuthFlowStateRepository CreateRepository() => new(DbContext);

    private static AuthFlowStateDto CreateValidDto() => new(
        Id: Guid.Empty,
        FlowType: FlowType.NewOrganization,
        Status: FlowStatus.Pending,
        TenantId: Guid.NewGuid(),
        InvitationId: null,
        JoinLinkId: null,
        Realm: "groundup",
        ReturnUrl: "https://app.example.com/callback",
        StateToken: Guid.NewGuid().ToString("N"),
        CodeVerifier: "integration-test-code-verifier-1234567890123",
        RedirectUri: "https://app.example.com/auth/callback",
        OrganizationName: null,
        Nonce: Guid.NewGuid().ToString("N"),
        CreatedByIp: "192.168.1.1",
        CreatedByUserAgent: "TestAgent/1.0",
        ExpiresAt: DateTime.UtcNow.AddMinutes(15),
        ConsumedAt: null,
        TerminatedAt: null,
        FailureReason: null,
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: null);

    [Fact]
    public async Task AddAsync_ValidDto_ReturnsSuccessWithGeneratedId()
    {
        // Arrange
        var repo = CreateRepository();
        var dto = CreateValidDto();

        // Act
        var result = await repo.AddAsync(dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().NotBe(Guid.Empty);
        result.Data.FlowType.Should().Be(FlowType.NewOrganization);
        result.Data.Status.Should().Be(FlowStatus.Pending);
        result.Data.Nonce.Should().Be(dto.Nonce);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingRow_ReturnsDto()
    {
        // Arrange
        var repo = CreateRepository();
        var created = await repo.AddAsync(CreateValidDto());
        var id = created.Data!.Id;

        // Act
        var result = await repo.GetByIdAsync(id);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(id);
        result.Data.FlowType.Should().Be(FlowType.NewOrganization);
        result.Data.Realm.Should().Be("groundup");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNotFound()
    {
        // Arrange
        var repo = CreateRepository();

        // Act
        var result = await repo.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task UpdateAsync_ExistingRow_UpdatesFields()
    {
        // Arrange
        var repo = CreateRepository();
        var created = await repo.AddAsync(CreateValidDto());
        var id = created.Data!.Id;

        var updated = created.Data with { ReturnUrl = "https://updated.example.com" };

        // Act
        var result = await repo.UpdateAsync(id, updated);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.ReturnUrl.Should().Be("https://updated.example.com");
    }

    [Fact]
    public async Task DeleteAsync_ExistingRow_RemovesFromDatabase()
    {
        // Arrange
        var repo = CreateRepository();
        var created = await repo.AddAsync(CreateValidDto());
        var id = created.Data!.Id;

        // Act
        var deleteResult = await repo.DeleteAsync(id);

        // Assert
        deleteResult.Success.Should().BeTrue();

        var getResult = await repo.GetByIdAsync(id);
        getResult.Success.Should().BeFalse();
        getResult.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_ReturnsNotFound()
    {
        // Arrange
        var repo = CreateRepository();

        // Act
        var result = await repo.DeleteAsync(Guid.NewGuid());

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}

