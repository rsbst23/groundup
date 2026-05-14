namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Tenant summary for the tenant selection list.
/// </summary>
/// <param name="Id">The unique identifier of the tenant.</param>
/// <param name="Name">The display name of the tenant.</param>
/// <param name="Description">An optional description of the tenant.</param>
public record TenantListItemDto(Guid Id, string Name, string? Description);
