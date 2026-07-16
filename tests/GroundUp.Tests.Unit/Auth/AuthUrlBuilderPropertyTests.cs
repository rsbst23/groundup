using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Property-based tests for <see cref="AuthUrlBuilderService"/>.
/// Feature: phase-10c-auth-dispatcher, Properties 5 and 6.
/// Validates: Requirements 4.2, 4.4, 4.5
/// </summary>
[Trait("Category", "Property")]
public sealed class AuthUrlBuilderPropertyTests
{
    private static readonly Regex Base64UrlRegex = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private static readonly Regex CodeVerifierRegex = new("^[A-Za-z0-9._~-]+$", RegexOptions.Compiled);

    private static AuthUrlBuilderService CreateSut(string publicBaseUrl = "https://keycloak.example.com", string appClientId = "groundup-app", string sharedRealm = "groundup")
    {
        var keycloakOptions = Options.Create(new KeycloakOptions
        {
            PublicBaseUrl = publicBaseUrl,
            AppClientId = appClientId,
            SharedRealmName = sharedRealm
        });

        return new AuthUrlBuilderService(keycloakOptions);
    }

    // --- Property 5: Cryptographic Token Format (State and Nonce) ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 5: Cryptographic Token Format
    /// The generated state token SHALL be a valid Base64URL string without padding characters ('=').
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthUrlBuilderArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 5: State token is valid Base64URL without padding")]
    public Property StateToken_IsValidBase64UrlWithoutPadding(ValidRedirectUri input)
    {
        var sut = CreateSut();
        var request = new AuthUrlRequest(RealmOverride: null, RedirectUri: input.Uri);

        var result = sut.BuildAuthorizationUrlAsync(request).GetAwaiter().GetResult();

        return (result.Success
            && !result.Data!.StateToken.Contains('=')
            && Base64UrlRegex.IsMatch(result.Data.StateToken))
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 5: Cryptographic Token Format
    /// The generated nonce SHALL be a valid Base64URL string without padding characters ('=').
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthUrlBuilderArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 5: Nonce is valid Base64URL without padding")]
    public Property Nonce_IsValidBase64UrlWithoutPadding(ValidRedirectUri input)
    {
        var sut = CreateSut();
        var request = new AuthUrlRequest(RealmOverride: null, RedirectUri: input.Uri);

        var result = sut.BuildAuthorizationUrlAsync(request).GetAwaiter().GetResult();

        return (result.Success
            && !result.Data!.Nonce.Contains('=')
            && Base64UrlRegex.IsMatch(result.Data.Nonce))
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 5: Cryptographic Token Format
    /// When decoded, the state token SHALL contain at least 32 bytes of data.
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthUrlBuilderArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 5: State token decodes to >= 32 bytes")]
    public Property StateToken_DecodesToAtLeast32Bytes(ValidRedirectUri input)
    {
        var sut = CreateSut();
        var request = new AuthUrlRequest(RealmOverride: null, RedirectUri: input.Uri);

        var result = sut.BuildAuthorizationUrlAsync(request).GetAwaiter().GetResult();

        var decoded = Base64UrlDecode(result.Data!.StateToken);

        return (result.Success && decoded.Length >= 32).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 5: Cryptographic Token Format
    /// When decoded, the nonce SHALL contain at least 32 bytes of data.
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthUrlBuilderArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 5: Nonce decodes to >= 32 bytes")]
    public Property Nonce_DecodesToAtLeast32Bytes(ValidRedirectUri input)
    {
        var sut = CreateSut();
        var request = new AuthUrlRequest(RealmOverride: null, RedirectUri: input.Uri);

        var result = sut.BuildAuthorizationUrlAsync(request).GetAwaiter().GetResult();

        var decoded = Base64UrlDecode(result.Data!.Nonce);

        return (result.Success && decoded.Length >= 32).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 5: Cryptographic Token Format
    /// No two sequential invocations SHALL produce the same state token or nonce
    /// (probabilistic — collision probability less than 2^-128).
    /// **Validates: Requirements 4.2, 4.5**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(AuthUrlBuilderArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 5: Sequential invocations produce unique state and nonce")]
    public Property SequentialInvocations_ProduceUniqueStateAndNonce(ValidRedirectUri input)
    {
        var sut = CreateSut();
        var request = new AuthUrlRequest(RealmOverride: null, RedirectUri: input.Uri);

        var result1 = sut.BuildAuthorizationUrlAsync(request).GetAwaiter().GetResult();
        var result2 = sut.BuildAuthorizationUrlAsync(request).GetAwaiter().GetResult();

        return (result1.Success && result2.Success
            && result1.Data!.StateToken != result2.Data!.StateToken
            && result1.Data.Nonce != result2.Data.Nonce)
            .ToProperty();
    }

    // --- Property 6: PKCE Round-Trip Integrity ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 6: PKCE Round-Trip Integrity
    /// The generated code_verifier SHALL be 43–128 characters long using only unreserved
    /// URL-safe characters ([A-Za-z0-9._~-]).
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthUrlBuilderArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 6: Code verifier is 43-128 chars, URL-safe only")]
    public Property CodeVerifier_IsValidLengthAndCharacterSet(ValidRedirectUri input)
    {
        var sut = CreateSut();
        var request = new AuthUrlRequest(RealmOverride: null, RedirectUri: input.Uri);

        var result = sut.BuildAuthorizationUrlAsync(request).GetAwaiter().GetResult();
        var verifier = result.Data!.CodeVerifier;

        return (result.Success
            && verifier.Length >= 43
            && verifier.Length <= 128
            && CodeVerifierRegex.IsMatch(verifier))
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 6: PKCE Round-Trip Integrity
    /// The code_challenge SHALL equal Base64URL(SHA256(code_verifier)) without padding.
    /// Recomputing the challenge from the stored verifier SHALL always produce the same value.
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthUrlBuilderArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 6: code_challenge = Base64URL(SHA256(verifier))")]
    public Property CodeChallenge_MatchesIndependentSha256Computation(ValidRedirectUri input)
    {
        var sut = CreateSut();
        var request = new AuthUrlRequest(RealmOverride: null, RedirectUri: input.Uri);

        var result = sut.BuildAuthorizationUrlAsync(request).GetAwaiter().GetResult();
        var verifier = result.Data!.CodeVerifier;
        var authorizationUrl = result.Data.AuthorizationUrl;

        // Independently compute the expected code_challenge
        var verifierBytes = Encoding.ASCII.GetBytes(verifier);
        var hash = SHA256.HashData(verifierBytes);
        var expectedChallenge = Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        // Extract code_challenge from the authorization URL
        var uri = new Uri(authorizationUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var actualChallenge = query["code_challenge"];

        return (result.Success
            && actualChallenge == expectedChallenge
            && !actualChallenge!.Contains('=')
            && Base64UrlRegex.IsMatch(actualChallenge))
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 6: PKCE Round-Trip Integrity
    /// Recomputing the challenge from the stored verifier SHALL always produce the same
    /// challenge value (deterministic derivation — calling twice yields identical result).
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(AuthUrlBuilderArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 6: PKCE derivation is deterministic")]
    public Property PkceDerivation_IsDeterministic(ValidRedirectUri input)
    {
        var sut = CreateSut();
        var request = new AuthUrlRequest(RealmOverride: null, RedirectUri: input.Uri);

        var result = sut.BuildAuthorizationUrlAsync(request).GetAwaiter().GetResult();
        var verifier = result.Data!.CodeVerifier;

        // Compute challenge twice from the same verifier
        var challenge1 = ComputeCodeChallenge(verifier);
        var challenge2 = ComputeCodeChallenge(verifier);

        // Extract actual challenge from URL
        var uri = new Uri(result.Data.AuthorizationUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var actualChallenge = query["code_challenge"];

        return (challenge1 == challenge2 && challenge1 == actualChallenge).ToProperty();
    }

    // --- Helper methods ---

    private static byte[] Base64UrlDecode(string base64Url)
    {
        var padded = base64Url
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var verifierBytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = SHA256.HashData(verifierBytes);
        return Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

// --- Test data types ---

/// <summary>
/// Represents a valid redirect URI for testing AuthUrlBuilderService.
/// </summary>
public sealed record ValidRedirectUri(string Uri)
{
    public override string ToString() => Uri;
}

/// <summary>
/// Custom FsCheck Arbitrary generators for AuthUrlBuilder property tests.
/// </summary>
public static class AuthUrlBuilderArbitraries
{
    /// <summary>
    /// Generates valid redirect URIs representing realistic OAuth callback URLs.
    /// </summary>
    public static Arbitrary<ValidRedirectUri> ValidRedirectUriArb()
    {
        var gen = Gen.Elements(
            "https://app.example.com/auth/callback",
            "https://myapp.io/auth/callback",
            "https://localhost:5001/auth/callback",
            "https://staging.company.co.uk/auth/callback",
            "https://groundup.dev/auth/callback",
            "https://tenant1.sampleapp.com/auth/callback",
            "http://localhost:3000/auth/callback"
        ).Select(uri => new ValidRedirectUri(uri));

        return gen.ToArbitrary();
    }
}
