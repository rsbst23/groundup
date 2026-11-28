namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// One permission a role grants through one of its policies (GU-96). A role has one row per
/// permission and policy.
/// </summary>
/// <param name="PermissionKey">The permission key.</param>
/// <param name="PolicyId">The policy granting it.</param>
/// <param name="PolicyName">The policy's name.</param>
public sealed record RoleGrantDto(string PermissionKey, Guid PolicyId, string PolicyName);