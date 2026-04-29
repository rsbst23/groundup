namespace GroundUp.Core.Abstractions;

/// <summary>
/// Contract for encrypting and decrypting sensitive setting values at rest.
/// Consuming applications implement this interface with their own encryption
/// strategy (e.g., AES, AWS KMS, Azure Key Vault).
/// </summary>
public interface ISettingEncryptionProvider
{
    /// <summary>
    /// Encrypts a plaintext setting value for storage.
    /// </summary>
    /// <param name="plaintext">The plaintext value to encrypt.</param>
    /// <returns>The encrypted ciphertext representation.</returns>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a ciphertext setting value back to plaintext.
    /// </summary>
    /// <param name="ciphertext">The encrypted ciphertext to decrypt.</param>
    /// <returns>The original plaintext value.</returns>
    string Decrypt(string ciphertext);
}
