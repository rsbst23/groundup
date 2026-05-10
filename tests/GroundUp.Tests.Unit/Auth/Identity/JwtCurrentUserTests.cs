using System.Security.Claims;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Unit.Auth.Identity;

public sealed class JwtCurrentUserTests
{
    private readonly AuthOptions _options = new();

    [Fact]
    public void UserId_NoHttpContext_ReturnsGuidEmpty()
    {
        // Arrange
        var accessor = CreateAccessor(null);
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal(Guid.Empty, sut.UserId);
    }

    [Fact]
    public void Email_NoHttpContext_ReturnsNull()
    {
        // Arrange
        var accessor = CreateAccessor(null);
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Null(sut.Email);
    }

    [Fact]
    public void DisplayName_NoHttpContext_ReturnsNull()
    {
        // Arrange
        var accessor = CreateAccessor(null);
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Null(sut.DisplayName);
    }

    [Fact]
    public void UserId_ValidGuidClaim_ReturnsCorrectGuid()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        var accessor = CreateAccessorWithClaims(
            new Claim("sub", expectedId.ToString()));
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal(expectedId, sut.UserId);
    }

    [Fact]
    public void UserId_InvalidGuidClaim_ReturnsGuidEmpty()
    {
        // Arrange
        var accessor = CreateAccessorWithClaims(
            new Claim("sub", "not-a-guid"));
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal(Guid.Empty, sut.UserId);
    }

    [Fact]
    public void Email_ValidClaim_ReturnsCorrectValue()
    {
        // Arrange
        var accessor = CreateAccessorWithClaims(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("email", "user@example.com"));
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal("user@example.com", sut.Email);
    }

    [Fact]
    public void DisplayName_ValidClaim_ReturnsCorrectValue()
    {
        // Arrange
        var accessor = CreateAccessorWithClaims(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("name", "John Doe"));
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal("John Doe", sut.DisplayName);
    }

    [Fact]
    public void UserId_CustomClaimType_ExtractsFromConfiguredType()
    {
        // Arrange
        _options.UserIdClaimType = "custom_user_id";
        var expectedId = Guid.NewGuid();
        var accessor = CreateAccessorWithClaims(
            new Claim("custom_user_id", expectedId.ToString()));
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal(expectedId, sut.UserId);
    }

    [Fact]
    public void Email_MissingClaim_ReturnsNull()
    {
        // Arrange — authenticated user but no email claim
        var accessor = CreateAccessorWithClaims(
            new Claim("sub", Guid.NewGuid().ToString()));
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Null(sut.Email);
    }

    [Fact]
    public void UserId_UnauthenticatedUser_ReturnsGuidEmpty()
    {
        // Arrange — HttpContext exists but user is not authenticated
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type = not authenticated
        var accessor = CreateAccessor(httpContext);
        var sut = CreateSut(accessor);

        // Act & Assert
        Assert.Equal(Guid.Empty, sut.UserId);
    }

    // --- Helpers ---

    private JwtCurrentUser CreateSut(IHttpContextAccessor accessor)
    {
        return new JwtCurrentUser(accessor, Options.Create(_options));
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
