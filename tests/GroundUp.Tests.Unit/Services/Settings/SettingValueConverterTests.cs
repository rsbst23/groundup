using FluentAssertions;
using GroundUp.Core.Enums;
using GroundUp.Services.Settings;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Unit tests for <see cref="SettingValueConverter"/> covering all data type conversions,
/// null handling, invalid input handling, and AllowMultiple JSON array deserialization.
/// </summary>
public sealed class SettingValueConverterTests
{
    [Fact]
    public void Convert_StringType_ReturnsStringValue()
    {
        var result = SettingValueConverter.Convert<string>("hello", SettingDataType.String, false, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().Be("hello");
    }

    [Fact]
    public void Convert_IntType_ReturnsIntValue()
    {
        var result = SettingValueConverter.Convert<int>("42", SettingDataType.Int, false, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().Be(42);
    }

    [Fact]
    public void Convert_LongType_ReturnsLongValue()
    {
        var result = SettingValueConverter.Convert<long>("9999999999", SettingDataType.Long, false, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().Be(9999999999L);
    }

    [Fact]
    public void Convert_DecimalType_ReturnsDecimalValue()
    {
        var result = SettingValueConverter.Convert<decimal>("3.14", SettingDataType.Decimal, false, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().Be(3.14m);
    }

    [Fact]
    public void Convert_BoolType_TrueValue_ReturnsBool()
    {
        var result = SettingValueConverter.Convert<bool>("True", SettingDataType.Bool, false, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().BeTrue();
    }

    [Fact]
    public void Convert_BoolType_CaseInsensitive_ReturnsBool()
    {
        var result = SettingValueConverter.Convert<bool>("true", SettingDataType.Bool, false, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().BeTrue();
    }

    [Fact]
    public void Convert_DateTimeType_ReturnsDateTime()
    {
        var result = SettingValueConverter.Convert<DateTime>("2024-01-15T10:30:00.0000000Z", SettingDataType.DateTime, false, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().Be(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Convert_DateType_ReturnsDateOnly()
    {
        var result = SettingValueConverter.Convert<DateOnly>("2024-01-15", SettingDataType.Date, false, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().Be(new DateOnly(2024, 1, 15));
    }

    [Fact]
    public void Convert_JsonType_ReturnsDeserializedObject()
    {
        var json = """{"Name":"Test","Value":42}""";

        var result = SettingValueConverter.Convert<TestJsonObject>(json, SettingDataType.Json, false, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Name.Should().Be("Test");
        result.Data.Value.Should().Be(42);
    }

    [Fact]
    public void Convert_NullValue_ReturnsDefault()
    {
        var result = SettingValueConverter.Convert<int>(null, SettingDataType.Int, false, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().Be(0);
    }

    [Fact]
    public void Convert_NullStringValue_ReturnsDefault()
    {
        var result = SettingValueConverter.Convert<string>(null, SettingDataType.String, false, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().BeNull();
    }

    [Fact]
    public void Convert_InvalidInt_ReturnsFail()
    {
        var result = SettingValueConverter.Convert<int>("not-a-number", SettingDataType.Int, false, "key");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not-a-number");
        result.Message.Should().Contain("key");
    }

    [Fact]
    public void Convert_InvalidBool_ReturnsFail()
    {
        var result = SettingValueConverter.Convert<bool>("maybe", SettingDataType.Bool, false, "key");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("maybe");
        result.Message.Should().Contain("key");
    }

    [Fact]
    public void Convert_AllowMultiple_ReturnsListOfInt()
    {
        var result = SettingValueConverter.Convert<List<int>>("[1,2,3]", SettingDataType.Int, true, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().BeEquivalentTo(new List<int> { 1, 2, 3 });
    }

    [Fact]
    public void Convert_AllowMultiple_ReturnsListOfString()
    {
        var result = SettingValueConverter.Convert<List<string>>("""["a","b"]""", SettingDataType.String, true, "key");

        result.Success.Should().BeTrue();
        result.Data.Should().BeEquivalentTo(new List<string> { "a", "b" });
    }

    private record TestJsonObject(string Name, int Value);
}
