using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// Grants the tenant's default role (<c>Tenant.DefaultRoleId</c>, set through the roles API) when a
/// sign-in joins (or rejoins) the default tenant (GU-68 Requirement 5.7, GU-95 Requirement 5). A
/// tenant without a default role grants nothing; a default role that is not one of the tenant's
/// roles (only possible with hand-edited data) is logged and skipped.
/// </summary>
internal sealed class DefaultRoleAssigner
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IRoleGrantWriter _roleGrantWriter;
    private readonly ILogger<DefaultRoleAssigner> _logger;

    public DefaultRoleAssigner(
        ITenantRepository tenantRepository,
        IRoleRepository roleRepository,
        IRoleGrantWriter roleGrantWriter,
        ILogger<DefaultRoleAssigner> logger)
    {
        _tenantRepository = tenantRepository;
        _roleRepository = roleRepository;
        _roleGrantWriter = roleGrantWriter;
        _logger = logger;
    }

    /// <summary>
    /// Grants the default role to <paramref name="userId"/> in <paramref name="tenantId"/> (source
    /// DefaultRole), if the tenant has one. Never fails the sign-in.
    /// </summary>
    public async Task AssignAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        Guid? roleId;
        try
        {
            var defaultRole = await _tenantRepository.GetDefaultRoleIdAsync(tenantId, cancellationToken);
            roleId = defaultRole.Success ? defaultRole.Data : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A read failure must not fail the sign-in; the user joins without the default role
            _logger.LogWarning(ex, "The default role of tenant {TenantId} could not be read", tenantId);
            return;
        }

        if (roleId is null)
        {
            _logger.LogDebug("Tenant {TenantId} has no default role", tenantId);
            return;
        }

        // System-level read: the callback runs before a tenant is selected, so the role's tenant is checked here
        var role = await _roleRepository.FindByIdAnyTenantAsync(roleId.Value, cancellationToken);
        if (!role.Success || role.Data is null || role.Data.TenantId != tenantId)
        {
            _logger.LogWarning(
                "Default role {RoleId} of tenant {TenantId} is not one of its roles; no role assigned to user {UserId}",
                roleId, tenantId, userId);
            return;
        }

        var grant = await _roleGrantWriter.GrantAsync(
            userId, role.Data.Id, tenantId, RoleAssignmentSource.DefaultRole, cancellationToken: cancellationToken);
        if (!grant.Success)
        {
            _logger.LogWarning(
                "Default role {RoleId} could not be assigned to user {UserId} in tenant {TenantId}: {Reason}",
                roleId, userId, tenantId, grant.Message);
        }
    }
}
