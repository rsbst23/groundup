namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request to select a tenant for the current session.
/// </summary>
/// <param name="TenantId">The tenant to select, or null to trigger auto-selection or list retrieval.</param>
public record SetTenantRequestDto(Guid? TenantId);
