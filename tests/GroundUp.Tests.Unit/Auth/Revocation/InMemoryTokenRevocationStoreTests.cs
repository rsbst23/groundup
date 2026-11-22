using GroundUp.Auth.Services.Revocation;

namespace GroundUp.Tests.Unit.Auth.Revocation;

public sealed class InMemoryTokenRevocationStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 12, 0, 0, 250, TimeSpan.Zero); // .250 s

    private readonly TestClock _clock = new(Start);
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _tenant = Guid.NewGuid();

    private InMemoryTokenRevocationStore CreateStore() => new(_clock);

    [Fact]
    public async Task TenantRevocation_TokenIssuedBefore_IsRevoked()
    {
        var store = CreateStore();
        await store.RevokeTenantAccessAsync(_user, _tenant);

        Assert.True(await store.IsRevokedAsync(_user, _tenant, Start.AddMinutes(-5)));
    }

    [Fact]
    public async Task TenantRevocation_TokenIssuedInSameSecond_IsRevoked()
    {
        // Cutoff rounds 12:00:00.250 up to 12:00:01; iat has whole seconds, so 12:00:00 is before it.
        var store = CreateStore();
        await store.RevokeTenantAccessAsync(_user, _tenant);

        Assert.True(await store.IsRevokedAsync(_user, _tenant, new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task TenantRevocation_TokenIssuedAtOrAfterCutoff_IsNotRevoked()
    {
        var store = CreateStore();
        await store.RevokeTenantAccessAsync(_user, _tenant);

        var cutoff = new DateTimeOffset(2026, 11, 21, 12, 0, 1, TimeSpan.Zero);
        Assert.False(await store.IsRevokedAsync(_user, _tenant, cutoff));
        Assert.False(await store.IsRevokedAsync(_user, _tenant, cutoff.AddMinutes(3)));
    }

    [Fact]
    public async Task TenantRevocation_CutoffAlreadyOnWholeSecond_IsNotMoved()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
        var store = new InMemoryTokenRevocationStore(clock);
        await store.RevokeTenantAccessAsync(_user, _tenant);

        Assert.True(await store.IsRevokedAsync(_user, _tenant, clock.GetUtcNow().AddSeconds(-1)));
        Assert.False(await store.IsRevokedAsync(_user, _tenant, clock.GetUtcNow()));
    }

    [Fact]
    public async Task TenantRevocation_DoesNotAffectOtherTenantOrTenantlessToken()
    {
        var store = CreateStore();
        await store.RevokeTenantAccessAsync(_user, _tenant);

        Assert.False(await store.IsRevokedAsync(_user, Guid.NewGuid(), Start.AddMinutes(-5)));
        Assert.False(await store.IsRevokedAsync(_user, null, Start.AddMinutes(-5)));
    }

    [Fact]
    public async Task TenantRevocation_DoesNotAffectOtherUser()
    {
        var store = CreateStore();
        await store.RevokeTenantAccessAsync(_user, _tenant);

        Assert.False(await store.IsRevokedAsync(Guid.NewGuid(), _tenant, Start.AddMinutes(-5)));
    }

    [Fact]
    public async Task UserWideRevocation_RevokesTenantAndTenantlessTokens()
    {
        var store = CreateStore();
        await store.RevokeAllAccessAsync(_user);

        Assert.True(await store.IsRevokedAsync(_user, _tenant, Start.AddMinutes(-5)));
        Assert.True(await store.IsRevokedAsync(_user, Guid.NewGuid(), Start.AddMinutes(-5)));
        Assert.True(await store.IsRevokedAsync(_user, null, Start.AddMinutes(-5)));
        Assert.False(await store.IsRevokedAsync(_user, null, Start.AddMinutes(5)));
    }

    [Fact]
    public async Task SecondRevocation_LaterCutoffWins()
    {
        var store = CreateStore();
        await store.RevokeTenantAccessAsync(_user, _tenant);

        _clock.Advance(TimeSpan.FromMinutes(10));
        await store.RevokeTenantAccessAsync(_user, _tenant);

        // A token issued between the two revocations is revoked by the second one.
        Assert.True(await store.IsRevokedAsync(_user, _tenant, Start.AddMinutes(5)));
    }

    [Fact]
    public async Task Revocation_OlderThanRetention_IsIgnored()
    {
        var store = CreateStore();
        await store.RevokeTenantAccessAsync(_user, _tenant);

        _clock.Advance(InMemoryTokenRevocationStore.Retention + TimeSpan.FromSeconds(2));

        Assert.False(await store.IsRevokedAsync(_user, _tenant, Start.AddMinutes(-5)));
    }

    [Fact]
    public async Task Revocation_WithinRetention_StillApplies()
    {
        var store = CreateStore();
        await store.RevokeTenantAccessAsync(_user, _tenant);

        _clock.Advance(TimeSpan.FromMinutes(120));

        Assert.True(await store.IsRevokedAsync(_user, _tenant, Start.AddMinutes(-5)));
    }

    [Fact]
    public void Retention_IsMaximumTokenLifetimePlusOneMinute()
    {
        Assert.Equal(TimeSpan.FromMinutes(121), InMemoryTokenRevocationStore.Retention);
    }

    [Fact]
    public async Task Write_RemovesExpiredEntries()
    {
        var store = CreateStore();
        await store.RevokeTenantAccessAsync(_user, _tenant);
        await store.RevokeAllAccessAsync(_user);
        Assert.Equal(2, store.Count);

        _clock.Advance(InMemoryTokenRevocationStore.Retention + TimeSpan.FromSeconds(2));
        await store.RevokeTenantAccessAsync(Guid.NewGuid(), _tenant);

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task ConcurrentWrites_AllRecorded()
    {
        var store = CreateStore();
        var users = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToArray();

        await Task.WhenAll(users.Select(u => Task.Run(() => store.RevokeTenantAccessAsync(u, _tenant))));

        Assert.Equal(users.Length, store.Count);
        foreach (var user in users)
        {
            Assert.True(await store.IsRevokedAsync(user, _tenant, Start.AddMinutes(-1)));
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now;

        public TestClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
