using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Data.Postgres.Seeders;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Data.Postgres;
using GroundUp.Repositories.Settings;
using GroundUp.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Session timeouts as runtime settings, end to end: a value changed through
/// <see cref="ISettingsService"/> applies to the next issued token and cookie without a restart,
/// a tenant value shortens the System value, and the absolute cap returns <c>REAUTH_REQUIRED</c>.
/// <para>
/// Each test removes the values it wrote, so other tests in the collection see the defaults.
/// </para>
/// </summary>
[Collection("AuthFlow")]
public sealed class SessionPolicyIntegrationTests : AuthFlowTestBase, IAsyncLifetime
{
    private readonly List<Guid> _writtenValueIds = new();
    private Guid _systemLevelId;
    private Guid _tenantLevelId;

    public SessionPolicyIntegrationTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    public async Task InitializeAsync()
    {
        // The fixture removes hosted services, so the seeders do not run on startup. Run the two
        // this feature needs: the System/Tenant levels and the session setting definitions.
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GroundUpDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        await new DefaultSettingLevelSeeder(new SettingsRepository(db, new PostgresUniqueConstraintDetector())).SeedAsync();
        await new DefaultAuthSettingsSeeder(settings, NullLogger<DefaultAuthSettingsSeeder>.Instance).SeedAsync();

        var levels = await db.Set<SettingLevel>().AsNoTracking().ToListAsync();
        _systemLevelId = levels.Single(l => l.Name == "System").Id;
        _tenantLevelId = levels.Single(l => l.Name == "Tenant").Id;

        // Write both System values explicitly. Besides fixing the starting point, each write
        // publishes SettingChangedEvent, which clears any result an earlier test cached while the
        // definitions did not exist yet.
        await SetAsync(SessionSettingKeys.AbsoluteLifetimeMinutes, "480", _systemLevelId, null);
        await SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "60", _systemLevelId, null);
    }

    public async Task DisposeAsync()
    {
        using var scope = Fixture.Services.CreateScope();
        var settings = CreateUnrestrictedSettings(scope.ServiceProvider);

        // Delete in reverse order so tenant values go before the System values they depend on.
        foreach (var id in Enumerable.Reverse(_writtenValueIds).Distinct())
        {
            await settings.DeleteValueAsync(id);
        }
    }

    [Fact]
    public async Task SetTenant_SystemTokenLifetimeChanged_NextTokenAndCookieUseNewValue()
    {
        // Arrange
        await SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "25", _systemLevelId, null);
        var (userId, tenantId) = await SeedMemberAsync("sys");
        var before = DateTimeOffset.UtcNow;

        // Act
        var response = await Client.SendAsync(SetTenantRequest(TenantlessToken(userId), tenantId));

        // Assert: the token and the cookie both expire 25 minutes from now
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = ExtractAuthCookie(response)!;
        ReadExp(token).Should().BeCloseTo(before.AddMinutes(25), TimeSpan.FromSeconds(30));
        ReadCookieExpires(response, "AuthToken").Should().BeCloseTo(before.AddMinutes(25), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task SetTenant_TenantValueShorterThanSystem_TenantValueApplies()
    {
        // Arrange
        await SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "40", _systemLevelId, null);
        var (userId, tenantId) = await SeedMemberAsync("ten");
        await SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "10", _tenantLevelId, tenantId);
        var before = DateTimeOffset.UtcNow;

        // Act
        var response = await Client.SendAsync(SetTenantRequest(TenantlessToken(userId), tenantId));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ReadExp(ExtractAuthCookie(response)!).Should().BeCloseTo(before.AddMinutes(10), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task SettingsService_TenantValueLongerThanSystem_IsRejected()
    {
        // Arrange
        await SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "30", _systemLevelId, null);
        using var scope = Fixture.Services.CreateScope();
        var settings = CreateUnrestrictedSettings(scope.ServiceProvider);

        // Act
        var result = await settings.SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "45", _tenantLevelId, Guid.NewGuid());

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SetTenant_TenantAbsoluteCapStricterThanSessionAge_Returns401ReauthRequired()
    {
        // Arrange: System allows 480 minutes; this tenant only 30. The session is 45 minutes old.
        await SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "20", _systemLevelId, null);
        var (userId, tenantId) = await SeedMemberAsync("cap");
        await SetAsync(SessionSettingKeys.AbsoluteLifetimeMinutes, "30", _tenantLevelId, tenantId);
        var token = TenantlessToken(userId, DateTimeOffset.UtcNow.AddMinutes(-45).ToUnixTimeSeconds());

        // Act
        var response = await Client.SendAsync(SetTenantRequest(token, tenantId));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorCodeAsync(response)).Should().Be(ErrorCodes.ReauthRequired);
        ExtractAuthCookie(response).Should().BeNull();
    }

    [Fact]
    public async Task Refresh_SessionPastAbsoluteLifetime_Returns401ReauthRequired()
    {
        // Arrange: a tenant-scoped token whose sign-in was 10 hours ago (cap is 8 hours)
        var (userId, tenantId) = await SeedMemberAsync("ref");
        var token = Fixture.GenerateTestToken(
            userId,
            tenantId,
            additionalClaims: new[] { AuthTimeClaim(DateTimeOffset.UtcNow.AddHours(-10).ToUnixTimeSeconds()) });

        // Act
        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, "/auth/refresh", token));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorCodeAsync(response)).Should().Be(ErrorCodes.ReauthRequired);
    }

    [Fact]
    public async Task SetTenant_SessionNearCap_TokenExpiryStopsAtCap()
    {
        // Arrange: signed in 470 minutes ago with a 480-minute cap; a 60-minute token would overrun it
        var (userId, tenantId) = await SeedMemberAsync("near");
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-470);
        var token = TenantlessToken(userId, authTime.ToUnixTimeSeconds());

        // Act
        var response = await Client.SendAsync(SetTenantRequest(token, tenantId));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ReadExp(ExtractAuthCookie(response)!).Should().BeCloseTo(authTime.AddMinutes(480), TimeSpan.FromSeconds(30));
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// These tests cover session policy, not settings authorization, and run without a signed-in user.
    /// They use the concrete service with the allow-all authorizer. Authorization is covered by
    /// <c>SettingsAuthorizationIntegrationTests</c>.
    /// </summary>
    private static ISettingsService CreateUnrestrictedSettings(IServiceProvider provider)
        => ActivatorUtilities.CreateInstance<SettingsService>(provider, new AllowAllSettingsAuthorizer());

    private async Task SetAsync(string key, string value, Guid levelId, Guid? scopeId)
    {
        using var scope = Fixture.Services.CreateScope();
        var settings = CreateUnrestrictedSettings(scope.ServiceProvider);
        var result = await settings.SetAsync(key, value, levelId, scopeId);
        result.Success.Should().BeTrue($"setting {key}={value} should succeed: {result.Message}");
        _writtenValueIds.Add(result.Data!.Id);
    }

    private async Task<(Guid UserId, Guid TenantId)> SeedMemberAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"SP {label} {suffix}", $"sp-{label}-{suffix}");
        var userId = await SeedUserAsync($"sub-sp-{label}-{suffix}", $"sp-{label}-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);
        return (userId, tenantId);
    }

    private static Claim AuthTimeClaim(long unix) =>
        new("auth_time", unix.ToString(), ClaimValueTypes.Integer64);

    private string TenantlessToken(Guid userId, long? authTimeUnix = null) =>
        Fixture.GenerateTestToken(
            userId,
            additionalClaims: new[] { AuthTimeClaim(authTimeUnix ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()) });

    private HttpRequestMessage SetTenantRequest(string token, Guid tenantId)
    {
        var request = CreateBearerRequest(HttpMethod.Post, "/auth/set-tenant", token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { tenantId }), Encoding.UTF8, "application/json");
        return request;
    }

    private static DateTimeOffset ReadExp(string token) =>
        new(DateTime.SpecifyKind(new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo, DateTimeKind.Utc));

    private static DateTimeOffset ReadCookieExpires(HttpResponseMessage response, string cookieName)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith(cookieName + "=", StringComparison.Ordinal));
        var part = header.Split(';')
            .Select(p => p.Trim())
            .First(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        return DateTimeOffset.Parse(
            part["expires=".Length..],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal);
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return json.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }
}
