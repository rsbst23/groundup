using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Auth.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Auth.Services;

/// <summary>
/// Property-based tests verifying sweeper expiration logic:
/// for any random mix of expired/non-expired Pending rows, the sweep
/// transitions only expired Pending rows and leaves others untouched.
/// </summary>
public sealed class AuthFlowStateSweeperExpirationPropertyTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private AuthDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new AuthDbContext(options);
    }

    /// <summary>
    /// Property: For any random mix of rows (some expired, some not),
    /// MarkExpiredOlderThanAsync transitions only expired Pending rows.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Sweep_OnlyExpiredPendingRowsTransition(
        PositiveInt expiredCountRaw,
        PositiveInt freshCountRaw)
    {
        var expiredCount = (expiredCountRaw.Get % 5) + 1; // 1–5
        var freshCount = (freshCountRaw.Get % 5) + 1;     // 1–5

        var now = DateTime.UtcNow;
        var expiredIds = new List<Guid>();
        var freshIds = new List<Guid>();

        using (var ctx = CreateContext())
        {
            var repo = new AuthFlowStateRepository(ctx);

            // Seed expired rows (ExpiresAt in the past)
            for (var i = 0; i < expiredCount; i++)
            {
                var dto = CreateDto(now.AddMinutes(-(i + 1)));
                var result = repo.AddAsync(dto).GetAwaiter().GetResult();
                expiredIds.Add(result.Data!.Id);
            }

            // Seed fresh rows (ExpiresAt in the future)
            for (var i = 0; i < freshCount; i++)
            {
                var dto = CreateDto(now.AddMinutes(i + 10));
                var result = repo.AddAsync(dto).GetAwaiter().GetResult();
                freshIds.Add(result.Data!.Id);
            }

            // Act — sweep
            var sweepResult = repo.MarkExpiredOlderThanAsync(now).GetAwaiter().GetResult();

            // Verify count
            if (!sweepResult.Success || sweepResult.Data != expiredCount)
                return false.ToProperty();

            // Verify expired rows are now Expired
            foreach (var id in expiredIds)
            {
                var row = repo.GetByIdAsync(id).GetAwaiter().GetResult();
                if (!row.Success || row.Data!.Status != FlowStatus.Expired)
                    return false.ToProperty();
            }

            // Verify fresh rows are still Pending
            foreach (var id in freshIds)
            {
                var row = repo.GetByIdAsync(id).GetAwaiter().GetResult();
                if (!row.Success || row.Data!.Status != FlowStatus.Pending)
                    return false.ToProperty();
            }
        }

        return true.ToProperty();
    }

    private static AuthFlowStateDto CreateDto(DateTime expiresAt) => new(
        Id: Guid.Empty,
        FlowType: FlowType.NewOrganization,
        Status: FlowStatus.Pending,
        TenantId: null,
        InvitationId: null,
        JoinLinkId: null,
        Realm: null,
        ReturnUrl: null,
        Nonce: Guid.NewGuid().ToString("N"),
        CreatedByIp: "10.0.0.1",
        CreatedByUserAgent: "SweeperPropTest",
        ExpiresAt: expiresAt,
        ConsumedAt: null,
        TerminatedAt: null,
        FailureReason: null,
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: null);
}
