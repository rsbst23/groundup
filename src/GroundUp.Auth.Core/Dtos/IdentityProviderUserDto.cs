namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// An account in the identity provider, as found by email.
/// </summary>
/// <param name="ExternalUserId">The identity provider's user identifier (the <c>sub</c>).</param>
/// <param name="Email">The account's email address.</param>
/// <param name="EmailVerified">Whether the identity provider has verified the email.</param>
public sealed record IdentityProviderUserDto(string ExternalUserId, string Email, bool EmailVerified);
