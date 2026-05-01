using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Models;
using GroundUp.Data.Postgres;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Services.Settings;

/// <summary>
/// Default implementation of <see cref="IScopeChainProvider"/> that builds a single-entry
/// scope chain from <see cref="ITenantContext"/>. Queries the database for a "Tenant" level
/// and returns a scope chain containing that level's ID paired with the current tenant ID.
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
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return Array.Empty<SettingScopeEntry>();
        }

        var tenantLevel = await _dbContext.Set<SettingLevel>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Name == "Tenant", cancellationToken);

        if (tenantLevel is null)
        {
            return Array.Empty<SettingScopeEntry>();
        }

        return new[] { new SettingScopeEntry(tenantLevel.Id, _tenantContext.TenantId) };
    }
}
