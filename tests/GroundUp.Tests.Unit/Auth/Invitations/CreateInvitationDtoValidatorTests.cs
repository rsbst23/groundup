using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Validators;

namespace GroundUp.Tests.Unit.Auth.Invitations;

public sealed class CreateInvitationDtoValidatorTests
{
    private readonly CreateInvitationDtoValidator _sut = new();

    [Theory]
    [InlineData("ana@example.com")]
    [InlineData("  ana@example.com  ")]
    public void Validate_ValidRequest_Passes(string email)
        => _sut.Validate(new CreateInvitationDto(email, Guid.NewGuid(), DateTime.UtcNow.AddDays(1))).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public void Validate_MissingOrMalformedEmail_Fails(string? email)
        => _sut.Validate(new CreateInvitationDto(email, Guid.NewGuid())).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_EmailTooLong_Fails()
        => _sut.Validate(new CreateInvitationDto(new string('a', 315) + "@x.com", Guid.NewGuid())).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NoRole_Fails()
        => _sut.Validate(new CreateInvitationDto("ana@example.com", Guid.Empty)).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_ExpiryInThePast_Fails()
        => _sut.Validate(new CreateInvitationDto("ana@example.com", Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1))).IsValid.Should().BeFalse();
}
