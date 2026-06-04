using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using GroundUp.Core.Entities;
using GroundUp.Sample.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// Integration tests for the POST /setup/recover/{transactionLogId} endpoint.
/// Verifies: db-pending recovery, already-completed rejection, keycloak-pending refusal,
/// and nonexistent ID handling.
/// Requirements: 13.4, 13.5, 13.6, 13.7, 13.8
/// </summary>
[Collection("SetupWizardApi")]
public sealed class SetupRecoveryIntegrationTests : IAsyncLifetime
{
    private readonly SetupWizardApiFactory _factory;
    private HttpClient _client = null!;

    public SetupRecoveryIntegrationTests(SetupWizardApiFactory factory)
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
            new AuthenticationHeaderValue("Bearer", SetupWizardApiFactory.TestBootstrapToken);

        await _factory.EnsureSystemLevelSeededAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    #region db-pending Recovery → 200 (Req 13.5)

    [Fact]
    public async Task Recover_DbPendingEntry_ReturnsOkWithRecoveredTrue()
    {
        // Arrange — seed a db-pending transaction log entry
        var logId = Guid.NewGuid();
        await SeedTransactionLogEntryAsync(logId, "first-admin-create", "db-pending",
            externalUserId: Guid.NewGuid().ToString(), email: "recover-test@example.com");

        // Act
        var response = await _client.PostAsync($"/setup/recover/{logId}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RecoverResponse>();
        body.Should().NotBeNull();
        body!.Recovered.Should().BeTrue();
        body.UserId.Should().NotBeEmpty();

        // Verify the transaction log entry was updated to "completed"
        await VerifyTransactionLogStageAsync(logId, "completed");
    }

    #endregion

    #region already-completed Rejection → 409 (Req 13.6)

    [Fact]
    public async Task Recover_CompletedEntry_Returns409Conflict()
    {
        // Arrange — seed a completed transaction log entry
        var logId = Guid.NewGuid();
        await SeedTransactionLogEntryAsync(logId, "first-admin-create", "completed",
            externalUserId: Guid.NewGuid().ToString(), email: "completed@example.com");

        // Act
        var response = await _client.PostAsync($"/setup/recover/{logId}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("conflict");
        body.Message.Should().Contain("already completed");
    }

    #endregion

    #region keycloak-pending Refusal → 409 (Req 13.7)

    [Fact]
    public async Task Recover_KeycloakPendingEntry_Returns409Conflict()
    {
        // Arrange — seed a keycloak-pending transaction log entry
        var logId = Guid.NewGuid();
        await SeedTransactionLogEntryAsync(logId, "first-admin-create", "keycloak-pending",
            externalUserId: null, email: "keycloak-pending@example.com");

        // Act
        var response = await _client.PostAsync($"/setup/recover/{logId}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("conflict");
        body.Message.Should().Contain("keycloak-pending");
    }

    #endregion

    #region Nonexistent ID → 404 (Req 13.4)

    [Fact]
    public async Task Recover_NonexistentId_Returns404NotFound()
    {
        // Arrange — use a random GUID that doesn't exist in the database
        var nonexistentId = Guid.NewGuid();

        // Act
        var response = await _client.PostAsync($"/setup/recover/{nonexistentId}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Helper Methods

    private async Task SeedTransactionLogEntryAsync(
        Guid id, string operation, string stage,
        string? externalUserId, string? email)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SampleDbContext>();

        var entry = new SetupTransactionLog
        {
            Id = id,
            Operation = operation,
            Stage = stage,
            ExternalUserId = externalUserId,
            Email = email,
            CorrelationId = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "setup-wizard"
        };

        dbContext.Set<SetupTransactionLog>().Add(entry);
        await dbContext.SaveChangesAsync();
    }

    private async Task VerifyTransactionLogStageAsync(Guid id, string expectedStage)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SampleDbContext>();

        var entry = await dbContext.Set<SetupTransactionLog>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id);

        entry.Should().NotBeNull();
        entry!.Stage.Should().Be(expectedStage);
    }

    #endregion

    #region Response DTOs

    private sealed record RecoverResponse(bool Recovered, Guid UserId);
    private sealed record ErrorResponse(string Code, string Message);

    #endregion
}
