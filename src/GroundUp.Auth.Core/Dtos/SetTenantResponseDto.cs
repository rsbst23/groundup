namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Response from tenant selection — either a token (tenant selected) or a list of available tenants (selection required).
/// </summary>
/// <param name="SelectionRequired">True if the user must explicitly choose a tenant from the available list.</param>
/// <param name="AvailableTenants">The list of tenants available for selection, populated when <paramref name="SelectionRequired"/> is true.</param>
/// <param name="Token">The issued JWT token scoped to the selected tenant, populated when <paramref name="SelectionRequired"/> is false.</param>
public record SetTenantResponseDto(bool SelectionRequired, List<TenantListItemDto>? AvailableTenants, string? Token);
