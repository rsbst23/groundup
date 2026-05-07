using FluentAssertions;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Unit.Authentication;

/// <summary>
/// Verifies enum member counts and explicit integer values for auth enums.
/// </summary>
public sealed class AuthEnumTests
{
    [Fact]
    public void TenantType_HasExactlyTwoMembers()
    {
        Enum.GetValues<TenantType>().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(TenantType.Standard, 0)]
    [InlineData(TenantType.Enterprise, 1)]
    public void TenantType_HasCorrectIntegerValues(TenantType member, int expectedValue)
    {
        ((int)member).Should().Be(expectedValue);
    }

    [Fact]
    public void OnboardingMode_HasExactlyThreeMembers()
    {
        Enum.GetValues<OnboardingMode>().Should().HaveCount(3);
    }

    [Theory]
    [InlineData(OnboardingMode.InviteOnly, 0)]
    [InlineData(OnboardingMode.JoinLink, 1)]
    [InlineData(OnboardingMode.Open, 2)]
    public void OnboardingMode_HasCorrectIntegerValues(OnboardingMode member, int expectedValue)
    {
        ((int)member).Should().Be(expectedValue);
    }

    [Fact]
    public void RoleType_HasExactlyThreeMembers()
    {
        Enum.GetValues<RoleType>().Should().HaveCount(3);
    }

    [Theory]
    [InlineData(RoleType.System, 0)]
    [InlineData(RoleType.Application, 1)]
    [InlineData(RoleType.Workspace, 2)]
    public void RoleType_HasCorrectIntegerValues(RoleType member, int expectedValue)
    {
        ((int)member).Should().Be(expectedValue);
    }
}
