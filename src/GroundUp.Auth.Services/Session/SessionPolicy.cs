namespace GroundUp.Auth.Services.Session;

/// <summary>
/// The effective session timeouts for a tenant (or for the System level).
/// </summary>
/// <param name="TokenLifetime">How long a token is valid. Also the inactivity timeout.</param>
/// <param name="AbsoluteLifetime">The longest a session can last from the user's real authentication.</param>
public sealed record SessionPolicy(TimeSpan TokenLifetime, TimeSpan AbsoluteLifetime);
