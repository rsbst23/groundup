using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Auth.Repositories;

namespace GroundUp.Tests.Integration.Auth.Services;

/// <summary>
/// Property-based tests verifying sweeper retention logic:
/// for any random mix of terminal rows (old/recent), the sweep
/// deletes only old terminal rows and leaves recent ones untouched.
/// </summary>
[Collection("AuthFlowStatePostgres")]
public sealed class AuthFlowStateSweeperRetentionPropertyTests
{
    private readonly AuthFlowStatePostgresFixture _fixture;

    public AuthFlowStateSweeperRetentionPropertyTests(AuthFlowStatePostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private AuthDbContext CreateContext() => _fixture.CreateContext();

    /// <summary>
    /// Property: For any random mix of terminal rows (some old, some recent),
    /// DeleteTerminalOlderThanAsync deletes only old terminal rows.
    /// Pending rows are never deleted regardless of age.
    /// </summary>
    [Property(MaxTest = 10)]
    public Property Sweep_OnlyOldTerminalRowsDeleted(
        PositiveInt oldCountRaw,
        PositiveInt recentCountRaw,
        PositiveInt pendingCountRaw)
    {
        var oldCount = (oldCountRaw.Get % 4) + 1;
        var recentCount = (recentCountRaw.Get % 4) + 1;
        var pendingCount = (pendingCountRaw.Get % 3) + 1;

        var now = DateTime.UtcNow;
        var retentionCutoff = now.AddMinutes(-5);

        var oldIds = new List<Guid>();
        var recentIds = new List<Guid>();
        var pendingIds = new List<Guid>();

        using (var ctx = CreateContext())
        {
            var repo = new AuthFlowStateRepository(ctx);

            for (var i = 0; i < oldCount; i++)
            {
                var dto = CreateDto(DateTime.UtcNow.AddMinutes(15));
                var created = repo.AddAsync(dto).GetAwaiter().GetResult();
                repo.MarkConsumedAsync(created.Data!.Id).GetAwaiter().GetResult();

                var entity = ctx.AuthFlowStates.Find(created.Data!.Id)!;
                entity.TerminatedAt = retentionCutoff.AddMinutes(-(i + 1));
                ctx.SaveChanges();

                oldIds.Add(created.Data!.Id);
            }

            for (var i = 0; i < recentCount; i++)
            {
                var dto = CreateDto(DateTime.UtcNow.AddMinutes(15));
                var created = repo.AddAsync(dto).GetAwaiter().GetResult();
                repo.MarkConsumedAsync(created.Data!.Id).GetAwaiter().GetResult();
                recentIds.Add(created.Data!.Id);
            }

            for (var i = 0; i < pendingCount; i++)
            {
                var dto = CreateDto(DateTime.UtcNow.AddMinutes(-10));
                var created = repo.AddAsync(dto).GetAwaiter().GetResult();
                pendingIds.Add(created.Data!.Id);
            }
        }

        using (var ctx = CreateContext())
        {
            var repo = new AuthFlowStateRepository(ctx);
            var deleteResult = repo.DeleteTerminalOlderThanAsync(retentionCutoff).GetAwaiter().GetResult();

            if (!deleteResult.Success || deleteResult.Data != oldCount)
                return false.ToProperty();

            foreach (var id in oldIds)
            {
                var row = repo.GetByIdAsync(id).GetAwaiter().GetResult();
                if (row.Success) return false.ToProperty();
            }

            foreach (var id in recentIds)
            {
                var row = repo.GetByIdAsync(id).GetAwaiter().GetResult();
                if (!row.Success || row.Data!.Status != FlowStatus.Consumed) return false.ToProperty();
            }

            foreach (var id in pendingIds)
            {
                var row = repo.GetByIdAsync(id).GetAwaiter().GetResult();
                if (!row.Success || row.Data!.Status != FlowStatus.Pending) return false.ToProperty();
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
