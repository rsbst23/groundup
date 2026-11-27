using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Validators;

namespace GroundUp.Tests.Unit.Auth.JoinLinks;

public sealed class CreateJoinLinkDtoValidatorTests
{
    private readonly CreateJoinLinkDtoValidator _sut = new();

    [Fact]
    public void Validate_RoleOnly_Passes()
        => _sut.Validate(new CreateJoinLinkDto(Guid.NewGuid())).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_ExpiryAndMaxUses_Passes()
        => _sut.Validate(new CreateJoinLinkDto(Guid.NewGuid(), DateTime.UtcNow.AddDays(1), 10)).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_NoRole_Fails()
        => _sut.Validate(new CreateJoinLinkDto(Guid.Empty)).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_ExpiryInThePast_Fails()
        => _sut.Validate(new CreateJoinLinkDto(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1))).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CreateJoinLinkDtoValidator.MaxUsesLimit + 1)]
    public void Validate_MaxUsesOutOfRange_Fails(int maxUses)
        => _sut.Validate(new CreateJoinLinkDto(Guid.NewGuid(), null, maxUses)).IsValid.Should().BeFalse();
}
