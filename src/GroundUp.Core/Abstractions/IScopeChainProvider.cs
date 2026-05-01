using GroundUp.Core.Models;

namespace GroundUp.Core.Abstractions;

/// <summary>
/// Builds a scope chain from the current request context. The default implementation
/// creates a single-entry chain from <c>ITenantContext</c>. Consuming applications override
/// this for complex hierarchies (e.g., User → Team → Tenant → System).
/// </summary>
public interface IScopeChainProvider
{
    /// <summary>
    /// Builds the scope chain for the current request context, ordered from
    /// most specific to least specific. The settings service walks this chain
    /// to resolve the effective value for each setting.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An ordered list of scope entries from most specific to least specific.
    /// An empty list causes resolution to fall back to definition defaults.
    /// </returns>
    Task<IReadOnlyList<SettingScopeEntry>> GetScopeChainAsync(
        CancellationToken cancellationToken = default);
}
