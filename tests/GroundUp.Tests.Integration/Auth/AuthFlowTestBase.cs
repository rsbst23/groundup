using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Base class for Phase 10C auth flow integration tests. Provides helper methods for:
/// <list type="bullet">
///   <item>Setting up <see cref="AuthFlowState"/> rows with all required security fields</item>
///   <item>Creating HTTP requests with state cookies set for callback simulation</item>
///   <item>Parsing callback responses (redirect, JSON picker, error)</item>
///   <item>Configuring mock IDP responses (code exchange, userinfo)</item>
///   <item>Seeding test tenants and users in the database</item>
/// </list>
/// </summary>
public abstract class AuthFlowTestBase
{
    protected readonly AuthFlowTestFixture Fixture;
    protected readonly HttpClient Client;

    protected AuthFlowTestBase(AuthFlowTestFixture fixture)
    {
        Fixture = fixture;
        Client = fixture.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, // We want to inspect redirects, not follow them
            HandleCookies = false // Cookies are extracted from Set-Cookie headers directly
        });
    }

    // ────────────────────────────────────────────────────────────────────────
    // AuthFlowState Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a Pending <see cref="AuthFlowState"/> row in the database with all required
    /// security fields populated (StateToken, CodeVerifier, RedirectUri, Nonce).
    /// Returns the entity for use in test assertions.
    /// </summary>
    /// <param name="flowType">The type of flow to create.</param>
    /// <param name="stateToken">Override state token; auto-generated if null.</param>
    /// <param name="nonce">Override nonce; auto-generated if null.</param>
    /// <param name="codeVerifier">Override code verifier; auto-generated if null.</param>
    /// <param name="redirectUri">Override redirect URI; defaults to test callback URL.</param>
    /// <param name="organizationName">Organization name for NewOrganization flows.</param>
    /// <param name="tenantId">Optional tenant ID (for host-pinned flows).</param>
    /// <param name="realm">Optional realm override (for enterprise tenants).</param>
    /// <param name="expiresAt">Override expiration; defaults to 10 minutes from now.</param>
    protected async Task<AuthFlowState> CreatePendingFlowStateAsync(
        FlowType flowType,
        string? stateToken = null,
        string? nonce = null,
        string? codeVerifier = null,
        string? redirectUri = null,
        string? organizationName = null,
        Guid? tenantId = null,
        string? realm = null,
        DateTime? expiresAt = null)
    {
        stateToken ??= AuthFlowTestFixture.GenerateStateToken();
        nonce ??= AuthFlowTestFixture.GenerateNonce();
        codeVerifier ??= AuthFlowTestFixture.GenerateCodeVerifier();
        redirectUri ??= "http://localhost/auth/callback";

        var flowState = new AuthFlowState
        {
            FlowType = flowType,
            Status = FlowStatus.Pending,
            StateToken = stateToken,
            Nonce = nonce,
            CodeVerifier = codeVerifier,
            RedirectUri = redirectUri,
            OrganizationName = organizationName,
            TenantId = tenantId,
            Realm = realm,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddMinutes(10),
            CreatedByIp = "127.0.0.1",
            CreatedByUserAgent = "IntegrationTest/1.0",
            CreatedAt = DateTime.UtcNow
        };

        await using var context = Fixture.CreateAuthDbContext();
        context.Set<AuthFlowState>().Add(flowState);
        await context.SaveChangesAsync();

        return flowState;
    }

    /// <summary>
    /// Creates an expired <see cref="AuthFlowState"/> row for testing expiration handling.
    /// </summary>
    protected Task<AuthFlowState> CreateExpiredFlowStateAsync(
        FlowType flowType,
        string? stateToken = null)
    {
        return CreatePendingFlowStateAsync(
            flowType,
            stateToken: stateToken,
            expiresAt: DateTime.UtcNow.AddMinutes(-5));
    }

    /// <summary>
    /// Creates a consumed (already-used) <see cref="AuthFlowState"/> row for testing replay protection.
    /// </summary>
    protected async Task<AuthFlowState> CreateConsumedFlowStateAsync(
        FlowType flowType,
        string? stateToken = null)
    {
        var flowState = await CreatePendingFlowStateAsync(flowType, stateToken: stateToken);

        await using var context = Fixture.CreateAuthDbContext();
        var entity = await context.Set<AuthFlowState>().FindAsync(flowState.Id);
        entity!.Status = FlowStatus.Consumed;
        entity.ConsumedAt = DateTime.UtcNow;
        entity.TerminatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        flowState.Status = FlowStatus.Consumed;
        flowState.ConsumedAt = entity.ConsumedAt;
        flowState.TerminatedAt = entity.TerminatedAt;
        return flowState;
    }

    // ────────────────────────────────────────────────────────────────────────
    // HTTP Request Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an HTTP GET request for the callback endpoint with the state cookie pre-set.
    /// Simulates the browser returning from Keycloak with the authorization code and state.
    /// </summary>
    /// <param name="code">The authorization code from Keycloak.</param>
    /// <param name="stateToken">The state token (must match what's in the state cookie).</param>
    /// <returns>An <see cref="HttpRequestMessage"/> ready to send.</returns>
    protected HttpRequestMessage CreateCallbackRequest(string code, string stateToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/auth/callback?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(stateToken)}");

        // Set the state cookie (browser-binding CSRF protection)
        request.Headers.Add("Cookie", $"AuthState={stateToken}");

        return request;
    }

    /// <summary>
    /// Creates an HTTP GET request for the callback endpoint WITHOUT the state cookie.
    /// Used for testing CSRF rejection.
    /// </summary>
    protected HttpRequestMessage CreateCallbackRequestWithoutCookie(string code, string stateToken)
    {
        return new HttpRequestMessage(HttpMethod.Get,
            $"/auth/callback?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(stateToken)}");
    }

    /// <summary>
    /// Creates an HTTP GET request for the callback with a mismatched state cookie.
    /// Used for testing CSRF binding validation.
    /// </summary>
    protected HttpRequestMessage CreateCallbackRequestWithMismatchedCookie(string code, string stateToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/auth/callback?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(stateToken)}");

        // Set a different state token in the cookie
        request.Headers.Add("Cookie", $"AuthState={AuthFlowTestFixture.GenerateStateToken()}");

        return request;
    }

    /// <summary>
    /// Creates an HTTP request with the auth cookie set to the specified token.
    /// Used for testing authenticated endpoints (me, set-tenant, refresh).
    /// </summary>
    protected HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", $"AuthToken={token}");
        return request;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Response Parsing Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a callback response that resulted in a redirect (302).
    /// Returns the redirect location URL.
    /// </summary>
    protected static string? ParseRedirectResponse(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Redirect &&
            response.StatusCode != HttpStatusCode.Found)
        {
            return null;
        }

        return response.Headers.Location?.ToString();
    }

    /// <summary>
    /// Parses a callback response that returned a tenant picker JSON response.
    /// </summary>
    protected static async Task<TenantPickerResponse?> ParseTenantPickerResponseAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<TenantPickerResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    /// <summary>
    /// Parses an error response from the callback endpoint.
    /// </summary>
    protected static async Task<ErrorResponse?> ParseErrorResponseAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the auth cookie value from the response Set-Cookie header.
    /// </summary>
    protected static string? ExtractAuthCookie(HttpResponseMessage response)
    {
        return ExtractCookieValue(response, "AuthToken");
    }

    /// <summary>
    /// Extracts a specific cookie value from the response Set-Cookie headers.
    /// </summary>
    protected static string? ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return null;

        foreach (var cookie in cookies)
        {
            if (cookie.StartsWith($"{cookieName}=", StringComparison.OrdinalIgnoreCase))
            {
                var value = cookie.Split(';')[0];
                return value[(cookieName.Length + 1)..];
            }
        }

        return null;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Mock IDP Configuration Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Configures the mocked IDP service to return a successful code exchange response.
    /// The id_token will contain the specified nonce for validation.
    /// </summary>
    /// <param name="sub">The external user ID (Keycloak sub claim).</param>
    /// <param name="nonce">The nonce that the handler will validate against the stored nonce.</param>
    /// <param name="email">The user's email address.</param>
    protected void SetupSuccessfulCodeExchange(string sub, string nonce, string email = "user@example.com")
    {
        var idToken = AuthFlowTestFixture.GenerateFakeIdToken(sub, nonce, email);
        var tokenResponse = new TokenResponseDto(
            AccessToken: "fake-access-token-" + Guid.NewGuid().ToString("N"),
            RefreshToken: "fake-refresh-token",
            ExpiresIn: 300,
            IdToken: idToken);

        Fixture.MockIdentityProviderService
            .ExchangeCodeForTokensAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(Task.FromResult<TokenResponseDto?>(tokenResponse));
    }

    /// <summary>
    /// Configures the mocked IDP service to return null from code exchange (simulating failure).
    /// </summary>
    protected void SetupFailedCodeExchange()
    {
        Fixture.MockIdentityProviderService
            .ExchangeCodeForTokensAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(Task.FromResult<TokenResponseDto?>(null));
    }

    /// <summary>
    /// Configures the mocked IDP service to return user info for a given access token.
    /// </summary>
    /// <param name="sub">The external user ID.</param>
    /// <param name="email">The user's email.</param>
    /// <param name="displayName">The user's display name.</param>
    protected void SetupSuccessfulUserInfo(string sub, string email = "user@example.com", string displayName = "Test User")
    {
        var userInfo = new ExternalUserInfo(sub, email, displayName, null);

        Fixture.MockIdentityProviderService
            .GetUserInfoAsync(Arg.Any<string>())
            .Returns(Task.FromResult<ExternalUserInfo?>(userInfo));
    }

    /// <summary>
    /// Configures the mocked IDP service to return null from userinfo (simulating failure).
    /// </summary>
    protected void SetupFailedUserInfo()
    {
        Fixture.MockIdentityProviderService
            .GetUserInfoAsync(Arg.Any<string>())
            .Returns(Task.FromResult<ExternalUserInfo?>(null));
    }

    /// <summary>
    /// Configures both code exchange and userinfo for a complete successful callback simulation.
    /// </summary>
    /// <param name="sub">The external user ID.</param>
    /// <param name="nonce">The nonce for id_token validation.</param>
    /// <param name="email">The user's email.</param>
    /// <param name="displayName">The user's display name.</param>
    protected void SetupFullSuccessfulCallback(
        string sub,
        string nonce,
        string email = "user@example.com",
        string displayName = "Test User")
    {
        SetupSuccessfulCodeExchange(sub, nonce, email);
        SetupSuccessfulUserInfo(sub, email, displayName);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Database Seeding Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a tenant directly into the auth database.
    /// </summary>
    /// <param name="name">Tenant name.</param>
    /// <param name="slug">Tenant slug (URL-safe identifier).</param>
    /// <param name="tenantType">Tenant type. Default: Standard.</param>
    /// <param name="realmName">Realm name for enterprise tenants. Null for standard.</param>
    /// <returns>The created tenant's ID.</returns>
    protected async Task<Guid> SeedTenantAsync(
        string name,
        string slug,
        TenantType tenantType = TenantType.Standard,
        string? realmName = null)
    {
        await using var context = Fixture.CreateAuthDbContext();
        var tenant = new GroundUp.Auth.Core.Entities.Tenant
        {
            Name = name,
            Slug = slug,
            TenantType = tenantType,
            RealmName = realmName,
            OnboardingMode = OnboardingMode.InviteOnly,
            IsActive = true
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    /// <summary>
    /// Seeds a user directly into the auth database.
    /// </summary>
    /// <param name="externalUserId">The external user ID (Keycloak sub).</param>
    /// <param name="email">The user's email.</param>
    /// <param name="displayName">The user's display name.</param>
    /// <returns>The created user's ID.</returns>
    protected async Task<Guid> SeedUserAsync(
        string externalUserId,
        string email,
        string displayName = "Test User")
    {
        await using var context = Fixture.CreateAuthDbContext();
        var user = new GroundUp.Auth.Core.Entities.User
        {
            ExternalUserId = externalUserId,
            Email = email,
            DisplayName = displayName,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// Seeds a user-tenant membership directly into the auth database.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="tenantId">The tenant's ID.</param>
    /// <param name="isActive">Whether the membership is active. Default: true.</param>
    /// <returns>The created membership's ID.</returns>
    protected async Task<Guid> SeedMembershipAsync(Guid userId, Guid tenantId, bool isActive = true)
    {
        await using var context = Fixture.CreateAuthDbContext();
        var membership = new GroundUp.Auth.Core.Entities.UserTenant
        {
            UserId = userId,
            TenantId = tenantId,
            IsActive = isActive
        };
        context.Set<GroundUp.Auth.Core.Entities.UserTenant>().Add(membership);
        await context.SaveChangesAsync();
        return membership.Id;
    }

    /// <summary>
    /// Seeds a role in a specific tenant.
    /// </summary>
    /// <param name="tenantId">The tenant to create the role in.</param>
    /// <param name="roleName">The role name.</param>
    /// <param name="isSystem">Whether this is a system role (immutable). Default: false.</param>
    /// <returns>The created role's ID.</returns>
    protected async Task<Guid> SeedRoleAsync(Guid tenantId, string roleName, bool isSystem = false)
    {
        await using var context = Fixture.CreateAuthDbContext();
        var role = new GroundUp.Auth.Core.Entities.Role
        {
            Name = roleName,
            TenantId = tenantId,
            IsSystem = isSystem
        };
        context.Set<GroundUp.Auth.Core.Entities.Role>().Add(role);
        await context.SaveChangesAsync();
        return role.Id;
    }

    /// <summary>
    /// Assigns a role to a user within a specific tenant.
    /// </summary>
    protected async Task AssignRoleToUserAsync(Guid userId, Guid roleId, Guid tenantId)
    {
        await using var context = Fixture.CreateAuthDbContext();
        var userRole = new GroundUp.Auth.Core.Entities.UserRole
        {
            UserId = userId,
            RoleId = roleId,
            TenantId = tenantId
        };
        context.Set<GroundUp.Auth.Core.Entities.UserRole>().Add(userRole);
        await context.SaveChangesAsync();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Assertion Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a tenant with the specified slug exists in the database.
    /// </summary>
    protected async Task<GroundUp.Auth.Core.Entities.Tenant?> GetTenantBySlugAsync(string slug)
    {
        await using var context = Fixture.CreateAuthDbContext();
        return await context.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);
    }

    /// <summary>
    /// Verifies that a user with the specified external user ID exists in the database.
    /// </summary>
    protected async Task<GroundUp.Auth.Core.Entities.User?> GetUserByExternalIdAsync(string externalUserId)
    {
        await using var context = Fixture.CreateAuthDbContext();
        return await context.Users.FirstOrDefaultAsync(u => u.ExternalUserId == externalUserId);
    }

    /// <summary>
    /// Gets all active memberships for a user.
    /// </summary>
    protected async Task<List<GroundUp.Auth.Core.Entities.UserTenant>> GetMembershipsForUserAsync(Guid userId)
    {
        await using var context = Fixture.CreateAuthDbContext();
        return await context.Set<GroundUp.Auth.Core.Entities.UserTenant>()
            .Where(ut => ut.UserId == userId && ut.IsActive)
            .ToListAsync();
    }

    /// <summary>
    /// Gets the AuthFlowState by its state token (for asserting state transitions).
    /// </summary>
    protected async Task<AuthFlowState?> GetFlowStateByStateTokenAsync(string stateToken)
    {
        await using var context = Fixture.CreateAuthDbContext();
        return await context.Set<AuthFlowState>()
            .FirstOrDefaultAsync(f => f.StateToken == stateToken);
    }
}

// ────────────────────────────────────────────────────────────────────────
// Response DTOs for test deserialization
// ────────────────────────────────────────────────────────────────────────

/// <summary>
/// Deserialization target for tenant picker JSON responses from the callback endpoint.
/// </summary>
public sealed record TenantPickerResponse
{
    public bool RequiresTenantSelection { get; init; }
    public List<TenantPickerItem>? Tenants { get; init; }
}

/// <summary>
/// Individual tenant item in the picker response.
/// </summary>
public sealed record TenantPickerItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
}

/// <summary>
/// Deserialization target for error responses from auth endpoints.
/// </summary>
public sealed record ErrorResponse
{
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
}
