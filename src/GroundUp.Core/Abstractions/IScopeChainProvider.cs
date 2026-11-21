using GroundUp.Core.Models;

namespace GroundUp.Core.Abstractions;

/// <summary>
/// Builds a scope chain from the current request context. The default implementation
/// builds Tenant, then System. Consuming applications override this for complex hierarchies
/// (e.g., User → Group → Case → Tenant → System).
/// <para>
/// <b>Order contract:</b> the chain is ordered most specific first. The settings service takes
/// the first level that has a value, so a value set at a more specific level wins, and each
/// parent level acts as the default for the levels below it. Implementations must never put a
/// parent level before one of its children.
/// </para>
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
