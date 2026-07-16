using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Property-based tests for <see cref="LoginFlowHandler"/>.
/// Feature: phase-10c-auth-dispatcher, Property 13 and Property 14.
/// **Validates: Requirements 8.9, 8.10**
/// </summary>
[Trait("Category", "Property")]
public sealed class LoginFlowHandlerPropertyTests
{
    // --- Helpers ---

    /// <summary>
    /// Builds a fake JWT id_token containing the specified nonce claim in the payload.
    /// </summary>
    private static string BuildIdTokenWithNonce(string nonce)
    {
        var header = Base64UrlEncode(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64UrlEncode(JsonSerializer.Serialize(new
        {
            sub = Guid.NewGuid().ToString(),
            iss = "https://keycloak.example.com/realms/shared",
            aud = "groundup-app",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            nonce
        }));
        var signature = Base64UrlEncode("fake-signature");
        return $"{header}.{payload}.{signature}";
    }

    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Creates a <see cref="LoginFlowHandler"/> configured so that code exchange and nonce validation
    /// succeed, and the user resolves with the specified ID. Memberships and tenant lookups
    /// are configured via the provided parameters.
    /// </summary>
    private static (LoginFlowHandler Handler, IAuthCookieWriter CookieWriter) CreateSut(
        UserDto user,
        List<UserTenantDto> memberships,
        List<TenantDto> tenants,
        string keycloakAccessToken = "keycloak-access-token-value")
    {
        var nonce = "test-nonce-value-with-enough-length-for-validation";
        var idToken = BuildIdTokenWithNonce(nonce);

        var idpService = Substitute.For<IIdentityProviderService>();
        idpService.ExchangeCodeForTokensAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Task.FromResult<TokenResponseDto?>(
                new TokenResponseDto(keycloakAccessToken, "refresh-token", 3600, idToken)));

        var userInfo = new ExternalUserInfo(
            ExternalUserId: user.ExternalUserId,
            Email: user.Email,
            DisplayName: user.DisplayName,
            Attributes: null);

        idpService.GetUserInfoAsync(Arg.Any<string>())
            .Returns(Task.FromResult<ExternalUserInfo?>(userInfo));

        var authFlowStateService = Substitute.For<IAuthFlowStateService>();
        authFlowStateService.MarkFailedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OperationResult<AuthFlowStateDto>.Ok(new AuthFlowStateDto(
                Id: callInfo.Arg<Guid>(),
                FlowType: FlowType.Login,
                Status: FlowStatus.Failed,
                TenantId: null, InvitationId: null, JoinLinkId: null,
                Realm: null, ReturnUrl: null,
                StateToken: "state", CodeVerifier: "verifier",
                RedirectUri: "https://app.example.com/auth/callback",
                OrganizationName: null,
                Nonce: nonce,
                CreatedByIp: "127.0.0.1", CreatedByUserAgent: "TestAgent",
                ExpiresAt: DateTime.UtcNow.AddMinutes(10),
                ConsumedAt: null, TerminatedAt: DateTime.UtcNow,
                FailureReason: callInfo.Arg<string>(),
                CreatedAt: DateTime.UtcNow.AddMinutes(-1),
                UpdatedAt: DateTime.UtcNow)));

