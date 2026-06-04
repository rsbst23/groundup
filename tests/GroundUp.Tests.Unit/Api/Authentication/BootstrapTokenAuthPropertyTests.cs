using System.Text.Encodings.Web;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Api.Authentication;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Api.Authentication;

/// <summary>
/// Property-based tests for <see cref="BootstrapAdminTokenAuthenticationHandler"/>.
/// Validates authentication correctness: succeeds iff isComplete=false AND token bytes match.
/// **Validates: Requirements 8.2, 8.3, 8.4, 8.5, 8.8**
/// </summary>
public sealed class BootstrapTokenAuthPropertyTests
{
    private static async Task<AuthenticateResult> AuthenticateAsync(
        string? authorizationHeader,
        bool setupComplete,
        string configuredToken)
    {
        var configData = new Dictionary<string, string?>
        {
            ["GroundUp:BootstrapAdminToken"] = configuredToken
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var bootstrapService = Substitute.For<IBootstrapStateService>();
        bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(setupComplete);

        var services = new ServiceCollection();
        services.AddSingleton<IBootstrapStateService>(bootstrapService);
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        if (authorizationHeader is not null)
        {
            context.Request.Headers.Authorization = authorizationHeader;
        }

        var optionsMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Get(BootstrapAdminTokenAuthenticationHandler.SchemeName)
            .Returns(new AuthenticationSchemeOptions());

        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        var handler = new BootstrapAdminTokenAuthenticationHandler(
            optionsMonitor,
            loggerFactory,
            UrlEncoder.Default,
            configuration);

        var scheme = new AuthenticationScheme(
            BootstrapAdminTokenAuthenticationHandler.SchemeName,
            displayName: null,
            handlerType: typeof(BootstrapAdminTokenAuthenticationHandler));

        await handler.InitializeAsync(scheme, context);

        return await handler.AuthenticateAsync();
    }

    /// <summary>
    /// Property 9a: Authentication succeeds iff isComplete=false AND token bytes match.
    /// For any non-empty configured token and any provided token:
    ///   result.Succeeded == (!isComplete AND provided == configured)
    /// **Validates: Requirements 8.2, 8.3, 8.5, 8.8**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property Authenticate_SucceedsIff_IncompleteAndTokenMatches(
        NonEmptyString configuredToken,
        NonEmptyString providedToken,
        bool isComplete)
    {
        var configured = configuredToken.Get;
        var provided = providedToken.Get;

        Func<bool> property = () =>
        {
            var result = AuthenticateAsync(
                $"Bearer {provided}",
                isComplete,
                configured).GetAwaiter().GetResult();

            var tokensMatch = string.Equals(provided.Trim(), configured, StringComparison.Ordinal);
            var expectedSuccess = !isComplete && tokensMatch;

            return result.Succeeded == expectedSuccess;
        };

        // Filter: configured token must be non-whitespace (handler requires non-empty config)
        return property.When(
            !string.IsNullOrWhiteSpace(configured) &&
            !string.IsNullOrWhiteSpace(provided));
    }

    /// <summary>
    /// Property 9b: Missing or non-Bearer Authorization header always fails authentication.
    /// For any isComplete state and any configured token, if the Authorization header
    /// is missing, empty, or uses a non-Bearer scheme, authentication fails.
    /// **Validates: Requirements 8.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Authenticate_FailsOnMissingOrNonBearerHeader(
        NonEmptyString configuredToken,
        bool isComplete,
        int headerVariant)
    {
        var configured = configuredToken.Get;

        Func<bool> property = () =>
        {
            // Generate different invalid header variants
            var variant = Math.Abs(headerVariant) % 4;
            string? header = variant switch
            {
                0 => null,                    // Missing header
                1 => "",                      // Empty header
                2 => "Basic dXNlcjpwYXNz",   // Wrong scheme
                3 => "   ",                   // Whitespace-only
                _ => null
            };

            var result = AuthenticateAsync(header, isComplete, configured)
                .GetAwaiter().GetResult();

            // When isComplete=true, fails with "Setup is already complete" (checked first)
            // When isComplete=false and header is invalid, fails with "Missing Authorization header"
            return !result.Succeeded;
        };

        return property.When(!string.IsNullOrWhiteSpace(configured));
    }

    /// <summary>
    /// Property 9c: Setup-complete state rejects ALL tokens regardless of correctness.
    /// For any configured token, even when the provided token matches exactly,
    /// authentication fails when isComplete=true.
    /// **Validates: Requirements 8.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Authenticate_SetupComplete_AlwaysFails(NonEmptyString configuredToken)
    {
        var configured = configuredToken.Get;

        Func<bool> property = () =>
        {
            // Provide the exact correct token — should still fail
            var result = AuthenticateAsync(
                $"Bearer {configured}",
                setupComplete: true,
                configured).GetAwaiter().GetResult();

            return !result.Succeeded &&
                   result.Failure!.Message.Contains("Setup is already complete");
        };

        return property.When(!string.IsNullOrWhiteSpace(configured));
    }

    /// <summary>
    /// Property 9d: Successful authentication produces the bootstrap-admin claim.
    /// For any valid token where isComplete=false and tokens match,
    /// the resulting principal has the bootstrap-admin claim with value "true".
    /// **Validates: Requirements 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Authenticate_Success_HasBootstrapAdminClaim(NonEmptyString configuredToken)
    {
        var configured = configuredToken.Get;

        Func<bool> property = () =>
        {
            var result = AuthenticateAsync(
                $"Bearer {configured}",
                setupComplete: false,
                configured).GetAwaiter().GetResult();

            if (!result.Succeeded) return false;

            var claim = result.Principal!.FindFirst(BootstrapAdminTokenAuthenticationHandler.ClaimType);
            return claim is not null && claim.Value == "true";
        };

        // Filter: token must not have leading/trailing whitespace because the handler
        // trims the provided token from the header, so tokens with surrounding whitespace
        // won't byte-match after trim.
        return property.When(
            !string.IsNullOrWhiteSpace(configured) &&
            configured == configured.Trim());
    }

    /// <summary>
    /// Property 9e: Token comparison is byte-exact (no case folding, no trimming of token content).
    /// For any configured token, a case-altered version of the token fails authentication.
    /// This validates constant-time byte comparison behavior (Req 8.8) — the comparison
    /// operates on raw UTF-8 bytes, so case differences cause failure.
    /// **Validates: Requirements 8.5, 8.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Authenticate_TokenComparison_IsCaseSensitive(NonEmptyString configuredToken)
    {
        var configured = configuredToken.Get;

        Func<bool> property = () =>
        {
            // Alter case of the first alphabetic character
            var altered = AlterCase(configured);
            if (altered == configured)
                return true; // No alphabetic chars to alter — skip this case

            var result = AuthenticateAsync(
                $"Bearer {altered}",
                setupComplete: false,
                configured).GetAwaiter().GetResult();

            return !result.Succeeded;
        };

        return property.When(!string.IsNullOrWhiteSpace(configured));
    }

    private static string AlterCase(string input)
    {
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                chars[i] = char.IsUpper(chars[i])
                    ? char.ToLowerInvariant(chars[i])
                    : char.ToUpperInvariant(chars[i]);
                return new string(chars);
            }
        }
        return input; // No letters found
    }
}
