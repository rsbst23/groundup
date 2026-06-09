using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Keycloak;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Keycloak;

/// <summary>
/// Property-based tests for <see cref="KeycloakAdminLinkBuilder"/> URL pattern correctness.
/// Feature: phase-10b-keycloak-provider
/// Validates: Requirements 13.2, 13.3, 13.4, 13.5
/// </summary>
[Trait("Category", "Property")]
public sealed class KeycloakAdminLinkBuilderPropertyTests
{
    private static KeycloakAdminLinkBuilder CreateBuilder(string publicBaseUrl, string sharedRealmName)
    {
        var options = new KeycloakOptions
        {
            PublicBaseUrl = publicBaseUrl,
            SharedRealmName = sharedRealmName
        };

        var monitor = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        monitor.CurrentValue.Returns(options);

        return new KeycloakAdminLinkBuilder(monitor);
    }

    /// <summary>
    /// Property 17: Admin Link Builder URL Pattern Correctness — RealmOverview.
    /// For any valid PublicBaseUrl and explicit realm name, RealmOverview returns a URL
    /// matching the pattern {PublicBaseUrl}/admin/{realmName}/console/#/.
    /// **Validates: Requirements 13.2, 13.4, 13.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdminLinkBuilderArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 17: RealmOverview with explicit realm matches expected pattern")]
    public Property RealmOverview_WithExplicitRealm_MatchesExpectedPattern(ValidAdminLinkInput input)
    {
        var builder = CreateBuilder(input.PublicBaseUrl, "shared-realm");
        var result = builder.RealmOverview(input.RealmName);
        var expectedBase = input.PublicBaseUrl.TrimEnd('/');

        return (result == $"{expectedBase}/admin/{input.RealmName}/console/#/")
            .ToProperty();
    }

    /// <summary>
    /// Property 17: Admin Link Builder URL Pattern Correctness — RealmOverview with null realm defaults to SharedRealmName.
    /// **Validates: Requirements 13.3, 13.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdminLinkBuilderArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 17: RealmOverview with null realm uses SharedRealmName")]
    public Property RealmOverview_WithNullRealm_UsesSharedRealmName(ValidAdminLinkInput input)
    {
        var builder = CreateBuilder(input.PublicBaseUrl, input.RealmName);
        var result = builder.RealmOverview(null);
        var expectedBase = input.PublicBaseUrl.TrimEnd('/');

        return (result == $"{expectedBase}/admin/{input.RealmName}/console/#/")
            .ToProperty();
    }

    /// <summary>
    /// Property 17: Admin Link Builder URL Pattern Correctness — ClientList.
    /// For any valid PublicBaseUrl and realm name, ClientList returns a URL
    /// matching the pattern {PublicBaseUrl}/admin/{realmName}/console/#/clients.
    /// **Validates: Requirements 13.2, 13.4, 13.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdminLinkBuilderArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 17: ClientList with explicit realm matches expected pattern")]
    public Property ClientList_WithExplicitRealm_MatchesExpectedPattern(ValidAdminLinkInput input)
    {
        var builder = CreateBuilder(input.PublicBaseUrl, "shared-realm");
        var result = builder.ClientList(input.RealmName);
        var expectedBase = input.PublicBaseUrl.TrimEnd('/');

        return (result == $"{expectedBase}/admin/{input.RealmName}/console/#/clients")
            .ToProperty();
    }

    /// <summary>
    /// Property 17: Admin Link Builder URL Pattern Correctness — ClientDetail.
    /// For any valid PublicBaseUrl, realm name, and client ID, ClientDetail returns a URL
    /// matching the pattern {PublicBaseUrl}/admin/{realmName}/console/#/clients/{clientId}.
    /// **Validates: Requirements 13.2, 13.4, 13.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdminLinkBuilderArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 17: ClientDetail with explicit realm matches expected pattern")]
    public Property ClientDetail_WithExplicitRealm_MatchesExpectedPattern(ValidAdminLinkInput input)
    {
        var clientId = Guid.NewGuid().ToString();
        var builder = CreateBuilder(input.PublicBaseUrl, "shared-realm");
        var result = builder.ClientDetail(clientId, input.RealmName);
        var expectedBase = input.PublicBaseUrl.TrimEnd('/');

        return (result == $"{expectedBase}/admin/{input.RealmName}/console/#/clients/{clientId}")
            .ToProperty();
    }

    /// <summary>
    /// Property 17: Admin Link Builder URL Pattern Correctness — UserList.
    /// For any valid PublicBaseUrl and realm name, UserList returns a URL
    /// matching the pattern {PublicBaseUrl}/admin/{realmName}/console/#/users.
    /// **Validates: Requirements 13.2, 13.4, 13.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdminLinkBuilderArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 17: UserList with explicit realm matches expected pattern")]
    public Property UserList_WithExplicitRealm_MatchesExpectedPattern(ValidAdminLinkInput input)
    {
        var builder = CreateBuilder(input.PublicBaseUrl, "shared-realm");
        var result = builder.UserList(input.RealmName);
        var expectedBase = input.PublicBaseUrl.TrimEnd('/');

        return (result == $"{expectedBase}/admin/{input.RealmName}/console/#/users")
            .ToProperty();
    }

    /// <summary>
    /// Property 17: Admin Link Builder URL Pattern Correctness — UserDetail.
    /// For any valid PublicBaseUrl, realm name, and user ID, UserDetail returns a URL
    /// matching the pattern {PublicBaseUrl}/admin/{realmName}/console/#/users/{userId}.
    /// **Validates: Requirements 13.2, 13.4, 13.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdminLinkBuilderArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 17: UserDetail with explicit realm matches expected pattern")]
    public Property UserDetail_WithExplicitRealm_MatchesExpectedPattern(ValidAdminLinkInput input)
    {
        var userId = Guid.NewGuid().ToString();
        var builder = CreateBuilder(input.PublicBaseUrl, "shared-realm");
        var result = builder.UserDetail(userId, input.RealmName);
        var expectedBase = input.PublicBaseUrl.TrimEnd('/');

        return (result == $"{expectedBase}/admin/{input.RealmName}/console/#/users/{userId}")
            .ToProperty();
    }

