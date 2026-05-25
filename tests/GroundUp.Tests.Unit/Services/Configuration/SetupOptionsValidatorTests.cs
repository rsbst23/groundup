using FluentAssertions;
using GroundUp.Core.Configuration;
using GroundUp.Services.Configuration;

namespace GroundUp.Tests.Unit.Services.Configuration;

/// <summary>
/// Unit tests for <see cref="SetupOptionsValidator"/>.
/// Validates the 4096-byte floor clamp for MaxRequestBodyBytes.
/// </summary>
public sealed class SetupOptionsValidatorTests
{
    private readonly SetupOptionsValidator _validator = new();

    [Fact]
    public void Validate_MaxRequestBodyBytesBelow4096_ClampsTo4096()
    {
        // Arrange
        var options = new SetupOptions { MaxRequestBodyBytes = 1000 };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
        options.MaxRequestBodyBytes.Should().Be(4096);
    }

    [Fact]
    public void Validate_MaxRequestBodyBytesAbove4096_Unchanged()
    {
        // Arrange
        var options = new SetupOptions { MaxRequestBodyBytes = 65536 };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
        options.MaxRequestBodyBytes.Should().Be(65536);
    }

    [Fact]
    public void Validate_MaxRequestBodyBytesExactly4096_Unchanged()
    {
        // Arrange
        var options = new SetupOptions { MaxRequestBodyBytes = 4096 };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
        options.MaxRequestBodyBytes.Should().Be(4096);
    }

    [Fact]
    public void Validate_AlwaysReturnsSuccess()
    {
        // Arrange — even with a zero value, the validator clamps but never fails
        var options = new SetupOptions { MaxRequestBodyBytes = 0 };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
        options.MaxRequestBodyBytes.Should().Be(4096);
    }
}
