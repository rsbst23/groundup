using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Exceptions;
using GroundUp.Services.Security;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Security;

/// <summary>
/// Unit tests for <see cref="AesGcmSettingEncryptionProvider"/>.
/// Validates AES-GCM encryption/decryption behavior including format, round-trip,
/// error handling, and input validation.
/// </summary>
public sealed class AesGcmSettingEncryptionProviderTests
{
    private static readonly byte[] FixedKey = new byte[]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
    };

    private readonly AesGcmSettingEncryptionProvider _provider;

    public AesGcmSettingEncryptionProviderTests()
    {
        var mockKeyProvider = Substitute.For<IMasterKeyProvider>();
        mockKeyProvider.GetKey().Returns(FixedKey);
        _provider = new AesGcmSettingEncryptionProvider(mockKeyProvider);
    }

    #region Encrypt Format Tests

    [Fact]
    public void Encrypt_ValidInput_ProducesAesGcmV1Format()
    {
        // Arrange
        var plaintext = "my-secret-value";

        // Act
        var result = _provider.Encrypt(plaintext);

        // Assert
        result.Should().StartWith("aes-gcm-v1:");
        var segments = result.Split(':');
        segments.Should().HaveCount(4);
        segments[0].Should().Be("aes-gcm-v1");

        // Verify each segment after the prefix is valid base64
        var nonce = Convert.FromBase64String(segments[1]);
        var ciphertext = Convert.FromBase64String(segments[2]);
        var tag = Convert.FromBase64String(segments[3]);

        nonce.Should().HaveCount(12); // 96-bit nonce
        ciphertext.Should().NotBeEmpty();
        tag.Should().HaveCount(16); // 128-bit tag
    }

    #endregion

    #region Decrypt Round-Trip Tests

    [Fact]
    public void Decrypt_ValidCiphertext_ReturnsOriginalPlaintext()
    {
        // Arrange
        var plaintext = "Hello, World! This is a secret setting value.";
        var encrypted = _provider.Encrypt(plaintext);

        // Act
        var decrypted = _provider.Decrypt(encrypted);

        // Assert
        decrypted.Should().Be(plaintext);
    }

    #endregion

    #region Decrypt Malformed Input Tests

    [Fact]
    public void Decrypt_MalformedInput_MissingSegments_ThrowsEncryptionException()
    {
        // Arrange — only 2 segments instead of 4
        var malformed = "aes-gcm-v1:onlyone";

        // Act
        var act = () => _provider.Decrypt(malformed);

        // Assert
        act.Should().Throw<EncryptionException>()
            .WithMessage("*segment*");
    }

    [Fact]
    public void Decrypt_MalformedInput_InvalidBase64_ThrowsEncryptionException()
    {
        // Arrange — valid prefix and segment count, but invalid base64 in nonce
        var malformed = "aes-gcm-v1:not!!valid!!base64:AAAA:BBBB";

        // Act
        var act = () => _provider.Decrypt(malformed);

        // Assert
        act.Should().Throw<EncryptionException>()
            .WithMessage("*not valid base64*");
    }

    [Fact]
    public void Decrypt_UnsupportedPrefix_ThrowsEncryptionException()
    {
        // Arrange
        var unsupported = "aes-cbc-v1:AAAA:BBBB:CCCC";

        // Act
        var act = () => _provider.Decrypt(unsupported);

        // Assert
        act.Should().Throw<EncryptionException>()
            .WithMessage("*Unsupported*")
            .WithMessage("*aes-cbc-v1*");
    }

    [Fact]
    public void Decrypt_TamperedTag_ThrowsEncryptionException()
    {
        // Arrange — encrypt a valid value, then tamper with the tag segment
        var encrypted = _provider.Encrypt("sensitive data");
        var segments = encrypted.Split(':');
        var tagBytes = Convert.FromBase64String(segments[3]);
        tagBytes[0] ^= 0xFF; // Flip bits in the first byte of the tag
        segments[3] = Convert.ToBase64String(tagBytes);
        var tampered = string.Join(':', segments);

        // Act
        var act = () => _provider.Decrypt(tampered);

        // Assert
        act.Should().Throw<EncryptionException>()
            .WithMessage("*authentication failed*");
    }

    #endregion

    #region Encrypt Input Validation Tests

    [Fact]
    public void Encrypt_NullInput_ThrowsArgumentException()
    {
        // Act
        var act = () => _provider.Encrypt(null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("plaintext");
    }

    [Fact]
    public void Encrypt_EmptyInput_ThrowsArgumentException()
    {
        // Act
        var act = () => _provider.Encrypt(string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("plaintext");
    }

    [Fact]
    public void Encrypt_WhitespaceInput_ThrowsArgumentException()
    {
        // Act
        var act = () => _provider.Encrypt("   \t\n  ");

        // Assert
        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("plaintext");
    }

    #endregion

    #region Decrypt Input Validation Tests

    [Fact]
    public void Decrypt_NullInput_ThrowsArgumentException()
    {
        // Act
        var act = () => _provider.Decrypt(null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("ciphertext");
    }

    [Fact]
    public void Decrypt_EmptyInput_ThrowsArgumentException()
    {
        // Act
        var act = () => _provider.Decrypt(string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("ciphertext");
    }

    [Fact]
    public void Decrypt_WhitespaceInput_ThrowsArgumentException()
    {
        // Act
        var act = () => _provider.Decrypt("   \t\n  ");

        // Assert
        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("ciphertext");
    }

    #endregion
}