    /// <summary>
    /// Property 17: Admin Link Builder URL Pattern Correctness — IdentityProviderConfig.
    /// For any valid PublicBaseUrl and realm name, IdentityProviderConfig returns a URL
    /// matching the pattern {PublicBaseUrl}/admin/{realmName}/console/#/identity-providers.
    /// **Validates: Requirements 13.2, 13.4, 13.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdminLinkBuilderArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 17: IdentityProviderConfig with explicit realm matches expected pattern")]
    public Property IdentityProviderConfig_WithExplicitRealm_MatchesExpectedPattern(ValidAdminLinkInput input)
    {
        var builder = CreateBuilder(input.PublicBaseUrl, "shared-realm");
        var result = builder.IdentityProviderConfig(input.RealmName);
        var expectedBase = input.PublicBaseUrl.TrimEnd('/');

        return (result == $"{expectedBase}/admin/{input.RealmName}/console/#/identity-providers")
            .ToProperty();
    }

    /// <summary>
    /// Property 17: Admin Link Builder URL Pattern Correctness — all methods with null realm default to SharedRealmName.
    /// For any valid PublicBaseUrl and SharedRealmName, all link builder methods with null realm
    /// produce URLs using the SharedRealmName.
    /// **Validates: Requirements 13.3, 13.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdminLinkBuilderArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 17: All methods with null realm use SharedRealmName")]
    public Property AllMethods_WithNullRealm_UseSharedRealmName(ValidAdminLinkInput input)
    {
        var builder = CreateBuilder(input.PublicBaseUrl, input.RealmName);
        var expectedBase = input.PublicBaseUrl.TrimEnd('/');
        var clientId = "test-client";
        var userId = "test-user";

        var realmOverview = builder.RealmOverview(null);
        var clientList = builder.ClientList(null);
        var clientDetail = builder.ClientDetail(clientId, null);
        var userList = builder.UserList(null);
        var userDetail = builder.UserDetail(userId, null);
        var idpConfig = builder.IdentityProviderConfig(null);

        return (realmOverview == $"{expectedBase}/admin/{input.RealmName}/console/#/"
            && clientList == $"{expectedBase}/admin/{input.RealmName}/console/#/clients"
            && clientDetail == $"{expectedBase}/admin/{input.RealmName}/console/#/clients/{clientId}"
            && userList == $"{expectedBase}/admin/{input.RealmName}/console/#/users"
            && userDetail == $"{expectedBase}/admin/{input.RealmName}/console/#/users/{userId}"
            && idpConfig == $"{expectedBase}/admin/{input.RealmName}/console/#/identity-providers")
            .ToProperty();
    }

    /// <summary>
    /// Property 17: Admin Link Builder URL Pattern Correctness — trailing slashes handled correctly.
    /// For any valid PublicBaseUrl with trailing slash, the generated URL should NOT have a double slash
    /// after the scheme portion.
    /// **Validates: Requirements 13.2, 13.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdminLinkBuilderArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 17: Trailing slash handled correctly")]
    public Property TrailingSlash_IsHandledCorrectly(ValidAdminLinkInput input)
    {
        var urlWithTrailingSlash = input.PublicBaseUrl.TrimEnd('/') + "/";
        var builder = CreateBuilder(urlWithTrailingSlash, "shared-realm");
        var result = builder.RealmOverview(input.RealmName);

        // The result should never contain "//" after the scheme
        var afterScheme = result.Substring(result.IndexOf("://") + 3);

        return (!afterScheme.Contains("//"))
            .ToProperty();
    }
}

/// <summary>
/// Wrapper type for generated admin link builder test inputs.
/// </summary>
public sealed class ValidAdminLinkInput
{
    public string PublicBaseUrl { get; }
    public string RealmName { get; }

    public ValidAdminLinkInput(string publicBaseUrl, string realmName)
    {
        PublicBaseUrl = publicBaseUrl;
        RealmName = realmName;
    }

    public override string ToString() => $"BaseUrl={PublicBaseUrl}, Realm={RealmName}";
}

/// <summary>
/// FsCheck Arbitrary class for generating <see cref="ValidAdminLinkInput"/> instances.
/// </summary>
public static class AdminLinkBuilderArbitraries
{
    public static Arbitrary<ValidAdminLinkInput> ValidAdminLinkInputArb()
    {
        var schemes = new[] { "http", "https" };
        var hosts = Gen.Elements("keycloak.example.com", "auth.myapp.io", "localhost:8080", "idp.company.net");
        var paths = Gen.Elements("", "/auth", "/keycloak");
        var trailingSlash = Gen.Elements("", "/");

        var urlGen = from scheme in Gen.Elements(schemes)
                     from host in hosts
                     from path in paths
                     from trailing in trailingSlash
                     select $"{scheme}://{host}{path}{trailing}";

        var realmChars = Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
            'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-');

        var realmGen = from length in Gen.Choose(3, 15)
                       from charList in Gen.ListOf(length, realmChars)
                       let name = new string(charList.ToArray())
                       where !string.IsNullOrWhiteSpace(name) && !name.StartsWith("-") && !name.EndsWith("-")
                       select name;

        var inputGen = from url in urlGen
                       from realm in realmGen
                       select new ValidAdminLinkInput(url, realm);

        return Arb.From(inputGen);
    }
}
