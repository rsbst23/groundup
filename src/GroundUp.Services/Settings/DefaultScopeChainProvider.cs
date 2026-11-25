using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Data.Abstractions;

namespace GroundUp.Services.Settings;

/// <summary>
/// Default implementation of <see cref="IScopeChainProvider"/>. Builds the chain Tenant, then
/// System (most specific first) from <see cref="ITenantContext"/>. The Tenant entry is included
/// only when a tenant context is active; the System entry is always included when the level exists.
/// <para>
/// Consuming applications override this for complex hierarchies
/// (e.g., User → Team → Tenant → System) by registering their own
/// <see cref="IScopeChainProvider"/> implementation.
/// </para>
/// </summary>
public sealed class DefaultScopeChainProvider : IScopeChainProvider
{
    private readonly ITenantContext _tenantContext;
    private readonly ISettingsRepository _settingsRepository;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultScopeChainProvider"/>.
    /// </summary>
    /// <param name="tenantContext">Provides the current tenant identity.</param>
    /// <param name="settingsRepository">Looks up setting levels by name.</param>
    public DefaultScopeChainProvider(ITenantContext tenantContext, ISettingsRepository settingsRepository)
    {
        _tenantContext = tenantContext;
        _settingsRepository = settingsRepository;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingScopeEntry>> GetScopeChainAsync(
        CancellationToken cancellationToken = default)
    {
        // The chain is ordered most specific first (see IScopeChainProvider): Tenant, then System.
        var chain = new List<SettingScopeEntry>();

        // Include the Tenant level only when a tenant context is active
        if (_tenantContext.TenantId != Guid.Empty)
        {
            var tenantLevelId = await _settingsRepository.GetLevelIdByNameAsync(
                DefaultSettingLevelSeeder.TenantLevelName, cancellationToken);

            if (tenantLevelId is not null)
            {
                chain.Add(new SettingScopeEntry(tenantLevelId.Value, _tenantContext.TenantId));
            }
        }

        // Always include the System level last (scope = null for system-wide settings)
        var systemLevelId = await _settingsRepository.GetLevelIdByNameAsync(
            DefaultSettingLevelSeeder.SystemLevelName, cancellationToken);

        if (systemLevelId is not null)
        {
            chain.Add(new SettingScopeEntry(systemLevelId.Value, null));
        }

        return chain;
    }
}
