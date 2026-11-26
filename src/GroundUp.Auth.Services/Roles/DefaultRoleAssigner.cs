using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// Grants the role named by the <c>auth.application.default-role</c> setting when a sign-in joins
/// (or rejoins) the default tenant (GU-68 Requirement 5.7). The setting is read for that tenant
/// (tenant value, then System). A name that matches no role in the tenant is logged and skipped.
/// </summary>
internal sealed class DefaultRoleAssigner
{
    /// <summary>The setting key.</summary>
    public const string SettingKey = "auth.application.default-role";

    private readonly ISettingsService _settingsService;
    private readonly IRoleRepository _roleRepository;
    private readonly IRoleGrantWriter _roleGrantWriter;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<DefaultRoleAssigner> _logger;

    public DefaultRoleAssigner(
        ISettingsService settingsService,
        IRoleRepository roleRepository,
        IRoleGrantWriter roleGrantWriter,
        TenantContext tenantContext,
        ILogger<DefaultRoleAssigner> logger)
    {
        _settingsService = settingsService;
        _roleRepository = roleRepository;
        _roleGrantWriter = roleGrantWriter;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Grants the default role to <paramref name="userId"/> in <paramref name="tenantId"/> (source
    /// DefaultRole), if one is configured. Never fails the sign-in.
    /// </summary>
    public async Task AssignAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var roleName = await ReadSettingForTenantAsync(tenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(roleName))
        {
            _logger.LogDebug("No default role configured for tenant {TenantId}", tenantId);
            return;
        }

        var role = await _roleRepository.GetByNameAsync(tenantId, roleName, cancellationToken);
        if (!role.Success || role.Data is null)
        {
            _logger.LogWarning(
                "Default role {RoleName} does not exist in tenant {TenantId}; no role assigned to user {UserId}",
                roleName, tenantId, userId);
            return;
        }

        var grant = await _roleGrantWriter.GrantAsync(
            userId, role.Data.Id, tenantId, RoleAssignmentSource.DefaultRole, cancellationToken: cancellationToken);
        if (!grant.Success)
        {
            _logger.LogWarning(
                "Default role {RoleName} could not be assigned to user {UserId} in tenant {TenantId}: {Reason}",
                roleName, userId, tenantId, grant.Message);
        }
    }

    /// <summary>
    /// The settings scope chain follows the tenant context, which a sign-in callback has not set
    /// (or has set to another tenant), so it points at the joined tenant for the read.
    /// </summary>
    private async Task<string?> ReadSettingForTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var previous = _tenantContext.TenantId;
        _tenantContext.TenantId = tenantId;
        try
        {
            var result = await _settingsService.GetAsync<string>(SettingKey, cancellationToken);
            return result is { Success: true } ? result.Data?.Trim() : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A settings failure must not fail the sign-in; the user joins without the default role
            _logger.LogWarning(ex, "The default role setting could not be read for tenant {TenantId}", tenantId);
            return null;
        }
        finally
        {
            _tenantContext.TenantId = previous;
        }
    }
}
