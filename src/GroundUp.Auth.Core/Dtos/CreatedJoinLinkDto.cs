namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// The response to creating or regenerating a join link: the only time the token is returned.
/// </summary>
/// <param name="Link">The join link.</param>
/// <param name="Token">The link token, shown once. Only its hash is stored.</param>
public sealed record CreatedJoinLinkDto(JoinLinkDto Link, string Token);
