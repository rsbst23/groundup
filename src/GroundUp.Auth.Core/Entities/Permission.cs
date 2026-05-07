using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// Represents a granular, module-scoped authorization permission definition (e.g., "settings.read").
/// Permissions are seeded programmatically by modules and assigned to policies via <see cref="PolicyPermission"/>.
/// </summary>
public sealed class Permission : BaseEntity
{
    /// <summary>
    /// The unique dot-notation key identifying this permission (e.g., "settings.read").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The display name of the permission.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what this permission grants.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The module this permission belongs to (e.g., "settings", "users").
    /// </summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// The policy assignments that include this permission.
    /// </summary>
    public ICollection<PolicyPermission> PolicyPermissions { get; set; } = new List<PolicyPermission>();
}
