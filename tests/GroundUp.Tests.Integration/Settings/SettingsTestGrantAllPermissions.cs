using GroundUp.Auth.Services;

namespace GroundUp.Tests.Integration.Settings;

/// <summary>
/// Permission service that grants every permission and system role, as the SuperAdmin bypass does.
/// Used with <see cref="SettingsTestIdentity"/> by the settings CRUD and cascade tests.
/// </summary>
internal sealed class SettingsTestGrantAllPermissions : IPermissionService
{
    public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> HasAnyPermissionAsync(Guid userId, IEnumerable<string> permissionKeys, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<HashSet<string>> GetUserPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(new HashSet<string>());

    public Task<GroundUp.Auth.Core.Dtos.PermissionSummaryDto> GetPermissionSummaryAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(new GroundUp.Auth.Core.Dtos.PermissionSummaryDto(true, []));

    public Task<bool> HasSystemRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> HasAnySystemRoleAsync(Guid userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
