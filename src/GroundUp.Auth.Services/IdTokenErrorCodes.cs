namespace GroundUp.Auth.Services;

/// <summary>
/// Error codes returned by <see cref="IIdTokenValidator"/> and surfaced by the login callback.
/// </summary>
public static class IdTokenErrorCodes
{
    /// <summary>The ID token is valid but its nonce does not match the login flow.</summary>
    public const string NonceMismatch = "NONCE_MISMATCH";

    /// <summary>The ID token is missing, unsigned, wrongly signed, expired, or for another issuer or audience.</summary>
    public const string Invalid = "ID_TOKEN_INVALID";
}
