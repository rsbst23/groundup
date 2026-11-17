using System.Security.Claims;
using FluentAssertions;
using GroundUp.Auth.Api;

namespace GroundUp.Tests.Unit.Auth;

public sealed class AuthConstantsTests
{
    private static ClaimsPrincipal PrincipalWith(string? authenticationType, params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType));

    [Fact]
    public void IsExternalIdp_IdentityProviderPrincipal_ReturnsTrue()
    {
        var principal = PrincipalWith(AuthConstants.ExternalIdpAuthenticationType, new Claim("sub", Guid.NewGuid().ToString()));

        principal.IsExternalIdp().Should().BeTrue();
    }

    [Fact]
    public void IsExternalIdp_GroundUpTenantlessPrincipal_ReturnsFalseEvenWithGuidSub()
    {
        // A tenantless GroundUp token has no tid, but its sub is the GroundUp user id.
        var principal = PrincipalWith("AuthenticationTypes.Federation", new Claim("sub", Guid.NewGuid().ToString()));

        principal.IsExternalIdp().Should().BeFalse();
    }

    [Fact]
    public void IsExternalIdp_GroundUpTenantScopedPrincipal_ReturnsFalse()
    {
        var principal = PrincipalWith(
            "AuthenticationTypes.Federation",
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("tid", Guid.NewGuid().ToString()));

        principal.IsExternalIdp().Should().BeFalse();
    }

    [Fact]
    public void IsExternalIdp_UnauthenticatedPrincipal_ReturnsFalse()
    {
        var principal = PrincipalWith(authenticationType: null);

        principal.IsExternalIdp().Should().BeFalse();
    }
}
