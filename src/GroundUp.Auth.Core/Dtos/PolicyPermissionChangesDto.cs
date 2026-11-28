namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// The permission links added to and removed from a policy when its permissions are replaced (GU-96).
/// </summary>
/// <param name="Added">The new links.</param>
/// <param name="Removed">The links that were deleted.</param>
public sealed record PolicyPermissionChangesDto(List<PolicyPermissionDto> Added, List<PolicyPermissionDto> Removed)
{
    /// <summary>True when the policy's permissions changed.</summary>
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}