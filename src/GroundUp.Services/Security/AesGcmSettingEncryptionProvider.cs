using System.Security.Cryptography;
using System.Text;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Exceptions;

namespace GroundUp.Services.Security;

/// <summary>
/// AES-256-GCM encryption provider for settings marked <c>IsEncrypted=true</c>.
/// Produces self-describing ciphertext: <c>aes-gcm-v1:{nonce-b64}:{ciphertext-b64}:{tag-b64}</c>.
/// Uses a fresh 12-byte nonce per encryption operation, ensuring the same plaintext
/// encrypted twice produces different ciphertexts.
/// </summary>
/// <remarks>
/// <para>
/// Throws <see cref="ArgumentException"/> for null/empty/whitespace input on both
/// <see cref="Encrypt"/> and <see cref="Decrypt"/>. Callers (notably <c>SettingsService</c>)
/// are responsible for short-circuiting null/empty/whitespace values before invoking
/// the provider so that the provider's contract remains "valid input → valid output,
/// invalid input → fail loudly."
/// </para>
/// <para>
/// This class is sealed and singleton-safe — it holds no mutable state beyond the
/// injected <see cref="IMasterKeyProvider"/>.
/// </para>
/// </remarks>
public sealed class AesGcmSettingEncryptionProvider : ISettingEncryptionProvider
{
    /// <summary>
    /// The algorithm version prefix embedded in every ciphertext produced by this provider.
    /// </summary>
    internal const string VersionPrefix = "aes-gcm-v1";

    /// <summary>
    /// Nonce size in bytes (96 bits per NIST recommendation for AES-GCM).
    /// </summary>
    internal const int NonceSize = 12;

    /// <summary>
    /// Authentication tag size in bytes (128 bits for full authentication strength).
    /// </summary>
    internal const int TagSize = 16;

    private readonly IMasterKeyProvider _masterKeyProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AesGcmSettingEncryptionProvider"/> class.
    /// </summary>
    /// <param name="masterKeyProvider">The provider that supplies the 256-bit master key.</param>
    public AesGcmSettingEncryptionProvider(IMasterKeyProvider masterKeyProvider)
    {
        _masterKeyProvider = masterKeyProvider ?? throw new ArgumentNullException(nameof(masterKeyProvider));
    }

    /// <summary>
    /// Encrypts plaintext using AES-256-GCM with a fresh nonce.
    /// </summary>
    /// <param name="plaintext">The plaintext value to encrypt. Must be non-null, non-empty, and non-whitespace.</param>
    /// <returns>
    /// The encrypted value in self-describing format: <c>aes-gcm-v1:{nonce-base64}:{ciphertext-base64}:{tag-base64}</c>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="plaintext"/> is null, empty, or whitespace-only.
    /// </exception>
    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            throw new ArgumentException("Plaintext must be non-null, non-empty, and non-whitespace.", nameof(plaintext));

        var key = _masterKeyProvider.GetKey();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertextBytes = new byte[plaintextBytes.Length];

        RandomNumberGenerator.Fill(nonce);

        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertextBytes, tag);

        return $"{VersionPrefix}:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(ciphertextBytes)}:{Convert.ToBase64String(tag)}";
    }

    /// <summary>
    /// Decrypts ciphertext in <c>aes-gcm-v1</c> format.
    /// </summary>
    /// <param name="ciphertext">
    /// The encrypted value in format <c>aes-gcm-v1:{nonce-base64}:{ciphertext-base64}:{tag-base64}</c>.
    /// Must be non-null, non-empty, and non-whitespace.
    /// </param>
    /// <returns>The decrypted plaintext value.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="ciphertext"/> is null, empty, or whitespace-only.
    /// </exception>
    /// <exception cref="EncryptionException">
    /// Thrown when the prefix is unsupported, the format is malformed (wrong segment count or
    /// invalid base64), or the authentication tag verification fails.
    /// </exception>
    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext))
            throw new ArgumentException("Ciphertext must be non-null, non-empty, and non-whitespace.", nameof(ciphertext));

        var segments = ciphertext.Split(':');

        if (segments.Length != 4)
            throw new EncryptionException(
                $"Malformed ciphertext: expected 4 colon-delimited segments but found {segments.Length}.");

        if (!string.Equals(segments[0], VersionPrefix, StringComparison.Ordinal))
            throw new EncryptionException(
                $"Unsupported ciphertext version prefix '{segments[0]}'. Expected '{VersionPrefix}'.");

        byte[] nonce;
        byte[] ciphertextBytes;
        byte[] tag;

        try
        {
            nonce = Convert.FromBase64String(segments[1]);
        }
        catch (FormatException ex)
        {
            throw new EncryptionException("Malformed ciphertext: nonce segment is not valid base64.", ex);
        }

        try
        {
            ciphertextBytes = Convert.FromBase64String(segments[2]);
        }
        catch (FormatException ex)
        {
            throw new EncryptionException("Malformed ciphertext: ciphertext segment is not valid base64.", ex);
        }

        try
        {
            tag = Convert.FromBase64String(segments[3]);
        }
        catch (FormatException ex)
        {
            throw new EncryptionException("Malformed ciphertext: tag segment is not valid base64.", ex);
        }

        var key = _masterKeyProvider.GetKey();
        var plaintextBytes = new byte[ciphertextBytes.Length];

        try
        {
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Decrypt(nonce, ciphertextBytes, tag, plaintextBytes);
        }
        catch (CryptographicException ex)
        {
            throw new EncryptionException(
                "Ciphertext authentication failed — possible tampering or wrong key.", ex);
        }

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
