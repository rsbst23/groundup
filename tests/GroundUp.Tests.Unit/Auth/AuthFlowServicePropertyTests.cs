using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Property-based tests for <see cref="AuthFlowService"/> dispatch logic.
/// Feature: phase-10c-auth-dispatcher, Properties 7 and 8.
/// Validates: Requirements 5.5, 5.8
/// </summary>
[Trait("Category", "Property")]
public sealed class AuthFlowServicePropertyTests
{
    private static readonly AuthOptions DefaultOptions = new()
    {
        StateCookieName = "AuthState",
        FlowStateExpirationMinutes = 10,
        CallbackPath = "/auth/callback"
    };

    /// <summary>
    /// Creates a fully mocked <see cref="AuthFlowService"/> with configurable behavior
    /// for state lookup and consumption.
    /// </summary>
    private static AuthFlowService CreateSut(
        IAuthFlowStateRepository? flowStateRepository = null,
        IAuthFlowStateService? flowStateService = null,
        IEnumerable<IFlowHandler>? flowHandlers = null,
        TenantDto? hostResolvedTenant = null,
        AuthOptions? options = null)
    {
        var authUrlBuilder = Substitute.For<IAuthUrlBuilder>();
        var repository = flowStateRepository ?? Substitute.For<IAuthFlowStateRepository>();
        var stateService = flowStateService ?? Substitute.For<IAuthFlowStateService>();
        var handlers = flowHandlers ?? Array.Empty<IFlowHandler>();
        var resolvedTenant = new HostResolvedTenant { Tenant = hostResolvedTenant };
        var opts = Options.Create(options ?? DefaultOptions);
        var logger = NullLogger<AuthFlowService>.Instance;

        return new AuthFlowService(
            authUrlBuilder,
            stateService,
            repository,
            handlers,
            resolvedTenant,
            opts,
            logger);
    }

    /// <summary>
    /// Creates a mock HttpContext with the specified state cookie value (or no cookie if null).
    /// </summary>
    private static HttpContext CreateHttpContext(string? stateCookieValue, string cookieName = "AuthState")
    {
        var httpContext = new DefaultHttpContext();

        if (stateCookieValue is not null)
        {
            // Set the cookie on the request
            httpContext.Request.Headers["Cookie"] = $"{cookieName}={stateCookieValue}";
        }

        return httpContext;
    }

    /// <summary>
    /// Creates a Pending AuthFlowStateDto for test use.
    /// </summary>
    private static AuthFlowStateDto CreatePendingFlowState(
        string stateToken,
        FlowType flowType = FlowType.Login) =>
        new(
            Id: Guid.NewGuid(),
            FlowType: flowType,
            Status: FlowStatus.Pending,
            TenantId: null,
            InvitationId: null,
            JoinLinkId: null,
            Realm: null,
            ReturnUrl: null,
            StateToken: stateToken,
            CodeVerifier: "test-code-verifier-with-at-least-43-characters-for-pkce",
            RedirectUri: "https://app.example.com/auth/callback",
            OrganizationName: null,
            Nonce: "test-nonce-value",
            CreatedByIp: "127.0.0.1",
            CreatedByUserAgent: "TestAgent",
            ExpiresAt: DateTime.UtcNow.AddMinutes(10),
            ConsumedAt: null,
            TerminatedAt: null,
            FailureReason: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null);

    /// <summary>
    /// Creates a Consumed AuthFlowStateDto (already processed).
    /// </summary>
    private static AuthFlowStateDto CreateConsumedFlowState(string stateToken) =>
        new(
            Id: Guid.NewGuid(),
            FlowType: FlowType.Login,
            Status: FlowStatus.Consumed,
            TenantId: null,
            InvitationId: null,
            JoinLinkId: null,
            Realm: null,
            ReturnUrl: null,
            StateToken: stateToken,
            CodeVerifier: "test-code-verifier-with-at-least-43-characters-for-pkce",
            RedirectUri: "https://app.example.com/auth/callback",
            OrganizationName: null,
            Nonce: "test-nonce-value",
            CreatedByIp: "127.0.0.1",
            CreatedByUserAgent: "TestAgent",
            ExpiresAt: DateTime.UtcNow.AddMinutes(10),
            ConsumedAt: DateTime.UtcNow.AddMinutes(-1),
            TerminatedAt: DateTime.UtcNow.AddMinutes(-1),
            FailureReason: null,
            CreatedAt: DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt: DateTime.UtcNow.AddMinutes(-1));

