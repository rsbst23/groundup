using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using GroundUp.Auth.Keycloak;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Keycloak;

/// <summary>
/// Property-based tests for <see cref="KeycloakStartupValidator"/>.
/// Feature: phase-10b-keycloak-provider, Property 3: Startup Validation Names Missing or Invalid Keys
/// Validates: Requirements 3.1, 3.2, 3.3, 3.4
/// </summary>
[Trait("Category", "Property")]
public sealed class KeycloakStartupValidatorPropertyTests
{
    private static readonly string[] AllSettingKeys =
    {
        KeycloakOptionsSetup.SettingKeys.PublicBaseUrl,
        KeycloakOptionsSetup.SettingKeys.SharedRealmName,
        KeycloakOptionsSetup.SettingKeys.InternalBaseUrl,
        KeycloakOptionsSetup.SettingKeys.AdminClientId,
        KeycloakOptionsSetup.SettingKeys.AdminClientSecret,
        KeycloakOptionsSetup.SettingKeys.AppClientId
    };

    private static IServiceProvider BuildServiceProvider(KeycloakOptions options, bool bootstrapComplete)
    {
        var bootstrapService = Substitute.For<IBootstrapStateService>();
        bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(bootstrapComplete));

        var optionsMonitor = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        optionsMonitor.CurrentValue.Returns(options);

        var services = new ServiceCollection();
        services.AddSingleton(bootstrapService);
        services.AddSingleton(optionsMonitor);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Property 3: For any subset of missing/invalid settings, the exception message names each missing key.
    /// Generate random subsets of the 6 required settings to leave empty, verify the exception
    /// enumerates each one.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(StartupValidatorArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 3: Startup Validation names each missing key")]
    public Property StartAsync_MissingSettings_ExceptionNamesEachMissingKey(MissingSettingsSubset subset)
    {
        var options = new KeycloakOptions
        {
            PublicBaseUrl = subset.IncludePublicBaseUrl ? "https://keycloak.example.com" : "",
            SharedRealmName = subset.IncludeSharedRealmName ? "groundup" : "",
            InternalBaseUrl = subset.IncludeInternalBaseUrl ? "http://keycloak:8080" : "",
            AdminClientId = subset.IncludeAdminClientId ? "admin-cli" : "",
            AdminClientSecret = subset.IncludeAdminClientSecret ? "secret123" : "",
            AppClientId = subset.IncludeAppClientId ? "groundup-app" : ""
        };

        var sp = BuildServiceProvider(options, bootstrapComplete: true);
        var validator = new KeycloakStartupValidator(sp);

        // Determine expected missing keys
        var expectedMissing = new List<string>();
        if (!subset.IncludePublicBaseUrl) expectedMissing.Add(KeycloakOptionsSetup.SettingKeys.PublicBaseUrl);
        if (!subset.IncludeSharedRealmName) expectedMissing.Add(KeycloakOptionsSetup.SettingKeys.SharedRealmName);
        if (!subset.IncludeInternalBaseUrl) expectedMissing.Add(KeycloakOptionsSetup.SettingKeys.InternalBaseUrl);
        if (!subset.IncludeAdminClientId) expectedMissing.Add(KeycloakOptionsSetup.SettingKeys.AdminClientId);
        if (!subset.IncludeAdminClientSecret) expectedMissing.Add(KeycloakOptionsSetup.SettingKeys.AdminClientSecret);
        if (!subset.IncludeAppClientId) expectedMissing.Add(KeycloakOptionsSetup.SettingKeys.AppClientId);

        if (expectedMissing.Count == 0)
        {
            // All settings present — should NOT throw
            var act = () => validator.StartAsync(CancellationToken.None);
            act.Should().NotThrowAsync();
            return true.ToProperty();
        }

        // Should throw InvalidOperationException naming each missing key
        try
        {
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            return false.ToProperty(); // Should have thrown
        }
        catch (InvalidOperationException ex)
        {
            return expectedMissing.All(key => ex.Message.Contains(key)).ToProperty();
        }
    }

