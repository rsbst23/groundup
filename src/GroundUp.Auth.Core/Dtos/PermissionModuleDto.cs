namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// The permissions of one module in the permission catalogue (GU-96).
/// </summary>
/// <param name="Module">The module name (for example <c>auth</c>, <c>settings</c>).</param>
/// <param name="Permissions">The module's permissions, sorted by key.</param>
public sealed record PermissionModuleDto(string Module, List<PermissionDto> Permissions);