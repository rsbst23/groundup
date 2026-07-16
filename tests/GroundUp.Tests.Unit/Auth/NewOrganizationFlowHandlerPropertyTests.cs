using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Property-based tests for <see cref="NewOrganizationFlowHandler"/>.
/// Feature: phase-10c-auth-dispatcher, Property 9: OIDC Nonce Validation.
/// **Validates: Requirements 7.2**
/// </summary>
[Trait("Category", "Property")]
public sealed class NewOrganizationFlowHandlerPropertyTests
{
    /// <summary>
    /// Builds a fake JWT id_token containing the specified nonce claim in the payload.
    /// The handler extracts the nonce from the id_token payload claims; this helper
    /// creates a token whose middle segment (Base64URL-encoded JSON) includes a "nonce" field.
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

    /// <summary>
    /// Builds a fake JWT id_token WITHOUT a nonce claim.
    /// </summary>
    private static string BuildIdTokenWithoutNonce()
    {
        var header = Base64UrlEncode(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64UrlEncode(JsonSerializer.Serialize(new
        {
            sub = Guid.NewGuid().ToString(),
            iss = "https://keycloak.example.com/realms/shared",
            aud = "groundup-app",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
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
    /// Creates a <see cref="NewOrganizationFlowHandler"/> with mocked dependencies.
    /// The IIdentityProviderService is configured to return a TokenResponseDto with
    /// the specified id_token and a valid access token. Other dependencies are configured
    /// to succeed by default so the test isolates nonce validation behavior.
    /// </summary>
    private static NewOrganizationFlowHandler CreateSut(string? idToken)
    {
        var idpService = Substitute.For<IIdentityProviderService>();
        var tokenResponse = idToken is not null
            ? new TokenResponseDto("access-token-value", "refresh-token", 3600, idToken)
            : null;

        idpService.ExchangeCodeForTokensAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Task.FromResult(tokenResponse));

        // Default userinfo — returned when nonce passes and userinfo is requested
        var userInfo = new ExternalUserInfo(
            ExternalUserId: Guid.NewGuid().ToString(),
            Email: "founder@example.com",
            DisplayName: "Test Founder",
            Attributes: null);

        idpService.GetUserInfoAsync(Arg.Any<string>())
            .Returns(Task.FromResult<ExternalUserInfo?>(userInfo));

        var authFlowStateService = Substitute.For<IAuthFlowStateService>();
        authFlowStateService.MarkFailedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OperationResult<AuthFlowStateDto>.Ok(new AuthFlowStateDto(
                Id: callInfo.Arg<Guid>(),
                FlowType: FlowType.NewOrganization,
                Status: FlowStatus.Failed,
                TenantId: null,
                InvitationId: null,
                JoinLinkId: null,
                Realm: null,
                ReturnUrl: null,
                StateToken: "state",
                CodeVerifier: "verifier",
                RedirectUri: "https://app.example.com/auth/callback",
                OrganizationName: "Test Org",
                Nonce: "nonce",
                CreatedByIp: "127.0.0.1",
                CreatedByUserAgent: "TestAgent",
                ExpiresAt: DateTime.UtcNow.AddMinutes(10),
                ConsumedAt: null,
                TerminatedAt: DateTime.UtcNow,
                FailureReason: callInfo.Arg<string>(),
                CreatedAt: DateTime.UtcNow.AddMinutes(-1),
                UpdatedAt: DateTime.UtcNow)));

        var userRepository = Substitute.For<IUserRepository>();
        userRepository.GetByExternalUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Fail("Not found", 404));
        userRepository.AddAsync(Arg.Any<UserDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.Arg<UserDto>();
                return OperationResult<UserDto>.Ok(dto);
            });

        var tenantRepository = Substitute.For<ITenantRepository>();
        tenantRepository.AddAsync(Arg.Any<TenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.Arg<TenantDto>();
                return OperationResult<TenantDto>.Ok(dto);
            });

        var userTenantRepository = Substitute.For<IUserTenantRepository>();
        userTenantRepository.AddAsync(Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.Arg<UserTenantDto>();
                return OperationResult<UserTenantDto>.Ok(dto);
            });