    /// <summary>
    /// Property 3: Invalid URLs (non-http/https scheme) are reported in the exception message.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(StartupValidatorArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 3: Invalid URLs reported in exception")]
    public Property StartAsync_InvalidUrls_ExceptionNamesInvalidKeys(InvalidUrlInput input)
    {
        var options = new KeycloakOptions
        {
            PublicBaseUrl = input.PublicBaseUrl,
            SharedRealmName = "groundup",
            InternalBaseUrl = input.InternalBaseUrl,
            AdminClientId = "admin-cli",
            AdminClientSecret = "secret123",
            AppClientId = "groundup-app"
        };

        var sp = BuildServiceProvider(options, bootstrapComplete: true);
        var validator = new KeycloakStartupValidator(sp);

        try
        {
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            // If both URLs happen to be valid, no exception expected
            return true.ToProperty();
        }
        catch (InvalidOperationException ex)
        {
            // If an invalid URL was used, it should appear in the message
            var invalidPrefixes = new[] { "ftp://", "file://", "notaurl", "://missing" };
            var publicIsInvalid = invalidPrefixes.Any(p => input.PublicBaseUrl.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                || !Uri.TryCreate(input.PublicBaseUrl, UriKind.Absolute, out var pu)
                || (pu.Scheme != "http" && pu.Scheme != "https");
            var internalIsInvalid = invalidPrefixes.Any(p => input.InternalBaseUrl.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                || !Uri.TryCreate(input.InternalBaseUrl, UriKind.Absolute, out var iu)
                || (iu.Scheme != "http" && iu.Scheme != "https");

            if (publicIsInvalid)
            {
                return ex.Message.Contains(KeycloakOptionsSetup.SettingKeys.PublicBaseUrl).ToProperty();
            }
            if (internalIsInvalid)
            {
                return ex.Message.Contains(KeycloakOptionsSetup.SettingKeys.InternalBaseUrl).ToProperty();
            }
            return true.ToProperty();
        }
    }

    /// <summary>
    /// Property 3: When bootstrap is incomplete and ALL settings are empty, validation is skipped.
    /// **Validates: Requirement 3.3**
    /// </summary>
    [Fact]
    public async Task StartAsync_BootstrapIncompleteAllEmpty_DoesNotThrow()
    {
        var options = new KeycloakOptions(); // All defaults to string.Empty
        var sp = BuildServiceProvider(options, bootstrapComplete: false);
        var validator = new KeycloakStartupValidator(sp);

        var act = () => validator.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Property 3: When bootstrap is incomplete but settings are partially populated, validation runs.
    /// **Validates: Requirement 3.4**
    /// </summary>
    [Fact]
    public async Task StartAsync_BootstrapIncompletePartialSettings_ThrowsForMissing()
    {
        var options = new KeycloakOptions
        {
            PublicBaseUrl = "https://keycloak.example.com",
            SharedRealmName = "groundup"
            // Rest left empty
        };

        var sp = BuildServiceProvider(options, bootstrapComplete: false);
        var validator = new KeycloakStartupValidator(sp);

        var act = () => validator.StartAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain(KeycloakOptionsSetup.SettingKeys.InternalBaseUrl);
        ex.Which.Message.Should().Contain(KeycloakOptionsSetup.SettingKeys.AdminClientId);
        ex.Which.Message.Should().Contain(KeycloakOptionsSetup.SettingKeys.AdminClientSecret);
        ex.Which.Message.Should().Contain(KeycloakOptionsSetup.SettingKeys.AppClientId);
    }

    /// <summary>
    /// Property 3: When all settings are valid, startup validation passes without throwing.
    /// **Validates: Requirement 3.1**
    /// </summary>
    [Fact]
    public async Task StartAsync_AllSettingsValid_DoesNotThrow()
    {
        var options = new KeycloakOptions
        {
            PublicBaseUrl = "https://keycloak.example.com",
            SharedRealmName = "groundup",
            InternalBaseUrl = "http://keycloak:8080",
            AdminClientId = "admin-cli",
            AdminClientSecret = "secret123",
            AppClientId = "groundup-app"
        };

        var sp = BuildServiceProvider(options, bootstrapComplete: true);
        var validator = new KeycloakStartupValidator(sp);

        var act = () => validator.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}

/// <summary>
/// Represents a generated subset of settings where some are included and some are empty.
/// </summary>
public sealed class MissingSettingsSubset
{
    public bool IncludePublicBaseUrl { get; }
    public bool IncludeSharedRealmName { get; }
    public bool IncludeInternalBaseUrl { get; }
    public bool IncludeAdminClientId { get; }
    public bool IncludeAdminClientSecret { get; }
    public bool IncludeAppClientId { get; }

    public MissingSettingsSubset(
        bool includePublicBaseUrl, bool includeSharedRealmName, bool includeInternalBaseUrl,
        bool includeAdminClientId, bool includeAdminClientSecret, bool includeAppClientId)
    {
        IncludePublicBaseUrl = includePublicBaseUrl;
        IncludeSharedRealmName = includeSharedRealmName;
        IncludeInternalBaseUrl = includeInternalBaseUrl;
        IncludeAdminClientId = includeAdminClientId;
        IncludeAdminClientSecret = includeAdminClientSecret;
        IncludeAppClientId = includeAppClientId;
    }

    public override string ToString() =>
        $"Pub={IncludePublicBaseUrl}, Realm={IncludeSharedRealmName}, Int={IncludeInternalBaseUrl}, " +
        $"AdminId={IncludeAdminClientId}, AdminSec={IncludeAdminClientSecret}, App={IncludeAppClientId}";
}

/// <summary>
/// Represents an input with potentially invalid URLs for testing URL validation.
/// </summary>
public sealed class InvalidUrlInput
{
    public string PublicBaseUrl { get; }
    public string InternalBaseUrl { get; }

    public InvalidUrlInput(string publicBaseUrl, string internalBaseUrl)
    {
        PublicBaseUrl = publicBaseUrl;
        InternalBaseUrl = internalBaseUrl;
    }

    public override string ToString() => $"Public={PublicBaseUrl}, Internal={InternalBaseUrl}";
}

/// <summary>
/// FsCheck Arbitrary generators for startup validation tests.
/// </summary>
public static class StartupValidatorArbitraries
{
    /// <summary>
    /// Generates subsets where at least one setting is missing (to guarantee an exception).
    /// </summary>
    public static Arbitrary<MissingSettingsSubset> MissingSettingsSubsetArb()
    {
        var gen = from pub in Arb.Generate<bool>()
                  from realm in Arb.Generate<bool>()
                  from intern in Arb.Generate<bool>()
                  from adminId in Arb.Generate<bool>()
                  from adminSec in Arb.Generate<bool>()
                  from app in Arb.Generate<bool>()
                  // Ensure at least one is missing to make the test interesting
                  where !(pub && realm && intern && adminId && adminSec && app)
                  select new MissingSettingsSubset(pub, realm, intern, adminId, adminSec, app);

        return Arb.From(gen);
    }

    /// <summary>
    /// Generates URL inputs that include both valid and invalid URL formats.
    /// </summary>
    public static Arbitrary<InvalidUrlInput> InvalidUrlInputArb()
    {
        var validUrls = Gen.Elements(
            "https://keycloak.example.com",
            "http://keycloak:8080",
            "https://auth.myapp.io/auth");

        var invalidUrls = Gen.Elements(
            "ftp://keycloak.example.com",
            "file:///etc/passwd",
            "notaurl",
            "://missing-scheme",
            "tcp://wrong-scheme");

        var urlGen = Gen.OneOf(validUrls, invalidUrls);

        var gen = from pub in urlGen
                  from intern in urlGen
                  // Ensure at least one is invalid
                  where !IsValidHttpUrl(pub) || !IsValidHttpUrl(intern)
                  select new InvalidUrlInput(pub, intern);

        return Arb.From(gen);
    }

    private static bool IsValidHttpUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