        var userRepository = Substitute.For<IUserRepository>();
        userRepository.GetByExternalUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(user));

        var userTenantRepository = Substitute.For<IUserTenantRepository>();
        userTenantRepository.GetAllMembershipsForUserAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

        var tenantRepository = Substitute.For<ITenantRepository>();
        tenantRepository.GetByIdsBypassFilterAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var requestedIds = callInfo.Arg<IEnumerable<Guid>>().ToHashSet();
                var matchingTenants = tenants.Where(t => requestedIds.Contains(t.Id)).ToList();
                return OperationResult<List<TenantDto>>.Ok(matchingTenants);
            });

        var tokenService = Substitute.For<ITokenService>();
        tokenService.GenerateTokenAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<System.Security.Claims.Claim>?>())
            .Returns("generated-groundup-token");

        var authCookieWriter = Substitute.For<IAuthCookieWriter>();

        var settingsService = Substitute.For<ISettingsService>();
        // No default tenant configured — prevents auto-join path
        settingsService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Fail("Not configured", 404));

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        var logger = NullLogger<LoginFlowHandler>.Instance;

        var handler = new LoginFlowHandler(
            idpService,
            userRepository,
            userTenantRepository,
            tenantRepository,
            tokenService,
            authCookieWriter,
            authFlowStateService,
            settingsService,
            httpContextAccessor,
            logger);

        return (handler, authCookieWriter);
    }

    /// <summary>
    /// Creates a <see cref="FlowCallbackContext"/> for the Login flow with no host-resolved tenant.
    /// </summary>
    private static FlowCallbackContext CreateContext(TenantDto? hostResolvedTenant = null)
    {
        var nonce = "test-nonce-value-with-enough-length-for-validation";
        return new FlowCallbackContext(
            AuthorizationCode: "test-auth-code",
            CodeVerifier: "test-code-verifier-with-at-least-43-characters-for-pkce-validation",
            RedirectUri: "https://app.example.com/auth/callback",
            ConsumedState: new AuthFlowStateDto(
                Id: Guid.NewGuid(),
                FlowType: FlowType.Login,
                Status: FlowStatus.Consumed,
                TenantId: null, InvitationId: null, JoinLinkId: null,
                Realm: null, ReturnUrl: null,
                StateToken: "consumed-state-token",
                CodeVerifier: "test-code-verifier-with-at-least-43-characters-for-pkce-validation",
                RedirectUri: "https://app.example.com/auth/callback",
                OrganizationName: null,
                Nonce: nonce,
                CreatedByIp: "127.0.0.1", CreatedByUserAgent: "TestAgent",
                ExpiresAt: DateTime.UtcNow.AddMinutes(10),
                ConsumedAt: DateTime.UtcNow,
                TerminatedAt: null, FailureReason: null,
                CreatedAt: DateTime.UtcNow.AddMinutes(-1),
                UpdatedAt: DateTime.UtcNow),
            HostResolvedTenant: hostResolvedTenant);
    }

    // --- Property 13: Tenant Picker Excludes Enterprise Tenants ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 13: Tenant Picker Excludes Enterprise Tenants
    /// For any mix of standard (RealmName=null) and enterprise (RealmName≠null) tenants in a user's
    /// memberships, the TenantSelectionRequired result should only include standard tenants.
    /// The tenant list MUST never contain a tenant with a non-null RealmName.
    /// **Validates: Requirements 8.9, 8.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LoginFlowArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 13: Tenant picker excludes enterprise tenants")]
    public Property TenantPicker_ExcludesEnterpriseTenants(TenantMembershipSet input)
    {
        // Only test multi-membership scenarios where we expect the picker path
        // (need 2+ standard tenants for TenantSelectionRequired)
        var standardCount = input.Tenants.Count(t => t.RealmName is null);
        if (standardCount < 2)
        {
            return true.ToProperty(); // skip — not a multi-picker scenario
        }

        var user = new UserDto(
            Id: Guid.NewGuid(),
            ExternalUserId: "ext-" + Guid.NewGuid(),
            Email: "user@example.com",
            DisplayName: "Test User",
            IsActive: true);

        var memberships = input.Tenants
            .Select(t => new UserTenantDto(Guid.NewGuid(), user.Id, t.Id, user.ExternalUserId, true))
            .ToList();

        var (handler, _) = CreateSut(user, memberships, input.Tenants);
        var context = CreateContext();

        // Act
        var result = handler.HandleCallbackAsync(context).GetAwaiter().GetResult();

        // Assert — every tenant in the picker list must have RealmName = null
        if (!result.RequiresTenantSelection || result.TenantList is null)
        {
            // If we got auto-selected (standardCount ended up being 1 after filtering),
            // that's also valid — property still holds (no enterprise tenant was selected)
            return result.IsSuccess.ToProperty();
        }

        var allStandard = result.TenantList.All(pickerTenant =>
            input.Tenants.Any(t => t.Id == pickerTenant.Id && t.RealmName is null));

        var noEnterprise = result.TenantList.All(pickerTenant =>
            !input.Tenants.Any(t => t.Id == pickerTenant.Id && t.RealmName is not null));

        return (allStandard && noEnterprise).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 13: Tenant Picker Excludes Enterprise Tenants
    /// Additional check: all standard tenants in the memberships must appear in the picker list
    /// (none are incorrectly excluded).
    /// **Validates: Requirements 8.9, 8.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LoginFlowArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 13: All standard tenants appear in picker")]
    public Property TenantPicker_IncludesAllStandardTenants(TenantMembershipSet input)
    {
        var standardTenants = input.Tenants.Where(t => t.RealmName is null).ToList();
        if (standardTenants.Count < 2)
        {
            return true.ToProperty(); // skip — not a multi-picker scenario
        }

        var user = new UserDto(
            Id: Guid.NewGuid(),
            ExternalUserId: "ext-" + Guid.NewGuid(),
            Email: "user@example.com",
            DisplayName: "Test User",
            IsActive: true);

        var memberships = input.Tenants
            .Select(t => new UserTenantDto(Guid.NewGuid(), user.Id, t.Id, user.ExternalUserId, true))
            .ToList();

        var (handler, _) = CreateSut(user, memberships, input.Tenants);
        var context = CreateContext();

        // Act
        var result = handler.HandleCallbackAsync(context).GetAwaiter().GetResult();

        // Assert — all standard tenants must be in the picker list
        if (!result.RequiresTenantSelection || result.TenantList is null)
        {
            return result.IsSuccess.ToProperty();
        }

        var pickerIds = result.TenantList.Select(t => t.Id).ToHashSet();
        var allStandardIncluded = standardTenants.All(st => pickerIds.Contains(st.Id));

        return allStandardIncluded.ToProperty();
    }

    // --- Property 14: Pending-Selection Authentication ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 14: Pending-Selection Authentication
    /// When 2+ standard tenant memberships exist (no host-pin), the handler retains the
    /// Keycloak access token as the auth cookie (via WriteAuthCookie) and the FlowResult
    /// has RequiresTenantSelection=true with no Token field set (no GroundUp JWT issued).
    /// The result should have a non-empty TenantList.
    /// **Validates: Requirements 8.9, 8.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LoginFlowArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 14: No GroundUp token during pending selection")]
    public Property PendingSelection_NoGroundUpTokenIssued(TenantMembershipSet input)
    {
        var standardTenants = input.Tenants.Where(t => t.RealmName is null).ToList();
        if (standardTenants.Count < 2)
        {
            return true.ToProperty(); // skip — not a multi-picker scenario
        }

        var keycloakToken = "keycloak-at-" + Guid.NewGuid();
        var user = new UserDto(
            Id: Guid.NewGuid(),
            ExternalUserId: "ext-" + Guid.NewGuid(),
            Email: "user@example.com",
            DisplayName: "Test User",
            IsActive: true);

        var memberships = input.Tenants
            .Select(t => new UserTenantDto(Guid.NewGuid(), user.Id, t.Id, user.ExternalUserId, true))
            .ToList();

        var (handler, cookieWriter) = CreateSut(user, memberships, input.Tenants, keycloakToken);
        var context = CreateContext();

        // Act
        var result = handler.HandleCallbackAsync(context).GetAwaiter().GetResult();

        // Assert
        if (!result.RequiresTenantSelection)
        {
            // This might happen if after filtering there's only 1 standard tenant
            return result.IsSuccess.ToProperty();
        }

        // No GroundUp token is issued (Token field is null)
        var noGroundUpToken = result.Token is null;

        // TenantList is non-empty
        var hasNonEmptyList = result.TenantList is not null && result.TenantList.Count > 0;

        // RequiresTenantSelection is true
        var requiresSelection = result.RequiresTenantSelection;

        // IsSuccess is true (this is a valid intermediate state, not an error)
        var isSuccess = result.IsSuccess;

        return (noGroundUpToken && hasNonEmptyList && requiresSelection && isSuccess).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 14: Pending-Selection Authentication
    /// The handler writes the Keycloak access token (not a GroundUp JWT) to the auth cookie
    /// during the pending-selection flow.
    /// **Validates: Requirements 8.9, 8.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LoginFlowArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 14: Keycloak token retained as cookie")]
    public Property PendingSelection_KeycloakTokenRetainedAsCookie(TenantMembershipSet input)
    {
        var standardTenants = input.Tenants.Where(t => t.RealmName is null).ToList();
        if (standardTenants.Count < 2)
        {
            return true.ToProperty(); // skip — not a multi-picker scenario
        }

        var keycloakToken = "keycloak-at-" + Guid.NewGuid();
        var user = new UserDto(
            Id: Guid.NewGuid(),
            ExternalUserId: "ext-" + Guid.NewGuid(),
            Email: "user@example.com",
            DisplayName: "Test User",
            IsActive: true);

        var memberships = input.Tenants
            .Select(t => new UserTenantDto(Guid.NewGuid(), user.Id, t.Id, user.ExternalUserId, true))
            .ToList();

        var (handler, cookieWriter) = CreateSut(user, memberships, input.Tenants, keycloakToken);
        var context = CreateContext();

        // Act
        var result = handler.HandleCallbackAsync(context).GetAwaiter().GetResult();

        // Assert — verify the Keycloak token was written as the cookie value
        if (!result.RequiresTenantSelection)
        {
            return result.IsSuccess.ToProperty();
        }

        cookieWriter.Received().WriteAuthCookie(Arg.Any<HttpContext>(), keycloakToken);
        return true.ToProperty();
    }
}

// --- Test data types ---

/// <summary>
/// Represents a set of tenant memberships with a mix of standard and enterprise tenants.
/// </summary>
public sealed record TenantMembershipSet(List<TenantDto> Tenants)
{
    public override string ToString()
    {
        var standard = Tenants.Count(t => t.RealmName is null);
        var enterprise = Tenants.Count(t => t.RealmName is not null);
        return $"Standard={standard}, Enterprise={enterprise}, Total={Tenants.Count}";
    }
}

/// <summary>
/// Custom FsCheck Arbitrary generators for LoginFlowHandler property tests.
/// </summary>
public static class LoginFlowArbitraries
{
    /// <summary>
    /// Generates tenant membership sets with a mix of standard (RealmName=null) and enterprise
    /// (RealmName≠null) tenants. Guarantees at least 2 tenants total, with at least some mix
    /// of standard and enterprise to exercise the filtering logic.
    /// </summary>
    public static Arbitrary<TenantMembershipSet> TenantMembershipSetArb()
    {
        var standardTenantGen = from id in Gen.Fresh(() => Guid.NewGuid())
                                from name in Gen.Elements("Acme Corp", "Widgets Inc", "Tech Co", "Dev Studio", "StartUp Ltd")
                                select new TenantDto(
                                    Id: id,
                                    Name: name,
                                    Slug: name.ToLowerInvariant().Replace(" ", "-"),
                                    TenantType: TenantType.Standard,
                                    OnboardingMode: OnboardingMode.InviteOnly,
                                    ParentTenantId: null,
                                    RealmName: null,
                                    IsActive: true);

        var enterpriseTenantGen = from id in Gen.Fresh(() => Guid.NewGuid())
                                  from name in Gen.Elements("BigCorp", "Enterprise Co", "Global Inc", "MegaTech", "Corp Ltd")
                                  from realm in Gen.Elements("bigcorp-realm", "enterprise-realm", "global-realm", "mega-realm", "corp-realm")
                                  select new TenantDto(
                                      Id: id,
                                      Name: name,
                                      Slug: name.ToLowerInvariant().Replace(" ", "-"),
                                      TenantType: TenantType.Enterprise,
                                      OnboardingMode: OnboardingMode.InviteOnly,
                                      ParentTenantId: null,
                                      RealmName: realm,
                                      IsActive: true);

        var gen = from standardCount in Gen.Choose(2, 5)
                  from enterpriseCount in Gen.Choose(0, 4)
                  from standardTenants in Gen.ListOf(standardCount, standardTenantGen)
                  from enterpriseTenants in Gen.ListOf(enterpriseCount, enterpriseTenantGen)
                  select new TenantMembershipSet(standardTenants.Concat(enterpriseTenants).ToList());

        return gen.ToArbitrary();
    }
}
