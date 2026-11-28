namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// A user's permissions in the current tenant (GU-96), as returned by <c>/auth/me</c>.
/// </summary>
/// <param name="AllPermissions">
/// True for a SuperAdmin, and for a TenantAdmin of the current tenant: they pass every permission
/// check. <paramref name="Permissions"/> is then empty.
/// </param>
/// <param name="Permissions">The permission keys the user holds in the current tenant, sorted.</param>
public sealed record PermissionSummaryDto(bool AllPermissions, List<string> Permissions);