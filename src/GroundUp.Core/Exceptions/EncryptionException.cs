namespace GroundUp.Core.Exceptions;

/// <summary>
/// Exception thrown when an encryption or decryption operation fails.
/// This includes unsupported ciphertext format prefixes, malformed ciphertext,
/// authentication tag verification failures, and wrong-key scenarios.
/// </summary>
public sealed class EncryptionException : GroundUpException
{
    /// <summary>
    /// Creates a new <see cref="EncryptionException"/> with the specified message.
    /// </summary>
    /// <param name="message">A human-readable description of the encryption error.</param>
    public EncryptionException(string message) : base(message) { }

    /// <summary>
    /// Creates a new <see cref="EncryptionException"/> with the specified message and inner exception.
    /// </summary>
    /// <param name="message">A human-readable description of the encryption error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public EncryptionException(string message, Exception innerException) : base(message, innerException) { }
}
