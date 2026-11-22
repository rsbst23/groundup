using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Validators;

namespace GroundUp.Tests.Unit.Auth.Validators;

public sealed class CreateOrganizationRequestDtoValidatorTests
{
    private readonly CreateOrganizationRequestDtoValidator _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingOrBlank_Required(string? name)
    {
        var result = _sut.Validate(new CreateOrganizationRequestDto(name));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be("Organization name is required.");
    }

    [Fact]
    public void Validate_LongerThan200AfterTrim_TooLong()
    {
        var result = _sut.Validate(new CreateOrganizationRequestDto(new string('a', 201)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.ErrorMessage
            .Should().Be("Organization name must not exceed 200 characters.");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(1)]
    public void Validate_UpTo200_Valid(int length)
    {
        _sut.Validate(new CreateOrganizationRequestDto(new string('a', length))).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_200WithSurroundingSpaces_ValidBecauseLengthIsCheckedAfterTrim()
    {
        _sut.Validate(new CreateOrganizationRequestDto("  " + new string('a', 200) + "  ")).IsValid.Should().BeTrue();
    }
}
