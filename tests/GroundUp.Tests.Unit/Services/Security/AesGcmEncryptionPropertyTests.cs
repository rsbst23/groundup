using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Exceptions;
using GroundUp.Services.Security;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Security;

/// <summary>
/// Property-based tests for <see cref="AesGcmSettingEncryptionProvider"/>.
/// Validates encryption correctness properties from the Phase 10AB design document.
/// </summary>
public sealed class AesGcmEncryptionPropertyTests
{
    private static readonly byte[] FixedKey = new byte[]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
    };

    private static readonly byte[] AlternateKey = new byte[]
    {
        0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8,
        0xA9, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xAF, 0xB0,
        0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8,
        0xB9, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0
    };

    private static AesGcmSettingEncryptionProvider CreateProvider(byte[] key)
    {
        var mockKeyProvider = Substitute.For<IMasterKeyProvider>();
        mockKeyProvider.GetKey().Returns(key);
        return new AesGcmSettingEncryptionProvider(mockKeyProvider);
    }

    /// <summary>
    /// Property 1: Encryption Round-Trip Integrity.
    /// For any non-null, non-whitespace UTF-8 string v: Decrypt(Encrypt(v)) == v.
    /// **Validates: Requirements 2.2, 2.3, 3.1, 3.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EncryptThenDecrypt_ReturnsOriginalValue(NonEmptyString input)
    {
        var value = input.Get;

        Func<bool> property = () =>
        {
            var provider = CreateProvider(FixedKey);
            var encrypted = provider.Encrypt(value);
            var decrypted = provider.Decrypt(encrypted);
            return decrypted == value;
        };

        return property.When(!string.IsNullOrWhiteSpace(value));
    }

    /// <summary>
    /// Property 2: Encryption Produces Tagged, Fresh Ciphertext.
    /// For any non-null, non-whitespace UTF-8 string v:
    ///   let c1 = Encrypt(v), c2 = Encrypt(v)
    ///   c1 != v AND c1.StartsWith("aes-gcm-v1:") AND c1 != c2
    /// **Validates: Requirements 2.2, 2.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Encrypt_ProducesTaggedFreshCiphertext(NonEmptyString input)
    {
        var value = input.Get;

        Func<bool> property = () =>
        {
            var provider = CreateProvider(FixedKey);
            var c1 = provider.Encrypt(value);
            var c2 = provider.Encrypt(value);

            var isNotPlaintext = c1 != value;
            var hasVersionPrefix = c1.StartsWith("aes-gcm-v1:");
            var isFreshNonce = c1 != c2;
            var hasCorrectSegments = c1.Split(':').Length == 4;

            return isNotPlaintext && hasVersionPrefix && isFreshNonce && hasCorrectSegments;
        };

        return property.When(!string.IsNullOrWhiteSpace(value));
    }

    /// <summary>
    /// Property 3: Decryption Fails on Wrong Key, Tampering, or Unsupported Prefix.
    /// For any non-whitespace string v:
    ///   Decrypt_k2(Encrypt_k1(v)) throws EncryptionException
    ///   Decrypt of mutated ciphertext throws EncryptionException
    ///   Decrypt of string without "aes-gcm-v1:" prefix throws EncryptionException
    /// **Validates: Requirements 2.4, 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Decrypt_FailsOnWrongKey(NonEmptyString input)
    {
        var value = input.Get;

        Func<bool> property = () =>
        {
            var provider1 = CreateProvider(FixedKey);
            var provider2 = CreateProvider(AlternateKey);

            var encrypted = provider1.Encrypt(value);

            try
            {
                provider2.Decrypt(encrypted);
                return false; // Should have thrown
            }
            catch (EncryptionException)
            {
                return true;
            }
        };

        return property.When(!string.IsNullOrWhiteSpace(value));
    }

    [Property(MaxTest = 100)]
    public Property Decrypt_FailsOnTamperedCiphertext(NonEmptyString input, PositiveInt byteOffset)
    {
        var value = input.Get;

        Func<bool> property = () =>
        {
            var provider = CreateProvider(FixedKey);
            var encrypted = provider.Encrypt(value);

            // Tamper with the ciphertext segment (index 2 in the colon-delimited format)
            var segments = encrypted.Split(':');
            var ciphertextBytes = Convert.FromBase64String(segments[2]);

            if (ciphertextBytes.Length == 0)
                return true; // Degenerate case, skip

            var idx = byteOffset.Get % ciphertextBytes.Length;
            ciphertextBytes[idx] ^= 0xFF; // Flip all bits at the chosen position
            segments[2] = Convert.ToBase64String(ciphertextBytes);
            var tampered = string.Join(':', segments);

            try
            {
                provider.Decrypt(tampered);
                return false; // Should have thrown
            }
            catch (EncryptionException)
            {
                return true;
            }
        };

        return property.When(!string.IsNullOrWhiteSpace(value));
    }

    [Property(MaxTest = 100)]
    public Property Decrypt_FailsOnUnsupportedPrefix(NonEmptyString input)
    {
        var value = input.Get;

        Func<bool> property = () =>
        {
            var provider = CreateProvider(FixedKey);

            try
            {
                provider.Decrypt(value);
                return false; // Should have thrown
            }
            catch (EncryptionException)
            {
                return true;
            }
        };

        // Filter: non-whitespace AND does not start with the valid prefix
        return property.When(!string.IsNullOrWhiteSpace(value) && !value.StartsWith("aes-gcm-v1:"));
    }

    /// <summary>
    /// Property 4: Provider Rejects Null/Empty/Whitespace with ArgumentException.
    /// For any string s that is null, empty, or whitespace-only:
    ///   Encrypt(s) throws ArgumentException
    ///   Decrypt(s) throws ArgumentException
    /// **Validates: Requirements 2.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EncryptAndDecrypt_RejectWhitespace_WithArgumentException(int seed)
    {
        var provider = CreateProvider(FixedKey);

        // Generate whitespace-only strings: spaces, tabs, newlines, empty, or combinations
        var whitespaceChars = new[] { ' ', '\t', '\n', '\r', '\v', '\f' };
        var rng = new System.Random(seed);
        var length = rng.Next(0, 10); // 0 = empty string
        var whitespace = new string(Enumerable.Range(0, length)
            .Select(_ => whitespaceChars[rng.Next(whitespaceChars.Length)])
            .ToArray());

        var encryptThrows = false;
        try
        {
            provider.Encrypt(whitespace);
        }
        catch (ArgumentException ex) when (ex.ParamName == "plaintext")
        {
            encryptThrows = true;
        }

        var decryptThrows = false;
        try
        {
            provider.Decrypt(whitespace);
        }
        catch (ArgumentException ex) when (ex.ParamName == "ciphertext")
        {
            decryptThrows = true;
        }

        return (encryptThrows && decryptThrows).ToProperty();
    }

    [Fact]
    public void Encrypt_RejectsNull_WithArgumentException()
    {
        var provider = CreateProvider(FixedKey);

        var encryptEx = Assert.Throws<ArgumentException>(() => provider.Encrypt(null!));
        Assert.Equal("plaintext", encryptEx.ParamName);

        var decryptEx = Assert.Throws<ArgumentException>(() => provider.Decrypt(null!));
        Assert.Equal("ciphertext", decryptEx.ParamName);
    }
}
