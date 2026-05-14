namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Token response from an external identity provider.
/// </summary>
/// <param name="AccessToken">The access token issued by the identity provider.</param>
/// <param name="RefreshToken">The optional refresh token for obtaining new access tokens.</param>
/// <param name="ExpiresIn">The token lifetime in seconds.</param>
/// <param name="IdToken">The optional OpenID Connect ID token.</param>
public record TokenResponseDto(string AccessToken, string? RefreshToken, int ExpiresIn, string? IdToken);
