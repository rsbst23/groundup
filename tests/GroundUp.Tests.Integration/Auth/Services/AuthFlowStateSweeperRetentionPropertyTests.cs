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
/// Property-based tests verifying sweeper retention logic:
/// for any random mix of terminal rows (old/recent), the sweep
/// deletes only old terminal rows and leaves recent ones untouched.
/// </summary>
public sealed class AuthFlowStateSweeperRetentionPropertyTests : IAsyncLifetime
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
    /// Property: For any random mix of terminal rows (some old, some recent),
    /// DeleteTerminalOlderThanAsync deletes only old terminal rows.
    /// Pending rows are never deleted regardless of age.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Sweep_OnlyOldTerminalRowsDeleted(
        PositiveInt oldCountRaw,
        PositiveInt recentCountRaw,
        PositiveInt pendingCountRaw)
    {
        var oldCount = (oldCountRaw.Get % 4) + 1;     // 1–4
        var recentCount = (recentCountRaw.Get % 4) + 1; // 1–4
        var pendingCount = (pendingCountRaw.Get % 3) + 1; // 1–3

        var now = DateTime.UtcNow;
        var retentionCutoff = now.AddMinutes(-5); // 5 minutes ago

        var oldIds = new List<Guid>();
        var recentIds = new List<Guid>();
        var pendingIds = new List<Guid>();

        using (var ctx = CreateContext())
        {
            var repo = new AuthFlowStateRepository(ctx);

            // Seed old terminal rows (consumed, with TerminatedAt before cutoff)
            for (var i = 0; i < oldCount; i++)
            {
                var dto = CreateDto(DateTime.UtcNow.AddMinutes(15));
                var created = repo.AddAsync(dto).GetAwaiter().GetResult();
                // Consume to make terminal
                repo.MarkConsumedAsync(created.Data!.Id).GetAwaiter().GetResult();

                // Manually backdate TerminatedAt to before the cutoff
                var entity = ctx.AuthFlowStates.Find(created.Data!.Id)!;
                entity.TerminatedAt = retentionCutoff.AddMinutes(-(i + 1));
                ctx.SaveChanges();

                oldIds.Add(created.Data!.Id);
            }

            // Seed recent terminal rows (consumed, with TerminatedAt after cutoff)
            for (var i = 0; i < recentCount; i++)
            {
                var dto = CreateDto(DateTime.UtcNow.AddMinutes(15));
                var created = repo.AddAsync(dto).GetAwaiter().GetResult();
                repo.MarkConsumedAsync(created.Data!.Id).GetAwaiter().GetResult();
                // TerminatedAt is set to ~now by MarkConsumedAsync, which is after cutoff
                recentIds.Add(created.Data!.Id);
            }

            // Seed Pending rows (should never be deleted)
            for (var i = 0; i < pendingCount; i++)
            {
                var dto = CreateDto(DateTime.UtcNow.AddMinutes(-10)); // expired but still Pending
                var created = repo.AddAsync(dto).GetAwaiter().GetResult();
                pendingIds.Add(created.Data!.Id);
            }
        }

        // Act — delete with a fresh context
        using (var ctx = CreateContext())
        {
            var repo = new AuthFlowStateRepository(ctx);
            var deleteResult = repo.DeleteTerminalOlderThanAsync(retentionCutoff).GetAwaiter().GetResult();

            if (!deleteResult.Success || deleteResult.Data != oldCount)
                return false.ToProperty();

            // Verify old terminal rows are deleted
            foreach (var id in oldIds)
            {
                var row = repo.GetByIdAsync(id).GetAwaiter().GetResult();
                if (row.Success)
                    return false.ToProperty();
            }

            // Verify recent terminal rows still exist
            foreach (var id in recentIds)
            {
                var row = repo.GetByIdAsync(id).GetAwaiter().GetResult();
                if (!row.Success || row.Data!.Status != FlowStatus.Consumed)
                    return false.ToProperty();
            }

            // Verify Pending rows still exist
            foreach (var id in pendingIds)
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
        CreatedByUserAgent: "RetentionPropTest",
        ExpiresAt: expiresAt,
        ConsumedAt: null,
        TerminatedAt: null,
        FailureReason: null,
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: null);
}
