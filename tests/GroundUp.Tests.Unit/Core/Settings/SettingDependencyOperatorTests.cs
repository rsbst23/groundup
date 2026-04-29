using FluentAssertions;
using GroundUp.Core.Constants;

namespace GroundUp.Tests.Unit.Core.Settings;

/// <summary>
/// Tests verifying the <see cref="SettingDependencyOperator"/> static class
/// structure and constant values.
/// </summary>
public sealed class SettingDependencyOperatorTests
{
    [Fact]
    public void SettingDependencyOperator_IsStaticClass()
    {
        var type = typeof(SettingDependencyOperator);
        type.IsAbstract.Should().BeTrue();
        type.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Equals_HasCorrectValue()
    {
        SettingDependencyOperator.Equals.Should().Be("Equals");
    }

    [Fact]
    public void NotEquals_HasCorrectValue()
    {
        SettingDependencyOperator.NotEquals.Should().Be("NotEquals");
    }

    [Fact]
    public void Contains_HasCorrectValue()
    {
        SettingDependencyOperator.Contains.Should().Be("Contains");
    }

    [Fact]
    public void In_HasCorrectValue()
    {
        SettingDependencyOperator.In.Should().Be("In");
    }
}
