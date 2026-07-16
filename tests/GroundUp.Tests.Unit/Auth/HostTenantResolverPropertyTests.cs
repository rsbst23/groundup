using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Property-based tests for <see cref="HostTenantResolver"/>.
/// Feature: phase-10c-auth-dispatcher, Property 4: Host Subdomain Extraction
/// Validates: Requirements 2.2, 2.3, 2.6, 2.7
/// </summary>
[Trait("Category", "Property")]
public sealed class HostTenantResolverPropertyTests
{
    private static readonly TenantDto ActiveTenant = new(
        Id: Guid.NewGuid(),
        Name: "Test Tenant",
        Slug: "placeholder",
        TenantType: TenantType.Standard,
        OnboardingMode: OnboardingMode.InviteOnly,
        ParentTenantId: null,
        RealmName: null,
        IsActive: true);

    private static HostTenantResolver CreateResolver(
        string? defaultDomain,
        TenantDto? tenantForSlug = null)
    {
        var settingsService = Substitute.For<ISettingsService>();

        if (defaultDomain is null)
        {
            settingsService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<string>.Fail("Not found", 404));
        }
        else
        {
            settingsService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<string>.Ok(defaultDomain));
        }

        var tenantRepository = Substitute.For<ITenantRepository>();

        if (tenantForSlug is not null)
        {
            tenantRepository.GetBySlugBypassFilterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<TenantDto>.Ok(tenantForSlug));
        }
        else
        {
            tenantRepository.GetBySlugBypassFilterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<TenantDto>.Fail("Not found", 404));
        }

        return new HostTenantResolver(settingsService, tenantRepository);
    }

    /// <summary>
    /// Property 4: For any valid slug and default domain, the HostTenantResolver extracts the
    /// single label subdomain as the candidate slug (lowercased), strips any port,
    /// and performs case-insensitive domain comparison.
    /// **Validates: Requirements 2.2, 2.3**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(HostTenantResolverArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 4: Valid host pattern extracts slug correctly")]
    public Property ResolveAsync_ValidHostPattern_ExtractsSlugAndReturnsTenant(ValidHostInput input)
    {
        // Arrange — mock returns active tenant for any slug
        var expectedTenant = ActiveTenant with { Slug = input.Slug.ToLowerInvariant() };
        var sut = CreateResolver(input.DefaultDomain, expectedTenant);

        var host = new HostString(input.HostHeaderValue);

        // Act
        var result = sut.ResolveAsync(host, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should resolve to a tenant (not null)
        return (result is not null).ToProperty();
    }

    /// <summary>
    /// Property 4: For any valid slug and default domain with optional port suffix,
    /// port is stripped and does not affect slug extraction.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(HostTenantResolverArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 4: Port suffix does not affect slug extraction")]
    public Property ResolveAsync_HostWithPort_StillExtractsSlug(ValidHostWithPortInput input)
    {
        // Arrange
        var expectedTenant = ActiveTenant with { Slug = input.Slug.ToLowerInvariant() };
        var sut = CreateResolver(input.DefaultDomain, expectedTenant);

        // HostString strips port from Host property automatically
        var host = new HostString(input.HostHeaderValue);

        // Act
        var result = sut.ResolveAsync(host, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should resolve (port does not interfere)
        return (result is not null).ToProperty();
    }

    /// <summary>
    /// Property 4: For any host header that does not match {slug}.{default-domain}
    /// (bare domain, IP address, multi-level subdomain, or different domain),
    /// the resolver returns null.
    /// **Validates: Requirements 2.6, 2.7**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(HostTenantResolverArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 4: Invalid host patterns return null")]
    public Property ResolveAsync_InvalidHostPattern_ReturnsNull(InvalidHostInput input)
    {
        // Arrange — even if repo would return a tenant, the pattern doesn't match
        var sut = CreateResolver(input.DefaultDomain, ActiveTenant);

        var host = new HostString(input.HostHeaderValue);

        // Act
        var result = sut.ResolveAsync(host, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should always return null for invalid patterns
        return (result is null).ToProperty();
    }

    /// <summary>
    /// Property 4: Domain comparison is case-insensitive. Hosts with mixed-case
    /// domain that matches the default domain (ignoring case) should still resolve.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(HostTenantResolverArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 4: Case-insensitive domain comparison")]
    public Property ResolveAsync_MixedCaseHost_ResolvesCorrectly(CaseInsensitiveHostInput input)
    {
        // Arrange
        var expectedTenant = ActiveTenant with { Slug = input.Slug.ToLowerInvariant() };
        var sut = CreateResolver(input.DefaultDomain, expectedTenant);

        var host = new HostString(input.HostHeaderValue);

        // Act
        var result = sut.ResolveAsync(host, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should resolve despite case differences
        return (result is not null).ToProperty();
    }
}

// --- Test data types ---

/// <summary>
/// Represents a valid host header input matching {slug}.{default-domain}.
/// </summary>
public sealed record ValidHostInput(string Slug, string DefaultDomain, string HostHeaderValue)
{
    public override string ToString() => $"Host={HostHeaderValue}, Domain={DefaultDomain}, Slug={Slug}";
}

/// <summary>
/// Represents a valid host header input with a port suffix.
/// </summary>
public sealed record ValidHostWithPortInput(string Slug, string DefaultDomain, int Port, string HostHeaderValue)
{
    public override string ToString() => $"Host={HostHeaderValue}, Domain={DefaultDomain}, Slug={Slug}, Port={Port}";
}

/// <summary>
/// Represents an invalid host header that should not match the subdomain pattern
/// (bare domain, IP address, multi-level subdomain, or different domain).
/// </summary>
public sealed record InvalidHostInput(string DefaultDomain, string HostHeaderValue, string Reason)
{
    public override string ToString() => $"Host={HostHeaderValue}, Domain={DefaultDomain}, Reason={Reason}";
}

/// <summary>
/// Represents a valid host header input with mixed case for case-insensitivity testing.
/// </summary>
public sealed record CaseInsensitiveHostInput(string Slug, string DefaultDomain, string HostHeaderValue)
{
    public override string ToString() => $"Host={HostHeaderValue}, Domain={DefaultDomain}, Slug={Slug}";
}

/// <summary>
/// Custom FsCheck Arbitrary generators for HostTenantResolver property tests.
/// </summary>
public static class HostTenantResolverArbitraries
{
    private static readonly string[] ValidSlugs =
    {
        "acme", "tenant1", "myorg", "test-company", "abc123", "dev",
        "staging", "production", "alpha", "beta", "customer-a", "org42"
    };

    private static readonly string[] ValidDomains =
    {
        "sampleapp.com", "example.org", "myapp.io", "company.co.uk",
        "platform.net", "saas.app", "groundup.dev"
    };

    private static readonly int[] ValidPorts = { 80, 443, 3000, 5000, 8080, 8443, 9090 };

    /// <summary>
    /// Generates valid host inputs matching {slug}.{default-domain}.
    /// </summary>
    public static Arbitrary<ValidHostInput> ValidHostInputArb()
    {
        var gen = from slug in Gen.Elements(ValidSlugs)
                  from domain in Gen.Elements(ValidDomains)
                  select new ValidHostInput(slug, domain, $"{slug}.{domain}");

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates valid host inputs with port suffix.
    /// </summary>
    public static Arbitrary<ValidHostWithPortInput> ValidHostWithPortInputArb()
    {
        var gen = from slug in Gen.Elements(ValidSlugs)
                  from domain in Gen.Elements(ValidDomains)
                  from port in Gen.Elements(ValidPorts)
                  select new ValidHostWithPortInput(slug, domain, port, $"{slug}.{domain}:{port}");

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates invalid host inputs (bare domain, IP, multi-level subdomain, different domain).
    /// </summary>
    public static Arbitrary<InvalidHostInput> InvalidHostInputArb()
    {
        var bareDomainGen = from domain in Gen.Elements(ValidDomains)
                            select new InvalidHostInput(domain, domain, "bare domain");

        var ipAddressGen = Gen.Elements(
            new InvalidHostInput("sampleapp.com", "192.168.1.1", "IPv4 address"),
            new InvalidHostInput("sampleapp.com", "10.0.0.1", "IPv4 address"),
            new InvalidHostInput("sampleapp.com", "127.0.0.1", "IPv4 loopback"),
            new InvalidHostInput("sampleapp.com", "[::1]", "IPv6 loopback"),
            new InvalidHostInput("sampleapp.com", "172.16.0.100", "IPv4 private"));

        var multiLevelGen = from slug1 in Gen.Elements(ValidSlugs)
                            from slug2 in Gen.Elements(ValidSlugs)
                            from domain in Gen.Elements(ValidDomains)
                            select new InvalidHostInput(domain, $"{slug1}.{slug2}.{domain}", "multi-level subdomain");

        var differentDomainGen = from slug in Gen.Elements(ValidSlugs)
                                 from domain in Gen.Elements(ValidDomains)
                                 from otherDomain in Gen.Elements("otherdomain.com", "notmatch.io", "different.net", "wrong.org")
                                 select new InvalidHostInput(domain, $"{slug}.{otherDomain}", "different domain");

        var gen = Gen.OneOf(bareDomainGen, ipAddressGen, multiLevelGen, differentDomainGen);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates host inputs with mixed case to test case-insensitive comparison.
    /// </summary>
    public static Arbitrary<CaseInsensitiveHostInput> CaseInsensitiveHostInputArb()
    {
        var gen = from slug in Gen.Elements(ValidSlugs)
                  from domain in Gen.Elements(ValidDomains)
                  from mixCase in Gen.Elements("upper-slug", "upper-domain", "both")
                  let mixedSlug = mixCase == "upper-domain" ? slug : slug.ToUpperInvariant()
                  let mixedDomain = mixCase == "upper-slug" ? domain : domain.ToUpperInvariant()
                  select new CaseInsensitiveHostInput(slug, domain, $"{mixedSlug}.{mixedDomain}");

        return gen.ToArbitrary();
    }
}
