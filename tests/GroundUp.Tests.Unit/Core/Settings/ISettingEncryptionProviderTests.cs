using System.Reflection;
using FluentAssertions;
using GroundUp.Core.Abstractions;

namespace GroundUp.Tests.Unit.Core.Settings;

/// <summary>
/// Tests verifying the <see cref="ISettingEncryptionProvider"/> interface
/// structure and method signatures.
/// </summary>
public sealed class ISettingEncryptionProviderTests
{
    [Fact]
    public void ISettingEncryptionProvider_IsInterface()
    {
        typeof(ISettingEncryptionProvider).IsInterface.Should().BeTrue();
    }

    [Fact]
    public void Encrypt_AcceptsStringParameter_ReturnsString()
    {
        var method = typeof(ISettingEncryptionProvider).GetMethod("Encrypt");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(string));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(string));
    }

    [Fact]
    public void Decrypt_AcceptsStringParameter_ReturnsString()
    {
        var method = typeof(ISettingEncryptionProvider).GetMethod("Decrypt");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(string));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(string));
    }
}
