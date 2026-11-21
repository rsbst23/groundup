using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Models;
using GroundUp.Data.Postgres;
using Microsoft.EntityFrameworkCore;

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
    private readonly GroundUpDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultScopeChainProvider"/>.
    /// </summary>
    /// <param name="tenantContext">Provides the current tenant identity.</param>
    /// <param name="dbContext">The EF Core database context for querying setting levels.</param>
    public DefaultScopeChainProvider(ITenantContext tenantContext, GroundUpDbContext dbContext)
    {
        _tenantContext = tenantContext;
        _dbContext = dbContext;
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
            var tenantLevel = await _dbContext.Set<SettingLevel>()
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Name == "Tenant", cancellationToken);

            if (tenantLevel is not null)
            {
                chain.Add(new SettingScopeEntry(tenantLevel.Id, _tenantContext.TenantId));
            }
        }

        // Always include the System level last (scope = null for system-wide settings)
        var systemLevel = await _dbContext.Set<SettingLevel>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Name == "System", cancellationToken);

        if (systemLevel is not null)
        {
            chain.Add(new SettingScopeEntry(systemLevel.Id, null));
        }

        return chain;
    }
}
