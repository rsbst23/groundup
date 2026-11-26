namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Body of the public invitation endpoints. The token is sent in the body, not the URL, so it does
/// not end up in access logs.
/// </summary>
/// <param name="Token">The token from the invitation link.</param>
/// <param name="ForceLogin">
/// Accept only: ask the identity provider for credentials even when a single sign-on session is
/// alive (for example after <c>INVITATION_EMAIL_MISMATCH</c>, to sign in with the invited address).
/// </param>
public sealed record InvitationTokenRequestDto(string? Token, bool ForceLogin = false);
