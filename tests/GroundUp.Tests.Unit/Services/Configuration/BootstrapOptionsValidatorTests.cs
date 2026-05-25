using FluentAssertions;
using GroundUp.Core.Configuration;
using GroundUp.Services.Configuration;

namespace GroundUp.Tests.Unit.Services.Configuration;

/// <summary>
/// Unit tests for <see cref="BootstrapOptionsValidator"/>.
/// Validates startup-time checks for required configuration values.
/// </summary>
public sealed class BootstrapOptionsValidatorTests
{
    private readonly BootstrapOptionsValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingDatabaseConnection_Fails(string? databaseConnection)
    {
        // Arrange
        var options = new BootstrapOptions
        {
            DatabaseConnection = databaseConnection,
            MasterKey = "dGhpcyBpcyBhIHZhbGlkIGJhc2U2NCBrZXk="
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("DatabaseConnection");
    }

    [Fact]
    public void Validate_MissingBothMasterKeySources_Fails()
    {
        // Arrange
        var options = new BootstrapOptions
        {
            DatabaseConnection = "Host=localhost;Database=test",
            MasterKey = null,
            MasterKeyPath = null
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MasterKey");
    }

    [Fact]
    public void Validate_HasMasterKey_Succeeds()
    {
        // Arrange
        var options = new BootstrapOptions
        {
            DatabaseConnection = "Host=localhost;Database=test",
            MasterKey = "dGhpcyBpcyBhIHZhbGlkIGJhc2U2NCBrZXk=",
            MasterKeyPath = null
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_HasMasterKeyPath_Succeeds()
    {
        // Arrange
        var options = new BootstrapOptions
        {
            DatabaseConnection = "Host=localhost;Database=test",
            MasterKey = null,
            MasterKeyPath = "/etc/groundup/master.key"
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_BootstrapAdminTokenTooShort_Fails()
    {
        // Arrange
        var options = new BootstrapOptions
        {
            DatabaseConnection = "Host=localhost;Database=test",
            MasterKey = "dGhpcyBpcyBhIHZhbGlkIGJhc2U2NCBrZXk=",
            BootstrapAdminToken = "short-token-under-32"
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("BootstrapAdminToken");
        result.FailureMessage.Should().Contain("32");
    }

    [Fact]
    public void Validate_BootstrapAdminTokenLongEnough_Succeeds()
    {
        // Arrange
        var options = new BootstrapOptions
        {
            DatabaseConnection = "Host=localhost;Database=test",
            MasterKey = "dGhpcyBpcyBhIHZhbGlkIGJhc2U2NCBrZXk=",
            BootstrapAdminToken = "this-is-a-valid-token-that-is-at-least-32-characters-long"
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_BootstrapAdminTokenNull_Succeeds()
    {
        // Arrange
        var options = new BootstrapOptions
        {
            DatabaseConnection = "Host=localhost;Database=test",
            MasterKey = "dGhpcyBpcyBhIHZhbGlkIGJhc2U2NCBrZXk=",
            BootstrapAdminToken = null
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_AllValid_Succeeds()
    {
        // Arrange
        var options = new BootstrapOptions
        {
            DatabaseConnection = "Host=localhost;Database=groundup",
            MasterKey = "dGhpcyBpcyBhIHZhbGlkIGJhc2U2NCBrZXk=",
            MasterKeyPath = "/etc/groundup/master.key",
            BootstrapAdminToken = "a-secure-bootstrap-token-that-is-definitely-long-enough"
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }
}
