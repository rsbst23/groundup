namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of a permission entity for API responses.
/// </summary>
/// <param name="Id">The unique identifier of the permission.</param>
/// <param name="Key">The programmatic permission key (e.g., "settings.read").</param>
/// <param name="Name">The human-readable permission name.</param>
/// <param name="Description">The permission's description.</param>
/// <param name="Module">The module that defines this permission.</param>
public record PermissionDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    string Module);
