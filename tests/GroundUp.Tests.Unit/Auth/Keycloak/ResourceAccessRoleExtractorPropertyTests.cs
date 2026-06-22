using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using GroundUp.Auth.Keycloak;

namespace GroundUp.Tests.Unit.Auth.Keycloak;

/// <summary>
/// Property-based tests for <see cref="ResourceAccessRoleExtractor"/>.
/// Feature: phase-10b-keycloak-provider
/// </summary>
[Trait("Category", "Property")]
public sealed class ResourceAccessRoleExtractorPropertyTests
{
    /// <summary>
    /// Feature: phase-10b-keycloak-provider, Property 15: Role Extraction From resource_access (Happy Path)
    /// For any JWT containing a resource_access claim with a valid JSON structure
    /// { "{clientId}": { "roles": [...] } }, the role extractor SHALL return exactly the strings
    /// in the roles array for the specified client ID.
    /// **Validates: Requirements 12.1, 12.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ResourceAccessArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 15: Role Extraction From resource_access (Happy Path)")]
    public Property ExtractRoles_ValidResourceAccess_ReturnsExactRoles(ValidResourceAccessToken testCase)
    {
        // Act
        var result = ResourceAccessRoleExtractor.ExtractRoles(testCase.Token, testCase.ClientId);

        // Assert
        return (result.Count == testCase.ExpectedRoles.Count
            && result.SequenceEqual(testCase.ExpectedRoles))
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10b-keycloak-provider, Property 15 (Claims variant):
    /// ExtractRolesFromClaims returns exactly the roles for the specified client ID
    /// when given a valid resource_access claim.
    /// **Validates: Requirements 12.1, 12.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ResourceAccessArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 15: Role Extraction From Claims (Happy Path)")]
    public Property ExtractRolesFromClaims_ValidResourceAccess_ReturnsExactRoles(ValidResourceAccessClaims testCase)
    {
        // Act
        var result = ResourceAccessRoleExtractor.ExtractRolesFromClaims(testCase.Claims, testCase.ClientId);

        // Assert
        return (result.Count == testCase.ExpectedRoles.Count
            && result.SequenceEqual(testCase.ExpectedRoles))
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10b-keycloak-provider, Property 16: Role Extraction Returns Empty for Missing or Malformed Claims
    /// For any JWT where resource_access is absent, does not contain the specified client ID,
    /// or has a malformed structure, the role extractor SHALL return an empty list (not null).
    /// **Validates: Requirements 12.3, 12.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ResourceAccessArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 16: Role Extraction Returns Empty for Missing or Malformed Claims")]
    public Property ExtractRoles_MissingOrMalformedResourceAccess_ReturnsEmptyList(MalformedResourceAccessToken testCase)
    {
        // Act
        var result = ResourceAccessRoleExtractor.ExtractRoles(testCase.Token, testCase.ClientId);

        // Assert
        return (result != null && result.Count == 0).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10b-keycloak-provider, Property 16 (Claims variant):
    /// ExtractRolesFromClaims returns an empty list for missing or malformed resource_access claims.
    /// **Validates: Requirements 12.3, 12.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ResourceAccessArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 16: Role Extraction From Claims Returns Empty for Missing or Malformed")]
    public Property ExtractRolesFromClaims_MissingOrMalformed_ReturnsEmptyList(MalformedResourceAccessClaims testCase)
    {
        // Act
        var result = ResourceAccessRoleExtractor.ExtractRolesFromClaims(testCase.Claims, testCase.ClientId);

        // Assert
        return (result != null && result.Count == 0).ToProperty();
    }

    /// <summary>
    /// Property 16 edge case: null/empty/whitespace token or clientId always returns empty list.
    /// **Validates: Requirements 12.3, 12.5**
    /// </summary>
    [Theory]
    [InlineData(null, "my-client")]
    [InlineData("", "my-client")]
    [InlineData("   ", "my-client")]
    [InlineData("header.payload.sig", null)]
    [InlineData("header.payload.sig", "")]
    [InlineData("header.payload.sig", "   ")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void ExtractRoles_NullOrEmptyInputs_ReturnsEmptyList(string? token, string? clientId)
    {
        var result = ResourceAccessRoleExtractor.ExtractRoles(token!, clientId!);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Property 16 edge case: null claims or null/empty clientId always returns empty list.
    /// **Validates: Requirements 12.3, 12.5**
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("valid-client")]
    public void ExtractRolesFromClaims_NullOrEmptyClaims_ReturnsEmptyList(string? clientId)
    {
        // Test with null claims
        var result1 = ResourceAccessRoleExtractor.ExtractRolesFromClaims(null!, clientId!);

        // Test with empty claims
        var result2 = ResourceAccessRoleExtractor.ExtractRolesFromClaims(
            Array.Empty<Claim>(), clientId!);

        result1.Should().NotBeNull();
        result1.Should().BeEmpty();
        result2.Should().NotBeNull();
        result2.Should().BeEmpty();
    }
}
