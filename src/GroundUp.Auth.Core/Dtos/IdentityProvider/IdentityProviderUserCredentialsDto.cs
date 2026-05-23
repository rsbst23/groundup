namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Credentials to set for a user in the identity provider.
/// </summary>
/// <param name="Password">The password to set.</param>
/// <param name="Temporary">Whether the password is temporary (requires reset on next login).</param>
public record IdentityProviderUserCredentialsDto(string Password, bool Temporary);
