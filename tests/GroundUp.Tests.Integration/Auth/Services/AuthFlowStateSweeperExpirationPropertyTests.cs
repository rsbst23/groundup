using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Auth.Repositories;

namespace GroundUp.Tests.Integration.Auth.Services;

/// <summary>
/// Property-based tests verifying sweeper expiration logic:
/// for any random mix of expired/non-expired Pending rows, the sweep
/// transitions only expired Pending rows and leaves others untouched.
/// </summary>
[Collection("AuthFlowStatePostgres")]
public sealed class AuthFlowStateSweeperExpirationPropertyTests
{
    private readonly AuthFlowStatePostgresFixture _fixture;

    public AuthFlowStateSweeperExpirationPropertyTests(AuthFlowStatePostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private AuthDbContext CreateContext() => _fixture.CreateContext();

    /// <summary>
    /// Property: For any random mix of rows (some expired, some not),
    /// MarkExpiredOlderThanAsync transitions only expired Pending rows.
    /// </summary>
    [Property(MaxTest = 10)]
    public Property Sweep_OnlyExpiredPendingRowsTransition(
        PositiveInt expiredCountRaw,
        PositiveInt freshCountRaw)
    {
        var expiredCount = (expiredCountRaw.Get % 5) + 1;
        var freshCount = (freshCountRaw.Get % 5) + 1;

        var now = DateTime.UtcNow;
        var expiredIds = new List<Guid>();
        var freshIds = new List<Guid>();

        using (var ctx = CreateContext())
        {
            var repo = new AuthFlowStateRepository(ctx);

            for (var i = 0; i < expiredCount; i++)
            {
                var dto = CreateDto(now.AddMinutes(-(i + 1)));
                var result = repo.AddAsync(dto).GetAwaiter().GetResult();
                expiredIds.Add(result.Data!.Id);
            }

            for (var i = 0; i < freshCount; i++)
            {
                var dto = CreateDto(now.AddMinutes(i + 10));
                var result = repo.AddAsync(dto).GetAwaiter().GetResult();
                freshIds.Add(result.Data!.Id);
            }

            var sweepResult = repo.MarkExpiredOlderThanAsync(now).GetAwaiter().GetResult();

            if (!sweepResult.Success || sweepResult.Data != expiredCount)
                return false.ToProperty();

            foreach (var id in expiredIds)
            {
                var row = repo.GetByIdAsync(id).GetAwaiter().GetResult();
                if (!row.Success || row.Data!.Status != FlowStatus.Expired)
                    return false.ToProperty();
            }

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
        StateToken: Guid.NewGuid().ToString("N"),
        CodeVerifier: "integration-test-code-verifier-1234567890123",
        RedirectUri: "https://example.com/auth/callback",
        OrganizationName: null,
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