        var roleRepository = Substitute.For<IRoleRepository>();
        roleRepository.AddAsync(Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.Arg<RoleDto>();
                return OperationResult<RoleDto>.Ok(dto);
            });

        var userRoleRepository = Substitute.For<IUserRoleRepository>();
        userRoleRepository.AddAsync(Arg.Any<UserRoleDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.Arg<UserRoleDto>();
                return OperationResult<UserRoleDto>.Ok(dto);
            });

        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var operation = callInfo.Arg<Func<CancellationToken, Task>>();
                await operation(CancellationToken.None);
                return OperationResult.Ok();
            });

        var tokenService = Substitute.For<ITokenService>();
        tokenService.GenerateTokenAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<System.Security.Claims.Claim>?>())
            .Returns("generated-groundup-token");

        var authCookieWriter = Substitute.For<IAuthCookieWriter>();

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        var logger = NullLogger<NewOrganizationFlowHandler>.Instance;

        return new NewOrganizationFlowHandler(
            idpService,
            authFlowStateService,
            tenantRepository,
            userRepository,
            userTenantRepository,
            userRoleRepository,
            roleRepository,
            unitOfWork,
            tokenService,
            authCookieWriter,
            httpContextAccessor,
            new TenantContext(),
            logger);
    }

    /// <summary>
    /// Creates a <see cref="FlowCallbackContext"/> with the specified stored nonce.
    /// </summary>
    private static FlowCallbackContext CreateContext(string storedNonce) =>
        new(
            AuthorizationCode: "test-auth-code",
            CodeVerifier: "test-code-verifier-with-at-least-43-characters-for-pkce-validation",
            RedirectUri: "https://app.example.com/auth/callback",
            ConsumedState: new AuthFlowStateDto(
                Id: Guid.NewGuid(),
                FlowType: FlowType.NewOrganization,
                Status: FlowStatus.Consumed,
                TenantId: null,
                InvitationId: null,
                JoinLinkId: null,
                Realm: null,
                ReturnUrl: null,
                StateToken: "consumed-state-token",
                CodeVerifier: "test-code-verifier-with-at-least-43-characters-for-pkce-validation",
                RedirectUri: "https://app.example.com/auth/callback",
                OrganizationName: "Test Organization",
                Nonce: storedNonce,
                CreatedByIp: "127.0.0.1",
                CreatedByUserAgent: "TestAgent",
                ExpiresAt: DateTime.UtcNow.AddMinutes(10),
                ConsumedAt: DateTime.UtcNow,
                TerminatedAt: null,
                FailureReason: null,
                CreatedAt: DateTime.UtcNow.AddMinutes(-1),
                UpdatedAt: DateTime.UtcNow),
            HostResolvedTenant: null);

    // --- Property 9: OIDC Nonce Validation ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 9: OIDC Nonce Validation
    /// For any nonce value, when the id_token nonce claim equals the stored nonce,
    /// nonce validation SHALL pass (flow does NOT fail with NONCE_MISMATCH).
    /// **Validates: Requirements 7.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NonceValidationArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 9: Matching nonce passes validation")]
    public Property MatchingNonce_PassesValidation(ValidNonceValue input)
    {
        // Arrange — id_token contains the same nonce as stored in AuthFlowState
        var idToken = BuildIdTokenWithNonce(input.Nonce);
        var handler = CreateSut(idToken);
        var context = CreateContext(storedNonce: input.Nonce);

        // Act
        var result = handler.HandleCallbackAsync(context).GetAwaiter().GetResult();

        // Assert — should NOT fail with NONCE_MISMATCH
        return (result.ErrorCode != "NONCE_MISMATCH").ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 9: OIDC Nonce Validation
    /// For any pair of distinct nonce values, when the id_token nonce claim does NOT equal
    /// the stored nonce, nonce validation SHALL fail (flow returns NONCE_MISMATCH error).
    /// **Validates: Requirements 7.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NonceValidationArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 9: Mismatched nonce fails validation")]
    public Property MismatchedNonce_FailsValidation(MismatchedNonceValues input)
    {
        // Arrange — id_token contains a DIFFERENT nonce than stored in AuthFlowState
        var idToken = BuildIdTokenWithNonce(input.IdTokenNonce);
        var handler = CreateSut(idToken);
        var context = CreateContext(storedNonce: input.StoredNonce);

        // Act
        var result = handler.HandleCallbackAsync(context).GetAwaiter().GetResult();

        // Assert — should fail with NONCE_MISMATCH error
        return (!result.IsSuccess
            && result.ErrorCode == "NONCE_MISMATCH")
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 9: OIDC Nonce Validation
    /// When the id_token does not contain a nonce claim at all, nonce validation SHALL fail
    /// (the missing nonce cannot equal the stored nonce).
    /// **Validates: Requirements 7.2**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(NonceValidationArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 9: Missing nonce claim fails validation")]
    public Property MissingNonceClaim_FailsValidation(ValidNonceValue input)
    {
        // Arrange — id_token has NO nonce claim, stored nonce is non-empty
        var idToken = BuildIdTokenWithoutNonce();
        var handler = CreateSut(idToken);
        var context = CreateContext(storedNonce: input.Nonce);

        // Act
        var result = handler.HandleCallbackAsync(context).GetAwaiter().GetResult();

        // Assert — should fail with NONCE_MISMATCH error
        return (!result.IsSuccess
            && result.ErrorCode == "NONCE_MISMATCH")
            .ToProperty();
    }
}

// --- Test data types ---

/// <summary>
/// Represents a valid nonce value (non-empty Base64URL-like string of ≥32 bytes randomness).
/// </summary>
public sealed record ValidNonceValue(string Nonce)
{
    public override string ToString() => $"Nonce={Nonce[..Math.Min(Nonce.Length, 16)]}...";
}

/// <summary>
/// Represents a pair of distinct nonce values: one in the id_token and one stored in AuthFlowState.
/// </summary>
public sealed record MismatchedNonceValues(string IdTokenNonce, string StoredNonce)
{
    public override string ToString() =>
        $"IdToken={IdTokenNonce[..Math.Min(IdTokenNonce.Length, 12)]}..., Stored={StoredNonce[..Math.Min(StoredNonce.Length, 12)]}...";
}

/// <summary>
/// Custom FsCheck Arbitrary generators for OIDC nonce validation property tests.
/// </summary>
public static class NonceValidationArbitraries
{
    private static readonly char[] Base64UrlChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".ToCharArray();

    /// <summary>
    /// Generates valid nonce values (Base64URL strings of 43+ characters,
    /// simulating the nonces produced by AuthUrlBuilderService with ≥32 bytes of randomness).
    /// </summary>
    public static Arbitrary<ValidNonceValue> ValidNonceValueArb()
    {
        var gen = from length in Gen.Choose(43, 64)
                  from chars in Gen.ArrayOf(length, Gen.Elements(Base64UrlChars))
                  select new ValidNonceValue(new string(chars));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates pairs of DISTINCT nonce values guaranteed to be different,
    /// for testing mismatch scenarios.
    /// </summary>
    public static Arbitrary<MismatchedNonceValues> MismatchedNonceValuesArb()
    {
        var gen = from length1 in Gen.Choose(43, 64)
                  from chars1 in Gen.ArrayOf(length1, Gen.Elements(Base64UrlChars))
                  from length2 in Gen.Choose(43, 64)
                  from chars2 in Gen.ArrayOf(length2, Gen.Elements(Base64UrlChars))
                  let nonce1 = new string(chars1)
                  let nonce2 = new string(chars2)
                  where nonce1 != nonce2
                  select new MismatchedNonceValues(nonce1, nonce2);

        return gen.ToArbitrary();
    }
}
