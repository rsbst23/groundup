using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Auth.Repositories;

namespace GroundUp.Tests.Integration.Auth.Services;

/// <summary>
/// Property-based tests verifying concurrent consumption atomicity:
/// for any valid AuthFlowState row, launching N concurrent ConsumeAsync calls
/// via real Postgres guarantees exactly one winner.
/// </summary>
[Collection("AuthFlowStatePostgres")]
public sealed class AuthFlowStateConcurrentConsumptionPropertyTests
{
    private readonly AuthFlowStatePostgresFixture _fixture;

    public AuthFlowStateConcurrentConsumptionPropertyTests(AuthFlowStatePostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private AuthDbContext CreateContext() => _fixture.CreateContext();

    /// <summary>
    /// Property: For any valid Pending AuthFlowState row, launching N (2–10) concurrent
    /// MarkConsumedAsync calls against real Postgres guarantees exactly one success.
    /// </summary>
    [Property(MaxTest = 10)]
    public Property ConcurrentConsume_ExactlyOneWinner(
        int flowTypeInt,
        PositiveInt concurrencyRaw)
    {
        var flowType = (FlowType)(Math.Abs(flowTypeInt) % 7);
        var concurrency = (concurrencyRaw.Get % 9) + 2; // 2–10

        // Seed a Pending row
        Guid id;
        using (var ctx = CreateContext())
        {
            var repo = new AuthFlowStateRepository(ctx);
            var dto = new AuthFlowStateDto(
                Id: Guid.Empty,
                FlowType: flowType,
                Status: FlowStatus.Pending,
                TenantId: null,
                InvitationId: null,
                JoinLinkId: null,
                Realm: null,
                ReturnUrl: null,
                Nonce: Guid.NewGuid().ToString("N"),
                CreatedByIp: "10.0.0.1",
                CreatedByUserAgent: "ConcurrencyTest",
                ExpiresAt: DateTime.UtcNow.AddMinutes(15),
                ConsumedAt: null,
                TerminatedAt: null,
                FailureReason: null,
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: null);
            var result = repo.AddAsync(dto).GetAwaiter().GetResult();
            id = result.Data!.Id;
        }

        // Launch N concurrent consume attempts, each with its own DbContext
        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(() =>
        {
            using var ctx = CreateContext();
            var repo = new AuthFlowStateRepository(ctx);
            return repo.MarkConsumedAsync(id).GetAwaiter().GetResult();
        })).ToArray();

        Task.WaitAll(tasks);

        var successes = tasks.Count(t => t.Result.Success);
        var failures = tasks.Count(t => !t.Result.Success);

        return (successes == 1 && failures == concurrency - 1)
            .ToProperty();
    }
}
