using System.Security.Claims;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Unit.Auth.Identity;

public sealed class JwtTenantContextTests
{
    private readonly AuthOptions _options = new();

    [Fact]
    public void TenantId_NoHttpContext_ReturnsGuidEmpty()
    {
        // Arrange
        var accessor = CreateAccessor(null);
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    [Fact]
    public void TenantId_NoTenantClaim_ReturnsGuidEmpty()
    {
        // Arrange — authenticated user but no tenant_id claim
        var accessor = CreateAccessorWithClaims(
            new Claim("sub", Guid.NewGuid().ToString()));
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    [Fact]
    public void TenantId_ValidClaim_ReturnsCorrectGuid()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var accessor = CreateAccessorWithClaims(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("tenant_id", expectedTenantId.ToString()));
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal(expectedTenantId, sut.TenantId);
    }

    [Fact]
    public void TenantId_InvalidGuidClaim_ReturnsGuidEmpty()
    {
        // Arrange
        var accessor = CreateAccessorWithClaims(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("tenant_id", "not-a-guid"));
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    [Fact]
    public void TenantId_CustomClaimType_ExtractsFromConfiguredType()
    {
        // Arrange
        _options.TenantIdClaimType = "custom_tenant";
        var expectedTenantId = Guid.NewGuid();
        var accessor = CreateAccessorWithClaims(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("custom_tenant", expectedTenantId.ToString()));
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal(expectedTenantId, sut.TenantId);
    }

    [Fact]
    public void TenantId_UnauthenticatedUser_ReturnsGuidEmpty()
    {
        // Arrange — HttpContext exists but user is not authenticated
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        var accessor = CreateAccessor(httpContext);
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    // --- Helpers ---

    private JwtTenantContext CreateSut(IHttpContextAccessor accessor)
    {
        return new JwtTenantContext(accessor, Options.Create(_options));
    }

    private static IHttpContextAccessor CreateAccessor(HttpContext? context)
    {
        var accessor = new HttpContextAccessor { HttpContext = context };
        return accessor;
    }

    private static IHttpContextAccessor CreateAccessorWithClaims(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        return CreateAccessor(httpContext);
    }
}
