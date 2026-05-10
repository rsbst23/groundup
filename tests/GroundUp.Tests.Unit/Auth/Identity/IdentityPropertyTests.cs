using System.Security.Claims;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Unit.Auth.Identity;

/// <summary>
/// Property-based tests for JWT and System identity implementations.
/// Feature: phase-9c-permission-service
/// </summary>
[Trait("Category", "Property")]
public sealed class IdentityPropertyTests
{
    /// <summary>
    /// Feature: phase-9c-permission-service, Property 8:
    /// JWT claim extraction round-trip.
    /// For any valid Guid set as the UserIdClaimType claim, JwtCurrentUser.UserId returns that exact Guid.
    /// For any string set as the EmailClaimType claim, JwtCurrentUser.Email returns that exact string.
    /// For any string set as the DisplayNameClaimType claim, JwtCurrentUser.DisplayName returns that exact string.
    /// For any valid Guid set as the TenantIdClaimType claim, JwtTenantContext.TenantId returns that exact Guid.
    /// **Validates: Requirements 8.1, 8.2, 8.3, 8.6, 9.1, 9.4**
    /// </summary>
    [Property(MaxTest = 200, DisplayName = "Feature: phase-9c-permission-service, Property 8: JWT claim extraction round-trip")]
    public Property JwtClaimExtraction_RoundTrip(Guid userId, NonNull<string> email, NonNull<string> displayName, Guid tenantId)
    {
        // Arrange
        var options = Options.Create(new AuthOptions());

        var claims = new List<Claim>
        {
            new(options.Value.UserIdClaimType, userId.ToString()),
            new(options.Value.EmailClaimType, email.Get),
            new(options.Value.DisplayNameClaimType, displayName.Get),
            new(options.Value.TenantIdClaimType, tenantId.ToString())
        };

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var currentUser = new JwtCurrentUser(httpContextAccessor, options);
        var tenantContext = new JwtTenantContext(httpContextAccessor, options);

        // Assert
        return (currentUser.UserId == userId
            && currentUser.Email == email.Get
            && currentUser.DisplayName == displayName.Get
            && tenantContext.TenantId == tenantId)
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-9c-permission-service, Property 9:
    /// System identity constructor round-trip.
    /// For any Guid userId, optional string email, and optional string displayName passed to
    /// SystemCurrentUser, the properties return those exact values.
    /// For any Guid tenantId passed to SystemTenantContext, the TenantId property returns that exact value.
    /// **Validates: Requirements 10.1, 10.2**
    /// </summary>
    [Property(MaxTest = 200, DisplayName = "Feature: phase-9c-permission-service, Property 9: System identity constructor round-trip")]
    public Property SystemIdentity_ConstructorRoundTrip(Guid userId, string? email, string? displayName, Guid tenantId)
    {
        // Arrange & Act
        var currentUser = new SystemCurrentUser(userId, email, displayName);
        var tenantContext = new SystemTenantContext(tenantId);

        // Assert
        return (currentUser.UserId == userId
            && currentUser.Email == email
            && currentUser.DisplayName == displayName
            && tenantContext.TenantId == tenantId)
            .ToProperty();
    }
}
