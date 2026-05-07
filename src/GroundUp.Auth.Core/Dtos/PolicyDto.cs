namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of a policy entity for API responses.
/// </summary>
/// <param name="Id">The unique identifier of the policy.</param>
/// <param name="Name">The policy name.</param>
/// <param name="Description">The policy's description.</param>
/// <param name="TenantId">The tenant that owns this policy.</param>
public record PolicyDto(
    Guid Id,
    string Name,
    string? Description,
    Guid TenantId);
