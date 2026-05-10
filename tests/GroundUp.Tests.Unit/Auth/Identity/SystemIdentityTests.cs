using GroundUp.Auth.Services.Identity;

namespace GroundUp.Tests.Unit.Auth.Identity;

public sealed class SystemIdentityTests
{
    [Fact]
    public void SystemCurrentUser_ExposesConstructorValues()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";
        var displayName = "Test User";

        // Act
        var sut = new SystemCurrentUser(userId, email, displayName);

        // Assert
        Assert.Equal(userId, sut.UserId);
        Assert.Equal(email, sut.Email);
        Assert.Equal(displayName, sut.DisplayName);
    }

    [Fact]
    public void SystemCurrentUser_OptionalParametersDefaultToNull()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var sut = new SystemCurrentUser(userId);

        // Assert
        Assert.Equal(userId, sut.UserId);
        Assert.Null(sut.Email);
        Assert.Null(sut.DisplayName);
    }

    [Fact]
    public void SystemTenantContext_ExposesConstructorValue()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        // Act
        var sut = new SystemTenantContext(tenantId);

        // Assert
        Assert.Equal(tenantId, sut.TenantId);
    }

    [Fact]
    public void SystemTenantContext_GuidEmpty_IsValid()
    {
        // Act
        var sut = new SystemTenantContext(Guid.Empty);

        // Assert
        Assert.Equal(Guid.Empty, sut.TenantId);
    }
}
