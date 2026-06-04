using FsCheck;
using FsCheck.Xunit;
using GroundUp.Api.Middleware;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Api.Middleware;

/// <summary>
/// Property-based tests for <see cref="BootstrapModeMiddleware"/>.
/// Validates the path partition property from the Phase 10AB design document.
///
/// **Property 8: Bootstrap Middleware Path Partition** —
/// Allowed paths pass through; non-allowed paths get 503/302; all paths pass when complete.
///
/// **Validates: Requirements 7.2, 7.3, 7.4, 7.6**
/// </summary>
public sealed class BootstrapMiddlewarePropertyTests
{
    private static readonly string[] AllowedPrefixes =
    [
        "/setup",
        "/_framework",
        "/css",
        "/js",
        "/images",
        "/lib"
    ];

    private static readonly string[] AllowedExactPaths =
    [
        "/health",
        "/ready"
    ];

    /// <summary>
    /// Property 8a: When IsComplete=true, any path passes through unchanged.
    /// For any arbitrary path, the middleware invokes next when setup is complete.
    /// **Validates: Requirements 7.2**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property InvokeAsync_SetupComplete_AnyPath_PassesThrough(NonEmptyString pathSuffix)
    {
        var path = "/" + pathSuffix.Get.TrimStart('/');

        Func<bool> property = () =>
        {
            var (middleware, context, nextCalled) = CreateTestHarness(path, isComplete: true);
            middleware.InvokeAsync(context).GetAwaiter().GetResult();
            return nextCalled();
        };

        return property.When(IsValidPathSegment(pathSuffix.Get));
    }

    /// <summary>
    /// Property 8b: When IsComplete=false and path matches an allowed prefix (segment-aware),
    /// the middleware passes through unchanged.
    /// **Validates: Requirements 7.3**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(AllowedPathArbitrary) })]
    public Property InvokeAsync_SetupIncomplete_AllowedPath_PassesThrough(AllowedPath allowedPath)
    {
        Func<bool> property = () =>
        {
            var (middleware, context, nextCalled) = CreateTestHarness(allowedPath.Value, isComplete: false);
            middleware.InvokeAsync(context).GetAwaiter().GetResult();
            return nextCalled();
        };

        return property.ToProperty();
    }

    /// <summary>
    /// Property 8c: When IsComplete=false and path does NOT match any allowed path,
    /// and Accept header contains application/json, the middleware returns 503.
    /// **Validates: Requirements 7.4, 7.6**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(NonAllowedPathArbitrary) })]
    public Property InvokeAsync_SetupIncomplete_NonAllowedPath_JsonClient_Returns503(NonAllowedPath nonAllowedPath)
    {
        Func<bool> property = () =>
        {
            var (middleware, context, nextCalled) = CreateTestHarness(
                nonAllowedPath.Value, isComplete: false, acceptHeader: "application/json");
            middleware.InvokeAsync(context).GetAwaiter().GetResult();

            return !nextCalled()
                && context.Response.StatusCode == 503;
        };

        return property.ToProperty();
    }

    /// <summary>
    /// Property 8d: When IsComplete=false and path does NOT match any allowed path,
    /// and Accept header does NOT contain application/json, the middleware returns 302 redirect to /setup.
    /// **Validates: Requirements 7.4, 7.6**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(NonAllowedPathArbitrary) })]
    public Property InvokeAsync_SetupIncomplete_NonAllowedPath_NonJsonClient_Returns302(NonAllowedPath nonAllowedPath)
    {
        Func<bool> property = () =>
        {
            var (middleware, context, nextCalled) = CreateTestHarness(
                nonAllowedPath.Value, isComplete: false, acceptHeader: "text/html");
            middleware.InvokeAsync(context).GetAwaiter().GetResult();

            return !nextCalled()
                && context.Response.StatusCode == 302
                && context.Response.Headers.Location.ToString() == "/setup";
        };

        return property.ToProperty();
    }

    /// <summary>
    /// Property 8e: Segment-aware matching prevents prefix-extension attacks.
    /// A path like "/setup-evil" or "/css-hack" must NOT match the allowed prefixes.
    /// **Validates: Requirements 7.3**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(PrefixExtensionAttackPathArbitrary) })]
    public Property InvokeAsync_SetupIncomplete_PrefixExtensionPath_DoesNotPassThrough(PrefixExtensionAttackPath attackPath)
    {
        Func<bool> property = () =>
        {
            var (middleware, context, nextCalled) = CreateTestHarness(
                attackPath.Value, isComplete: false, acceptHeader: "text/html");
            middleware.InvokeAsync(context).GetAwaiter().GetResult();

            // Should NOT pass through — should redirect
            return !nextCalled()
                && context.Response.StatusCode == 302;
        };

        return property.ToProperty();
    }

    #region Test Infrastructure

