using FluentAssertions;
using GroundUp.Core.Enums;

namespace GroundUp.Tests.Unit.Core.Settings;

/// <summary>
/// Tests verifying the <see cref="SettingDataType"/> enum has the correct
/// members and integer values.
/// </summary>
public sealed class SettingDataTypeTests
{
    [Fact]
    public void SettingDataType_HasExactly8Members()
    {
        Enum.GetValues<SettingDataType>().Should().HaveCount(8);
    }

    [Theory]
    [InlineData(SettingDataType.String, 0)]
    [InlineData(SettingDataType.Int, 1)]
    [InlineData(SettingDataType.Long, 2)]
    [InlineData(SettingDataType.Decimal, 3)]
    [InlineData(SettingDataType.Bool, 4)]
    [InlineData(SettingDataType.DateTime, 5)]
    [InlineData(SettingDataType.Date, 6)]
    [InlineData(SettingDataType.Json, 7)]
    public void SettingDataType_Member_HasCorrectIntegerValue(SettingDataType member, int expectedValue)
    {
        ((int)member).Should().Be(expectedValue);
    }
}
