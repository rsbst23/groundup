namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Input DTO that replaces a policy's permissions with the given keys (GU-96).
/// </summary>
/// <param name="PermissionKeys">The keys the policy grants afterwards. An empty list removes them all.</param>
public record SetPolicyPermissionsDto(List<string> PermissionKeys);