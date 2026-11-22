using GroundUp.Core.Enums;

namespace GroundUp.Core.Abstractions;

/// <summary>
/// Decides whether the current caller may write or delete a setting value at a given level and
/// scope. These rules depend on the call's arguments, so they cannot be expressed with
/// <c>[RequiresPermission]</c>, which decides from the method name alone.
/// <para>
/// <see cref="ISettingsService"/> calls this before changing anything. The default
/// implementation allows everything, so applications without the auth module keep working.
/// The auth module registers an implementation that enforces roles and permissions.
/// A future module (for example object-level security) can replace it.
/// </para>
/// </summary>
public interface ISettingsAuthorizer
{
    /// <summary>
    /// Checks whether the caller may write a value for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="levelId">The level the value is written at.</param>
    /// <param name="levelName">The name of that level (for example "System" or "Tenant").</param>
    /// <param name="scopeId">The scope the value applies to (tenant ID, user ID, or null for System).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the write is allowed.</returns>
    Task<SettingsAuthorizationResult> AuthorizeWriteAsync(
        string key,
        Guid levelId,
        string? levelName,
        Guid? scopeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the caller may delete the stored value for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="levelId">The level of the stored value.</param>
    /// <param name="levelName">The name of that level.</param>
    /// <param name="scopeId">The scope of the stored value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the delete is allowed.</returns>
    Task<SettingsAuthorizationResult> AuthorizeDeleteAsync(
        string key,
        Guid levelId,
        string? levelName,
        Guid? scopeId,
        CancellationToken cancellationToken = default);
}
