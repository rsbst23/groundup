using GroundUp.Auth.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Settings;

/// <summary>
/// Enforcing <see cref="ISettingsAuthorizer"/>. Rules, in order:
/// <list type="number">
///   <item>While setup is incomplete, an allowlisted key at the System level is allowed without a user
///   (the setup wizard runs before any user exists).</item>
///   <item>No signed-in user: <see cref="SettingsAuthorizationResult.Unauthenticated"/>.</item>
///   <item>SuperAdmin: allowed everywhere.</item>
///   <item>Tenant level: allowed only for the current tenant, and only with <c>settings.manage.tenant</c>
///   (TenantAdmin holds it through the permission bypass).</item>
///   <item>Levels below Tenant: the owning tenant comes from <see cref="ISettingScopeOwnerResolver"/>,
///   then the Tenant rule applies. With no resolver, or no answer, the call is denied.</item>
///   <item>System level and every other level: SuperAdmin only.</item>
/// </list>
/// The method never logs a setting value.
/// </summary>
public sealed class SettingsAuthorizer : ISettingsAuthorizer
{
    private const string SystemLevelName = "System";
    private const string TenantLevelName = "Tenant";

    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IPermissionService _permissionService;
    private readonly IBootstrapStateService? _bootstrapState;
    private readonly ILogger<SettingsAuthorizer> _logger;
    private readonly ISettingScopeOwnerResolver? _ownerResolver;

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsAuthorizer"/>.
    /// </summary>
    /// <param name="currentUser">The signed-in user.</param>
    /// <param name="tenantContext">The current tenant.</param>
    /// <param name="permissionService">Role and permission checks (includes the admin bypass).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="bootstrapState">Tells whether setup is complete. Optional: when the bootstrap
    /// module is not registered there is no setup phase, so the setup exception never applies.</param>
    /// <param name="ownerResolver">Optional resolver for levels below Tenant.</param>
    public SettingsAuthorizer(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IPermissionService permissionService,
        ILogger<SettingsAuthorizer> logger,
        IBootstrapStateService? bootstrapState = null,
        ISettingScopeOwnerResolver? ownerResolver = null)
    {
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _permissionService = permissionService;
        _bootstrapState = bootstrapState;
        _logger = logger;
        _ownerResolver = ownerResolver;
    }

    /// <inheritdoc />
    public Task<SettingsAuthorizationResult> AuthorizeWriteAsync(
        string key, Guid levelId, string? levelName, Guid? scopeId,
        CancellationToken cancellationToken = default)
        => AuthorizeAsync("write", key, levelId, levelName, scopeId, allowSetupException: true, cancellationToken);

    /// <inheritdoc />
    public Task<SettingsAuthorizationResult> AuthorizeDeleteAsync(
        string key, Guid levelId, string? levelName, Guid? scopeId,
        CancellationToken cancellationToken = default)
        => AuthorizeAsync("delete", key, levelId, levelName, scopeId, allowSetupException: false, cancellationToken);

    private async Task<SettingsAuthorizationResult> AuthorizeAsync(
        string action, string key, Guid levelId, string? levelName, Guid? scopeId,
        bool allowSetupException, CancellationToken cancellationToken)
    {
        // 1. Setup exception: allowlisted key, System level, setup not complete. No user needed.
        if (allowSetupException
            && IsLevel(levelName, SystemLevelName)
            && scopeId is null
            && SettingsSetupAllowlist.Contains(key)
            && !await IsSetupCompleteAsync(cancellationToken))
        {
            return SettingsAuthorizationResult.Allowed;
        }

        // 2. Signed-in user required
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
        {
            return SettingsAuthorizationResult.Unauthenticated;
        }

        // 3. SuperAdmin may do everything
        if (await _permissionService.HasSystemRoleAsync(userId, AuthRoleNames.SuperAdmin, cancellationToken))
        {
            return SettingsAuthorizationResult.Allowed;
        }

        // 4. Find the tenant that owns the scope (Tenant level: the scope itself)
        var ownerTenantId = await GetOwnerTenantIdAsync(levelId, levelName, scopeId, cancellationToken);

        // 5. Tenant rule: current tenant only, with settings.manage.tenant
        if (ownerTenantId is { } owner
            && owner != Guid.Empty
            && owner == _tenantContext.TenantId
            && await _permissionService.HasPermissionAsync(userId, PermissionKeysAuth.SettingsManageTenant, cancellationToken))
        {
            return SettingsAuthorizationResult.Allowed;
        }

        _logger.LogWarning(
            "Settings {Action} denied for user {UserId} on key {SettingKey} at level {LevelName} (scope {ScopeId})",
            action, userId, key, levelName, scopeId);

        return SettingsAuthorizationResult.Forbidden;
    }

    private async Task<Guid?> GetOwnerTenantIdAsync(
        Guid levelId, string? levelName, Guid? scopeId, CancellationToken cancellationToken)
    {
        if (IsLevel(levelName, SystemLevelName) || scopeId is null)
        {
            return null;
        }

        if (IsLevel(levelName, TenantLevelName))
        {
            return scopeId;
        }

        return _ownerResolver is null
            ? null
            : await _ownerResolver.GetOwningTenantIdAsync(levelId, levelName, scopeId.Value, cancellationToken);
    }

    private async Task<bool> IsSetupCompleteAsync(CancellationToken cancellationToken)
    {
        if (_bootstrapState is null)
        {
            return true;
        }

        try
        {
            return await _bootstrapState.IsCompleteAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cannot tell: treat setup as complete so the normal rules apply (fail closed).
            _logger.LogWarning(ex, "Could not read the bootstrap state; the setup exception for settings is not applied.");
            return true;
        }
    }

    private static bool IsLevel(string? levelName, string expected)
        => string.Equals(levelName, expected, StringComparison.OrdinalIgnoreCase);
}
