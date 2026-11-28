using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Validators;

namespace GroundUp.Tests.Unit.Auth.Validators;

/// <summary>
/// GU-96: the policy input validators. Whether keys exist is checked by the policy service.
/// </summary>
public sealed class PolicyDtoValidatorTests
{
    [Fact]
    public void Create_NameOnly_Valid()
        => new CreatePolicyDtoValidator().Validate(new CreatePolicyDto("Editors", null)).IsValid.Should().BeTrue();

    [Fact]
    public void Create_WithKeys_Valid()
        => new CreatePolicyDtoValidator().Validate(new CreatePolicyDto("Editors", "Edit", ["auth.members.read"])).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_EmptyKey_Invalid(string key)
        => new CreatePolicyDtoValidator().Validate(new CreatePolicyDto("Editors", null, [key])).IsValid.Should().BeFalse();

    [Fact]
    public void Create_TooManyKeys_Invalid()
    {
        var keys = Enumerable.Range(0, PermissionKeyCountLimit + 1).Select(i => $"test.k{i}.read").ToList();

        new CreatePolicyDtoValidator().Validate(new CreatePolicyDto("Editors", null, keys)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Create_KeyTooLong_Invalid()
        => new CreatePolicyDtoValidator().Validate(new CreatePolicyDto("Editors", null, [new string('a', 201)])).IsValid.Should().BeFalse();

    [Fact]
    public void Create_EmptyName_Invalid()
        => new CreatePolicyDtoValidator().Validate(new CreatePolicyDto("", null)).IsValid.Should().BeFalse();

    [Fact]
    public void Update_Valid_AndEmptyNameInvalid()
    {
        new UpdatePolicyDtoValidator().Validate(new UpdatePolicyDto("Editors", "Edit")).IsValid.Should().BeTrue();
        new UpdatePolicyDtoValidator().Validate(new UpdatePolicyDto("", null)).IsValid.Should().BeFalse();
        new UpdatePolicyDtoValidator().Validate(new UpdatePolicyDto(new string('a', 201), null)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void SetPermissions_EmptyList_Valid()
        => new SetPolicyPermissionsDtoValidator().Validate(new SetPolicyPermissionsDto([])).IsValid.Should().BeTrue();

    [Fact]
    public void SetPermissions_NullList_Invalid()
        => new SetPolicyPermissionsDtoValidator().Validate(new SetPolicyPermissionsDto(null!)).IsValid.Should().BeFalse();

    [Fact]
    public void SetPermissions_EmptyKey_Invalid()
        => new SetPolicyPermissionsDtoValidator().Validate(new SetPolicyPermissionsDto(["auth.members.read", ""])).IsValid.Should().BeFalse();

    private const int PermissionKeyCountLimit = 200;
}
