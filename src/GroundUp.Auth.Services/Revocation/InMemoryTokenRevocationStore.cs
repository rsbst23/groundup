using System.Collections.Concurrent;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Abstractions;

namespace GroundUp.Auth.Services.Revocation;

/// <summary>
/// Default <see cref="ITokenRevocationStore"/>: thread-safe, in memory, per process.
/// <para>
/// <b>Phase 15 (GU-47):</b> replace with an <c>ICache</c>-backed implementation when the caching module
/// exists. Until then a revocation is visible only on the instance that recorded it; other instances
/// accept the token until it expires. Production refuses to start with this store unless
/// <c>GroundUp:Auth:AllowInMemoryTokenRevocationStore</c> is set (see <see cref="TokenRevocationStartupValidator"/>).
/// </para>
/// <para>
/// A cutoff is "now" rounded up to the next whole second, because <c>iat</c> has whole-second precision.
/// A token is revoked when <c>iat &lt; cutoff</c>, so a token issued in the same second as the
/// revocation is revoked too (the safe direction).
/// </para>
/// <para>
/// Entries are kept for <see cref="Retention"/>: the longest a token can live (the token lifetime
/// setting's maximum) plus a margin. Older entries can no longer match a valid token; they are ignored
/// on read and removed on write, so memory is bounded by the number of revocations in that window.
/// </para>
/// <para>
/// The clock must be the one that stamps <c>iat</c> on tokens (the system clock). Register this store with
/// <see cref="TimeProvider.System"/> rather than a <see cref="TimeProvider"/> resolved from the container.
/// </para>
/// </summary>
public sealed class InMemoryTokenRevocationStore : ITokenRevocationStore
{
    /// <summary>How long a revocation is kept: the maximum token lifetime plus one minute.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(SessionSettingKeys.TokenLifetimeMax + 1);

    private readonly ConcurrentDictionary<(Guid UserId, Guid? TenantId), DateTimeOffset> _cutoffs = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryTokenRevocationStore"/>.
    /// </summary>
    /// <param name="timeProvider">The clock. Must match the clock that stamps <c>iat</c> (system clock in production).</param>
    public InMemoryTokenRevocationStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Number of stored revocations (diagnostics). Expired entries are removed on the next write.
    /// </summary>
    public int Count => _cutoffs.Count;

    /// <inheritdoc />
    public Task RevokeTenantAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        Record((userId, tenantId));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeAllAccessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        Record((userId, null));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsRevokedAsync(
        Guid userId,
        Guid? tenantId,
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        var revoked = IsBefore((userId, null), issuedAt, now)
            || (tenantId is { } tid && IsBefore((userId, tid), issuedAt, now));

        return Task.FromResult(revoked);
    }

    private bool IsBefore((Guid, Guid?) key, DateTimeOffset issuedAt, DateTimeOffset now)
        => _cutoffs.TryGetValue(key, out var cutoff)
            && !IsExpired(cutoff, now)
            && issuedAt < cutoff;

    private void Record((Guid, Guid?) key)
    {
        var now = _timeProvider.GetUtcNow();
        var cutoff = RoundUpToSecond(now);

        // A later revocation for the same key wins; an earlier one never shortens it.
        _cutoffs.AddOrUpdate(key, cutoff, (_, existing) => existing > cutoff ? existing : cutoff);

        RemoveExpired(now);
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var entry in _cutoffs)
        {
            if (IsExpired(entry.Value, now))
            {
                // Removes only if the value is unchanged, so a concurrent newer revocation is kept.
                _cutoffs.TryRemove(entry);
            }
        }
    }

    private static bool IsExpired(DateTimeOffset cutoff, DateTimeOffset now) => now - cutoff > Retention;

    private static DateTimeOffset RoundUpToSecond(DateTimeOffset time)
    {
        var remainder = time.Ticks % TimeSpan.TicksPerSecond;
        return remainder == 0 ? time : time.AddTicks(TimeSpan.TicksPerSecond - remainder);
    }
}
