namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Input DTO for creating a new permission.
/// </summary>
/// <param name="Key">The programmatic permission key (e.g., "settings.read").</param>
/// <param name="Name">The human-readable permission name.</param>
/// <param name="Description">The permission's description.</param>
/// <param name="Module">The module that defines this permission.</param>
public record CreatePermissionDto(
    string Key,
    string Name,
    string? Description,
    string Module);