    private static (BootstrapModeMiddleware middleware, DefaultHttpContext context, Func<bool> nextCalled)
        CreateTestHarness(string path, bool isComplete, string? acceptHeader = null)
    {
        var bootstrapService = Substitute.For<IBootstrapStateService>();
        bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(isComplete);

        var services = new ServiceCollection();
        services.AddSingleton(bootstrapService);
        var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (acceptHeader is not null)
        {
            context.Request.Headers.Accept = acceptHeader;
        }

        var nextWasCalled = false;
        RequestDelegate next = _ => { nextWasCalled = true; return Task.CompletedTask; };
        var logger = Substitute.For<ILogger<BootstrapModeMiddleware>>();
        var middleware = new BootstrapModeMiddleware(next, logger);

        return (middleware, context, () => nextWasCalled);
    }

    private static bool IsValidPathSegment(string value)
    {
        // Filter out strings that would produce invalid paths
        return !string.IsNullOrWhiteSpace(value)
            && !value.Contains('\0')
            && !value.Contains('\n')
            && !value.Contains('\r');
    }

    #endregion

    #region Custom Types and Arbitraries

    /// <summary>Wrapper for paths that should be allowed through the middleware.</summary>
    public sealed class AllowedPath
    {
        public string Value { get; }
        public AllowedPath(string value) => Value = value;
        public override string ToString() => Value;
    }

    /// <summary>Wrapper for paths that should NOT be allowed through the middleware.</summary>
    public sealed class NonAllowedPath
    {
        public string Value { get; }
        public NonAllowedPath(string value) => Value = value;
        public override string ToString() => Value;
    }

    /// <summary>Wrapper for paths that extend an allowed prefix with non-segment characters.</summary>
    public sealed class PrefixExtensionAttackPath
    {
        public string Value { get; }
        public PrefixExtensionAttackPath(string value) => Value = value;
        public override string ToString() => Value;
    }

    /// <summary>
    /// Generates paths that match the allowed list (prefix or exact).
    /// </summary>
    public sealed class AllowedPathArbitrary
    {
        public static Arbitrary<AllowedPath> AllowedPath()
        {
            var prefixPaths = Gen.Elements(AllowedPrefixes)
                .SelectMany(prefix => Gen.Elements("", "/sub", "/deep/nested", "/file.js", "/page")
                    .Select(suffix => new AllowedPath(prefix + suffix)));

            var exactPaths = Gen.Elements(AllowedExactPaths)
                .Select(p => new AllowedPath(p));

            // Also test case-insensitive variants
            var caseVariants = Gen.Elements(AllowedPrefixes)
                .SelectMany(prefix => Gen.Elements("", "/sub", "/file.css")
                    .Select(suffix => new AllowedPath(RandomizeCase(prefix) + suffix)));

            return Arb.From(Gen.OneOf(prefixPaths, exactPaths, caseVariants));
        }

        private static string RandomizeCase(string input)
        {
            var chars = input.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (i % 2 == 0)
                    chars[i] = char.ToUpperInvariant(chars[i]);
            }
            return new string(chars);
        }
    }

    /// <summary>
    /// Generates paths that do NOT match any allowed prefix or exact path.
    /// </summary>
    public sealed class NonAllowedPathArbitrary
    {
        public static Arbitrary<NonAllowedPath> NonAllowedPath()
        {
            var apiPaths = Gen.Elements(
                "/api/users",
                "/api/settings",
                "/api/tenants",
                "/admin/dashboard",
                "/account/login",
                "/dashboard",
                "/users/profile",
                "/api/v1/orders",
                "/graphql",
                "/swagger"
            ).Select(p => new NonAllowedPath(p));

            // Generate random paths that don't start with any allowed prefix
            var randomPaths = Gen.Elements(
                "api", "admin", "account", "dashboard", "users", "orders",
                "products", "settings", "auth", "login", "register", "swagger"
            ).SelectMany(segment => Gen.Elements("", "/sub", "/deep/path")
                .Select(suffix => new NonAllowedPath("/" + segment + suffix)));

            return Arb.From(Gen.OneOf(apiPaths, randomPaths));
        }
    }

    /// <summary>
    /// Generates paths that extend an allowed prefix with non-segment characters
    /// (e.g., "/setup-evil", "/css-hack") to test segment-aware matching.
    /// </summary>
    public sealed class PrefixExtensionAttackPathArbitrary
    {
        public static Arbitrary<PrefixExtensionAttackPath> PrefixExtensionAttackPath()
        {
            var suffixes = new[] { "-evil", "-hack", "123", "extra", "_bad", ".malicious" };

            var attackPaths = Gen.Elements(AllowedPrefixes)
                .SelectMany(prefix => Gen.Elements(suffixes)
                    .Select(suffix => new PrefixExtensionAttackPath(prefix + suffix)));

            return Arb.From(attackPaths);
        }
    }

    #endregion
}
