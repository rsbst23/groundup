namespace GroundUp.Auth.Services;

/// <summary>
/// Claims taken from an ID token after <see cref="IIdTokenValidator"/> has validated it.
/// </summary>
/// <param name="Subject">The identity provider's user identifier (<c>sub</c>).</param>
/// <param name="AuthTime">
/// When the user last actually authenticated at the identity provider (<c>auth_time</c>), or null
/// if the provider did not include the claim. A silent single sign-on keeps the original value.
/// </param>
public sealed record ValidatedIdToken(string Subject, DateTimeOffset? AuthTime);
