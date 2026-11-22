namespace GroundUp.Core.Abstractions;

/// <summary>
/// Optional extension point for applications that add setting levels below Tenant (for example
/// Group, Case or User). Maps a scope at such a level to the tenant that owns it, so the
/// settings authorizer can apply the tenant rule to it. With no resolver registered, levels
/// below Tenant can be changed by a SuperAdmin only.
/// </summary>
public interface ISettingScopeOwnerResolver
{
    /// <summary>
    /// Returns the id of the tenant that owns the scope, or <c>null</c> when this resolver
    /// does not know the level or the scope. A <c>null</c> answer denies the operation.
    /// </summary>
    /// <param name="levelId">The level of the scope.</param>
    /// <param name="levelName">The name of that level.</param>
    /// <param name="scopeId">The scope (for example a group id or user id).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The owning tenant id, or <c>null</c>.</returns>
    Task<Guid?> GetOwningTenantIdAsync(
        Guid levelId,
        string? levelName,
        Guid scopeId,
        CancellationToken cancellationToken = default);
}
