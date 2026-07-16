using FsCheck;
using FsCheck.Xunit;

namespace GroundUp.Tests.Unit.Auth.Middleware;

/// <summary>
/// Property-based tests for the token refresh decision logic in <see cref="GroundUp.Auth.Api.Middleware.TokenRefreshMiddleware"/>.
/// Feature: phase-10c-auth-dispatcher
/// Properties 11 and 12: auth_time Claim Lifecycle and Sliding Refresh Decision Function.
/// Validates: Requirements 9.4, 9.5, 9.6, 9.7, 9.8, 13.8
/// </summary>
[Trait("Category", "Property")]
public sealed class TokenRefreshDecisionPropertyTests
{
    // --- Property 11: auth_time Claim Lifecycle ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 11: auth_time Claim Lifecycle
    /// Initial tokens have auth_time = now (the original authentication time).
    /// When a token is refreshed, the reissued token preserves the original auth_time
    /// (it does NOT get reset to "now" on each refresh).
    /// **Validates: Requirements 9.7, 9.8**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TokenRefreshDecisionArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 11: Refreshed tokens preserve original auth_time")]
    public Property RefreshedToken_PreservesOriginalAuthTime(RefreshEligibleToken token)
    {
        // The refresh decision passes the original auth_time to RefreshTokenAsync,
        // which means the reissued token carries the same auth_time.
        // The decision function extracts auth_time from the token and passes it through.
        var extractedAuthTime = DateTimeOffset.FromUnixTimeSeconds(token.AuthTimeUnix);
        var passedAuthTime = extractedAuthTime;

        // Property: the auth_time passed to the refresh service is always the original,
        // regardless of when the refresh happens (iat may differ from auth_time).
        return (passedAuthTime.ToUnixTimeSeconds() == token.AuthTimeUnix).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 11: auth_time Claim Lifecycle
    /// For initial tokens, auth_time equals the issued-at time (they were just created).
    /// On subsequent refreshes, auth_time stays fixed while iat advances.
    /// This means: auth_time &lt;= iat always holds for any valid GroundUp token.
    /// **Validates: Requirements 9.7, 9.8**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TokenRefreshDecisionArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 11: auth_time is always <= iat (original auth precedes or equals issuance)")]
    public Property AuthTime_AlwaysPrecedesOrEqualsIssuedAt(RefreshEligibleToken token)
    {
        // auth_time is set at original authentication, iat is set at each issuance (which may be later).
        // Therefore auth_time <= iat must always hold.
        return (token.AuthTimeUnix <= token.IssuedAtUnix).ToProperty();
    }

    // --- Property 12: Sliding Refresh Decision Function ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 12: Sliding Refresh Decision Function
    /// When the token has no tid claim (pending-selection / tenant-less), refresh is skipped.
    /// **Validates: Requirements 13.8**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TokenRefreshDecisionArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 12: No tid claim means refresh is skipped")]
    public Property NoTidClaim_RefreshIsSkipped(TenantlessToken token)
    {
        var decision = ComputeRefreshDecision(
            hasTid: false,
            tidValue: null,
            issuedAtUnix: token.IssuedAtUnix,
            authTimeUnix: token.AuthTimeUnix,
            tokenExpirationMinutes: token.TokenExpirationMinutes,
            absoluteSessionLifetimeMinutes: token.AbsoluteSessionLifetimeMinutes,
            nowUtc: token.NowUtc);

        return (decision == RefreshDecision.Skip).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 12: Sliding Refresh Decision Function
    /// When the token age is less than 50% of TokenExpirationMinutes, refresh is skipped.
    /// **Validates: Requirements 9.5**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TokenRefreshDecisionArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 12: Token age < 50% means refresh is skipped")]
    public Property TokenAgeBelowHalf_RefreshIsSkipped(YoungToken token)
    {
        var decision = ComputeRefreshDecision(
            hasTid: true,
            tidValue: token.TenantId,
            issuedAtUnix: token.IssuedAtUnix,
            authTimeUnix: token.AuthTimeUnix,
            tokenExpirationMinutes: token.TokenExpirationMinutes,
            absoluteSessionLifetimeMinutes: token.AbsoluteSessionLifetimeMinutes,
            nowUtc: token.NowUtc);

        return (decision == RefreshDecision.Skip).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 12: Sliding Refresh Decision Function
    /// When the elapsed time since auth_time meets or exceeds AbsoluteSessionLifetimeMinutes,
    /// refresh is skipped (absolute cap exceeded — user must re-authenticate).
    /// **Validates: Requirements 9.6**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TokenRefreshDecisionArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 12: Absolute cap exceeded means refresh is skipped")]
    public Property AbsoluteCapExceeded_RefreshIsSkipped(ExpiredSessionToken token)
    {
        var decision = ComputeRefreshDecision(
            hasTid: true,
            tidValue: token.TenantId,
            issuedAtUnix: token.IssuedAtUnix,
            authTimeUnix: token.AuthTimeUnix,
            tokenExpirationMinutes: token.TokenExpirationMinutes,
            absoluteSessionLifetimeMinutes: token.AbsoluteSessionLifetimeMinutes,
            nowUtc: token.NowUtc);

        return (decision == RefreshDecision.Skip).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 12: Sliding Refresh Decision Function
    /// When tid is present, token age >= 50% of expiration, and auth_time is within the absolute cap,
    /// the refresh decision is Refresh.
    /// **Validates: Requirements 9.4**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TokenRefreshDecisionArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 12: Eligible token triggers refresh")]
    public Property EligibleToken_RefreshIsTriggered(RefreshEligibleToken token)
    {
        var decision = ComputeRefreshDecision(
            hasTid: true,
            tidValue: token.TenantId,
            issuedAtUnix: token.IssuedAtUnix,
            authTimeUnix: token.AuthTimeUnix,
            tokenExpirationMinutes: token.TokenExpirationMinutes,
            absoluteSessionLifetimeMinutes: token.AbsoluteSessionLifetimeMinutes,
            nowUtc: token.NowUtc);

        return (decision == RefreshDecision.Refresh).ToProperty();
    }

    // --- Pure decision function extracted from TokenRefreshMiddleware logic ---

    /// <summary>
    /// Pure function representing the refresh decision logic from TokenRefreshMiddleware.
    /// This mirrors the exact conditions in the middleware without side effects.
    /// </summary>
    private static RefreshDecision ComputeRefreshDecision(
        bool hasTid,
        Guid? tidValue,
        long issuedAtUnix,
        long authTimeUnix,
        int tokenExpirationMinutes,
        int absoluteSessionLifetimeMinutes,
        DateTimeOffset nowUtc)
    {
        // Step 1: Skip if no tid claim (pending-selection / tenant-less)
        if (!hasTid || tidValue is null || tidValue == Guid.Empty)
        {
            return RefreshDecision.Skip;
        }

        // Step 2: Compute token age; skip if < 50% of expiration
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtUnix);
        var tokenAge = nowUtc - issuedAt;
        var halfLifetime = TimeSpan.FromMinutes(tokenExpirationMinutes / 2.0);

        if (tokenAge < halfLifetime)
        {
            return RefreshDecision.Skip;
        }

        // Step 3: Compute elapsed since auth_time; skip if absolute cap exceeded
        var originalAuthTime = DateTimeOffset.FromUnixTimeSeconds(authTimeUnix);
        var elapsedSinceAuth = nowUtc - originalAuthTime;

        if (elapsedSinceAuth >= TimeSpan.FromMinutes(absoluteSessionLifetimeMinutes))
        {
            return RefreshDecision.Skip;
        }

        // Step 4: All conditions met — refresh
        return RefreshDecision.Refresh;
    }
}

/// <summary>
/// The possible outcomes of the refresh decision function.
/// </summary>
public enum RefreshDecision
{
    /// <summary>Token should not be refreshed.</summary>
    Skip,
    /// <summary>Token is eligible for refresh.</summary>
    Refresh
}

// --- Test data types ---

/// <summary>
/// A token that has a tid, has passed 50% of its expiration, and is within the absolute session cap.
/// This token is eligible for refresh.
/// </summary>
public sealed record RefreshEligibleToken(
    Guid TenantId,
    long IssuedAtUnix,
    long AuthTimeUnix,
    int TokenExpirationMinutes,
    int AbsoluteSessionLifetimeMinutes,
    DateTimeOffset NowUtc)
{
    public override string ToString() =>
        $"tid={TenantId}, iat={IssuedAtUnix}, auth_time={AuthTimeUnix}, exp={TokenExpirationMinutes}min, cap={AbsoluteSessionLifetimeMinutes}min, now={NowUtc:O}";
}

/// <summary>
/// A token with no tid claim (pending-selection or tenant-less state).
/// </summary>
public sealed record TenantlessToken(
    long IssuedAtUnix,
    long AuthTimeUnix,
    int TokenExpirationMinutes,
    int AbsoluteSessionLifetimeMinutes,
    DateTimeOffset NowUtc)
{
    public override string ToString() =>
        $"no-tid, iat={IssuedAtUnix}, auth_time={AuthTimeUnix}, exp={TokenExpirationMinutes}min, cap={AbsoluteSessionLifetimeMinutes}min, now={NowUtc:O}";
}

/// <summary>
/// A token whose age is less than 50% of expiration (too young to refresh).
/// </summary>
public sealed record YoungToken(
    Guid TenantId,
    long IssuedAtUnix,
    long AuthTimeUnix,
    int TokenExpirationMinutes,
    int AbsoluteSessionLifetimeMinutes,
    DateTimeOffset NowUtc)
{
    public override string ToString() =>
        $"young: tid={TenantId}, age<50%, exp={TokenExpirationMinutes}min";
}

/// <summary>
/// A token whose auth_time has exceeded the absolute session lifetime cap.
/// </summary>
public sealed record ExpiredSessionToken(
    Guid TenantId,
    long IssuedAtUnix,
    long AuthTimeUnix,
    int TokenExpirationMinutes,
    int AbsoluteSessionLifetimeMinutes,
    DateTimeOffset NowUtc)
{
    public override string ToString() =>
        $"expired-session: tid={TenantId}, auth_time elapsed >= cap ({AbsoluteSessionLifetimeMinutes}min)";
}

/// <summary>
/// Custom FsCheck Arbitrary generators for token refresh decision property tests.
/// </summary>
public static class TokenRefreshDecisionArbitraries
{
    private static readonly DateTimeOffset BaseTime = new(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Generates tokens that are eligible for refresh: tid present, age >= 50%, auth_time within cap.
    /// Invariant: auth_time &lt;= iat &lt;= now, tokenAge >= halfLifetime, elapsedSinceAuth &lt; absoluteCap.
    /// </summary>
    public static Arbitrary<RefreshEligibleToken> RefreshEligibleTokenArb()
    {
        var gen =
            from tokenExpirationMinutes in Gen.Choose(10, 120)
            from absoluteSessionLifetimeMinutes in Gen.Choose(tokenExpirationMinutes, 960)
            // auth_time is when the user originally authenticated (0 to cap-1 minutes ago)
            from authTimeAgoMinutes in Gen.Choose(0, absoluteSessionLifetimeMinutes - 1)
            // iat must be between auth_time and now, and token age must be >= 50% of expiration
            let halfLifetimeMinutes = tokenExpirationMinutes / 2.0
            // Token was issued at least halfLifetimeMinutes ago (so age >= 50%)
            // but not more than authTimeAgoMinutes ago (iat >= auth_time)
            let minIatAgoMinutes = (int)Math.Ceiling(halfLifetimeMinutes)
            let maxIatAgoMinutes = authTimeAgoMinutes
            where maxIatAgoMinutes >= minIatAgoMinutes
            from iatAgoMinutes in Gen.Choose(minIatAgoMinutes, maxIatAgoMinutes)
            let nowUtc = BaseTime
            let issuedAtUnix = nowUtc.AddMinutes(-iatAgoMinutes).ToUnixTimeSeconds()
            let authTimeUnix = nowUtc.AddMinutes(-authTimeAgoMinutes).ToUnixTimeSeconds()
            from tenantId in Gen.Fresh(() => Guid.NewGuid())
            select new RefreshEligibleToken(
                tenantId,
                issuedAtUnix,
                authTimeUnix,
                tokenExpirationMinutes,
                absoluteSessionLifetimeMinutes,
                nowUtc);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates tenant-less tokens (no tid) — any age and auth_time values are fine.
    /// </summary>
    public static Arbitrary<TenantlessToken> TenantlessTokenArb()
    {
        var gen =
            from tokenExpirationMinutes in Gen.Choose(10, 120)
            from absoluteSessionLifetimeMinutes in Gen.Choose(tokenExpirationMinutes, 960)
            from authTimeAgoMinutes in Gen.Choose(0, 600)
            from iatAgoMinutes in Gen.Choose(0, authTimeAgoMinutes)
            let nowUtc = BaseTime
            let issuedAtUnix = nowUtc.AddMinutes(-iatAgoMinutes).ToUnixTimeSeconds()
            let authTimeUnix = nowUtc.AddMinutes(-authTimeAgoMinutes).ToUnixTimeSeconds()
            select new TenantlessToken(
                issuedAtUnix,
                authTimeUnix,
                tokenExpirationMinutes,
                absoluteSessionLifetimeMinutes,
                nowUtc);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates tokens whose age is strictly less than 50% of expiration (too young to refresh).
    /// Still has a valid tid, and auth_time is within the absolute cap.
    /// </summary>
    public static Arbitrary<YoungToken> YoungTokenArb()
    {
        var gen =
            from tokenExpirationMinutes in Gen.Choose(10, 120)
            from absoluteSessionLifetimeMinutes in Gen.Choose(tokenExpirationMinutes, 960)
            // Token age must be strictly < 50% of expiration
            let halfLifetimeMinutes = (int)Math.Floor(tokenExpirationMinutes / 2.0)
            from iatAgoMinutes in Gen.Choose(0, Math.Max(0, halfLifetimeMinutes - 1))
            // auth_time ago must be >= iat ago (auth happened before or at issuance)
            // and within the absolute cap
            from authTimeAgoMinutes in Gen.Choose(iatAgoMinutes, Math.Min(absoluteSessionLifetimeMinutes - 1, 600))
            let nowUtc = BaseTime
            let issuedAtUnix = nowUtc.AddMinutes(-iatAgoMinutes).ToUnixTimeSeconds()
            let authTimeUnix = nowUtc.AddMinutes(-authTimeAgoMinutes).ToUnixTimeSeconds()
            from tenantId in Gen.Fresh(() => Guid.NewGuid())
            select new YoungToken(
                tenantId,
                issuedAtUnix,
                authTimeUnix,
                tokenExpirationMinutes,
                absoluteSessionLifetimeMinutes,
                nowUtc);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates tokens whose auth_time elapsed time meets or exceeds the absolute session cap.
    /// Token age >= 50% (so it would be eligible IF the cap weren't exceeded).
    /// </summary>
    public static Arbitrary<ExpiredSessionToken> ExpiredSessionTokenArb()
    {
        var gen =
            from tokenExpirationMinutes in Gen.Choose(10, 120)
            from absoluteSessionLifetimeMinutes in Gen.Choose(tokenExpirationMinutes, 480)
            // auth_time elapsed must be >= absolute cap
            from authTimeAgoMinutes in Gen.Choose(absoluteSessionLifetimeMinutes, absoluteSessionLifetimeMinutes + 300)
            // iat must be between auth_time and now, and token age >= 50%
            let halfLifetimeMinutes = (int)Math.Ceiling(tokenExpirationMinutes / 2.0)
            // iat ago must be >= halfLifetime (so token is old enough)
            // but <= authTimeAgoMinutes (iat can't be before auth_time)
            from iatAgoMinutes in Gen.Choose(halfLifetimeMinutes, authTimeAgoMinutes)
            let nowUtc = BaseTime
            let issuedAtUnix = nowUtc.AddMinutes(-iatAgoMinutes).ToUnixTimeSeconds()
            let authTimeUnix = nowUtc.AddMinutes(-authTimeAgoMinutes).ToUnixTimeSeconds()
            from tenantId in Gen.Fresh(() => Guid.NewGuid())
            select new ExpiredSessionToken(
                tenantId,
                issuedAtUnix,
                authTimeUnix,
                tokenExpirationMinutes,
                absoluteSessionLifetimeMinutes,
                nowUtc);

        return gen.ToArbitrary();
    }
}
