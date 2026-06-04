namespace GroundUp.Core.Abstractions;

/// <summary>
/// Provides the 256-bit master key used for AES-GCM encryption of secret settings.
/// Implementations resolve the key from environment variables, files, or HSMs.
/// The key is cached after first resolution; subsequent calls return the same byte array.
/// </summary>
public interface IMasterKeyProvider
{
    /// <summary>
    /// Returns the 256-bit (32-byte) master key.
    /// </summary>
    /// <returns>A 32-byte array containing the master key.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the key cannot be resolved from any configured source.
    /// </exception>
    byte[] GetKey();
}