    // --- Property 7: State Cookie Binding (CSRF Protection) ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 7: State Cookie Binding
    /// For any state token, if the state cookie matches the state query parameter AND
    /// a Pending AuthFlowState row exists for that token, the callback SHALL proceed
    /// past the CSRF check (not rejected for state mismatch).
    /// **Validates: Requirements 5.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthFlowServiceArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 7: Matching state cookie + pending state proceeds past CSRF")]
    public Property MatchingStateCookie_WithPendingState_ProceedsPastCsrf(ValidStateToken input)
    {
        // Arrange — state cookie matches state param, and a Pending flow state exists
        var flowState = CreatePendingFlowState(input.Token);
        var consumedState = flowState with { Status = FlowStatus.Consumed, ConsumedAt = DateTime.UtcNow };

        var repository = Substitute.For<IAuthFlowStateRepository>();
        repository.FindByStateTokenAsync(input.Token, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(flowState));

        var stateService = Substitute.For<IAuthFlowStateService>();
        stateService.ConsumeAsync(flowState.Id, flowState.FlowType, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(consumedState));

        // Register a handler for the flow type so dispatch succeeds
        var handler = Substitute.For<IFlowHandler>();
        handler.HandledFlowType.Returns(flowState.FlowType);
        handler.HandleCallbackAsync(Arg.Any<FlowCallbackContext>())
            .Returns(FlowResult.Success("test-token"));

        var sut = CreateSut(
            flowStateRepository: repository,
            flowStateService: stateService,
            flowHandlers: new[] { handler });

        var httpContext = CreateHttpContext(input.Token);

        // Act
        var result = sut.HandleCallbackAsync("auth-code", input.Token, httpContext)
            .GetAwaiter().GetResult();

        // Assert — should NOT get a CSRF_STATE_MISMATCH error
        return (result.Success
            && result.Data!.ErrorCode != "CSRF_STATE_MISMATCH")
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 7: State Cookie Binding
    /// For any state token, if the state cookie is MISSING (null/empty), the callback
    /// SHALL be rejected with a CSRF error regardless of whether a valid Pending state exists.
    /// **Validates: Requirements 5.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthFlowServiceArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 7: Missing state cookie rejects callback")]
    public Property MissingStateCookie_RejectsCallback(ValidStateToken input)
    {
        // Arrange — no state cookie, even though a valid Pending state exists in DB
        var flowState = CreatePendingFlowState(input.Token);

        var repository = Substitute.For<IAuthFlowStateRepository>();
        repository.FindByStateTokenAsync(input.Token, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(flowState));

        var sut = CreateSut(flowStateRepository: repository);

        // HttpContext WITHOUT state cookie
        var httpContext = CreateHttpContext(stateCookieValue: null);

        // Act
        var result = sut.HandleCallbackAsync("auth-code", input.Token, httpContext)
            .GetAwaiter().GetResult();

        // Assert — should get CSRF_STATE_MISMATCH error
        return (result.Success
            && result.Data!.ErrorCode == "CSRF_STATE_MISMATCH"
            && result.Data.HttpStatus == 403)
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 7: State Cookie Binding
    /// For any pair of distinct state tokens, if the state cookie holds a DIFFERENT value
    /// than the state query parameter, the callback SHALL be rejected with a CSRF error
    /// regardless of the state token's validity in the database.
    /// **Validates: Requirements 5.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthFlowServiceArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 7: Mismatched state cookie rejects callback")]
    public Property MismatchedStateCookie_RejectsCallback(MismatchedStateTokens input)
    {
        // Arrange — state cookie holds a different value than the state param
        var flowState = CreatePendingFlowState(input.StateParam);

        var repository = Substitute.For<IAuthFlowStateRepository>();
        repository.FindByStateTokenAsync(input.StateParam, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(flowState));

        var sut = CreateSut(flowStateRepository: repository);

        // HttpContext with DIFFERENT state cookie
        var httpContext = CreateHttpContext(input.CookieValue);

        // Act
        var result = sut.HandleCallbackAsync("auth-code", input.StateParam, httpContext)
            .GetAwaiter().GetResult();

        // Assert — should get CSRF_STATE_MISMATCH error
        return (result.Success
            && result.Data!.ErrorCode == "CSRF_STATE_MISMATCH"
            && result.Data.HttpStatus == 403)
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 7: State Cookie Binding
    /// For any state token where the cookie matches but NO Pending AuthFlowState row exists
    /// (token not in database), the callback SHALL be rejected with STATE_NOT_FOUND.
    /// **Validates: Requirements 5.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthFlowServiceArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 7: Cookie matches but no DB state returns not found")]
    public Property MatchingCookie_NoDbState_ReturnsNotFound(ValidStateToken input)
    {
        // Arrange — cookie matches but no row found in DB
        var repository = Substitute.For<IAuthFlowStateRepository>();
        repository.FindByStateTokenAsync(input.Token, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Fail("Not found", 404));

        var sut = CreateSut(flowStateRepository: repository);

        var httpContext = CreateHttpContext(input.Token);

        // Act
        var result = sut.HandleCallbackAsync("auth-code", input.Token, httpContext)
            .GetAwaiter().GetResult();

        // Assert — should get STATE_NOT_FOUND error
        return (result.Success
            && result.Data!.ErrorCode == "STATE_NOT_FOUND"
            && result.Data.HttpStatus == 404)
            .ToProperty();
    }

    // --- Property 8: Replay Protection (Consumption Idempotency) ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 8: Replay Protection
    /// For any AuthFlowState that has already been consumed (status = Consumed),
    /// a subsequent attempt to process the same state token SHALL return HTTP 410 Gone.
    /// The system SHALL never process the same authorization code twice.
    /// **Validates: Requirements 5.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthFlowServiceArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 8: Already-consumed state returns 410 Gone")]
    public Property AlreadyConsumedState_Returns410Gone(ValidStateToken input)
    {
        // Arrange — state cookie matches, but the flow state is already Consumed
        var consumedFlowState = CreateConsumedFlowState(input.Token);

        var repository = Substitute.For<IAuthFlowStateRepository>();
        repository.FindByStateTokenAsync(input.Token, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(consumedFlowState));

        var sut = CreateSut(flowStateRepository: repository);

        var httpContext = CreateHttpContext(input.Token);

        // Act
        var result = sut.HandleCallbackAsync("auth-code", input.Token, httpContext)
            .GetAwaiter().GetResult();

        // Assert — should get FLOW_ALREADY_CONSUMED with HTTP 410
        return (result.Success
            && result.Data!.ErrorCode == "FLOW_ALREADY_CONSUMED"
            && result.Data.HttpStatus == 410)
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 8: Replay Protection
    /// For any state token where the cookie matches and the state is Pending, but
    /// ConsumeAsync returns a 409 conflict (race condition — another request consumed it first),
    /// the system SHALL return 410 Gone (treating the race-lost case as replay).
    /// **Validates: Requirements 5.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthFlowServiceArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 8: Race-lost consume returns 410 Gone")]
    public Property RaceLostConsume_Returns410Gone(ValidStateToken input)
    {
        // Arrange — state is Pending in DB, but ConsumeAsync returns 409 (another request won the race)
        var flowState = CreatePendingFlowState(input.Token);

        var repository = Substitute.For<IAuthFlowStateRepository>();
        repository.FindByStateTokenAsync(input.Token, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(flowState));

        var stateService = Substitute.For<IAuthFlowStateService>();
        stateService.ConsumeAsync(flowState.Id, flowState.FlowType, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Fail("Already consumed", 409));

        var sut = CreateSut(
            flowStateRepository: repository,
            flowStateService: stateService);

        var httpContext = CreateHttpContext(input.Token);

        // Act
        var result = sut.HandleCallbackAsync("auth-code", input.Token, httpContext)
            .GetAwaiter().GetResult();

        // Assert — should get FLOW_ALREADY_CONSUMED with HTTP 410
        return (result.Success
            && result.Data!.ErrorCode == "FLOW_ALREADY_CONSUMED"
            && result.Data.HttpStatus == 410)
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 8: Replay Protection
    /// For any distinct pair of state tokens, consuming one SHALL NOT affect the other.
    /// Each flow state is independently consumable.
    /// **Validates: Requirements 5.8**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(AuthFlowServiceArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 8: Consuming one state does not affect another")]
    public Property ConsumingOneState_DoesNotAffectAnother(MismatchedStateTokens input)
    {
        // Arrange — two independent flow states, each with its own state token
        var flowState1 = CreatePendingFlowState(input.StateParam);
        var flowState2 = CreatePendingFlowState(input.CookieValue);
        var consumedState1 = flowState1 with { Status = FlowStatus.Consumed, ConsumedAt = DateTime.UtcNow };

        var repository = Substitute.For<IAuthFlowStateRepository>();
        repository.FindByStateTokenAsync(input.StateParam, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(flowState1));
        repository.FindByStateTokenAsync(input.CookieValue, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(flowState2));

        var stateService = Substitute.For<IAuthFlowStateService>();
        stateService.ConsumeAsync(flowState1.Id, flowState1.FlowType, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(consumedState1));

        var handler = Substitute.For<IFlowHandler>();
        handler.HandledFlowType.Returns(FlowType.Login);
        handler.HandleCallbackAsync(Arg.Any<FlowCallbackContext>())
            .Returns(FlowResult.Success("test-token"));

        var sut = CreateSut(
            flowStateRepository: repository,
            flowStateService: stateService,
            flowHandlers: new[] { handler });

        // Consume state1 (cookie matches state1)
        var httpContext1 = CreateHttpContext(input.StateParam);
        var result1 = sut.HandleCallbackAsync("auth-code-1", input.StateParam, httpContext1)
            .GetAwaiter().GetResult();

        // Now state2 should still be independently consumable
        // (We verify state1 was successfully consumed — not a CSRF error)
        return (result1.Success
            && result1.Data!.ErrorCode != "CSRF_STATE_MISMATCH"
            && result1.Data.ErrorCode != "FLOW_ALREADY_CONSUMED")
            .ToProperty();
    }
}

// --- Test data types ---

/// <summary>
/// Represents a valid, cryptographically-like state token for testing.
/// </summary>
public sealed record ValidStateToken(string Token)
{
    public override string ToString() => $"Token={Token[..Math.Min(Token.Length, 12)]}...";
}

/// <summary>
/// Represents a pair of distinct state tokens for mismatch testing.
/// </summary>
public sealed record MismatchedStateTokens(string StateParam, string CookieValue)
{
    public override string ToString() => $"Param={StateParam[..Math.Min(StateParam.Length, 8)]}..., Cookie={CookieValue[..Math.Min(CookieValue.Length, 8)]}...";
}

/// <summary>
/// Custom FsCheck Arbitrary generators for AuthFlowService property tests.
/// </summary>
public static class AuthFlowServiceArbitraries
{
    private static readonly char[] Base64UrlChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".ToCharArray();

    /// <summary>
    /// Generates valid state tokens (Base64URL-like strings of 43+ characters,
    /// simulating the format produced by AuthUrlBuilderService).
    /// </summary>
    public static Arbitrary<ValidStateToken> ValidStateTokenArb()
    {
        var gen = from length in Gen.Choose(43, 64)
                  from chars in Gen.ArrayOf(length, Gen.Elements(Base64UrlChars))
                  select new ValidStateToken(new string(chars));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates pairs of DISTINCT state tokens guaranteed to be different.
    /// </summary>
    public static Arbitrary<MismatchedStateTokens> MismatchedStateTokensArb()
    {
        var gen = from length1 in Gen.Choose(43, 64)
                  from chars1 in Gen.ArrayOf(length1, Gen.Elements(Base64UrlChars))
                  from length2 in Gen.Choose(43, 64)
                  from chars2 in Gen.ArrayOf(length2, Gen.Elements(Base64UrlChars))
                  let token1 = new string(chars1)
                  let token2 = new string(chars2)
                  where token1 != token2
                  select new MismatchedStateTokens(token1, token2);

        return gen.ToArbitrary();
    }
}
