using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Core.Abstractions;

/// <summary>
/// Service interface for all settings operations — resolving effective values,
/// setting overrides, retrieving all settings for a scope, and deleting overrides.
/// The caller provides a scope chain (ordered from most specific to least specific)
/// and the service resolves the effective value using cascade resolution.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Resolves a single setting to a typed value by walking the scope chain
    /// from most specific to least specific, returning the first match or the default.
    /// </summary>
    /// <typeparam name="T">The CLR type to deserialize the setting value into.</typeparam>
    /// <param name="key">The setting definition key.</param>
    /// <param name="scopeChain">
    /// Ordered list of scope entries from most specific to least specific.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="OperationResult{T}"/> containing the typed value on success,
    /// or a failure result if the key is not found or the value cannot be converted.
    /// </returns>
    Task<OperationResult<T>> GetAsync<T>(
        string key,
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a setting value at a specific level and scope.
    /// Validates the value against the definition's rules before persisting.
    /// </summary>
    /// <param name="key">The setting definition key.</param>
    /// <param name="value">The serialized value to store.</param>
    /// <param name="levelId">The cascade level to set the value at.</param>
    /// <param name="scopeId">
    /// The specific entity at the level (e.g., TenantId). Null for system level.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="OperationResult{T}"/> containing the created or updated
    /// <see cref="SettingValueDto"/> on success, or a failure result if validation fails.
    /// </returns>
    Task<OperationResult<SettingValueDto>> SetAsync(
        string key,
        string value,
        Guid levelId,
        Guid? scopeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves all settings with their effective values for the given scope chain.
    /// Secret values are masked in the response.
    /// </summary>
    /// <param name="scopeChain">
    /// Ordered list of scope entries from most specific to least specific.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="OperationResult{T}"/> containing a list of
    /// <see cref="ResolvedSettingDto"/> ordered by display order.
    /// </returns>
    Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> GetAllForScopeAsync(
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves all settings in a specific group with their effective values
    /// for the given scope chain. Secret values are masked in the response.
    /// </summary>
    /// <param name="groupKey">The setting group key.</param>
    /// <param name="scopeChain">
    /// Ordered list of scope entries from most specific to least specific.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="OperationResult{T}"/> containing a list of
    /// <see cref="ResolvedSettingDto"/> for the group, ordered by display order.
    /// </returns>
    Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> GetGroupAsync(
        string groupKey,
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a setting value override, causing the setting to revert to the
    /// next value in the cascade chain or the definition default.
    /// </summary>
    /// <param name="settingValueId">The unique identifier of the setting value to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="OperationResult"/> indicating success or failure.
    /// Returns NotFound if the setting value does not exist.
    /// </returns>
    Task<OperationResult> DeleteValueAsync(
        Guid settingValueId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a single setting to a typed value using the scope chain
    /// from <see cref="IScopeChainProvider"/>. Convenience overload that
    /// avoids the caller needing to build a scope chain manually.
    /// </summary>
    /// <typeparam name="T">The CLR type to deserialize the setting value into.</typeparam>
    /// <param name="key">The setting definition key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="OperationResult{T}"/> containing the typed value on success,
    /// or a failure result if the key is not found or the value cannot be converted.
    /// </returns>
    Task<OperationResult<T>> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves all settings with their effective values using the scope chain
    /// from <see cref="IScopeChainProvider"/>. Convenience overload that
    /// avoids the caller needing to build a scope chain manually.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="OperationResult{T}"/> containing a list of
    /// <see cref="ResolvedSettingDto"/> ordered by display order.
    /// </returns>
    Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> GetAllForScopeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves all settings in a specific group with their effective values
    /// using the scope chain from <see cref="IScopeChainProvider"/>. Convenience
    /// overload that avoids the caller needing to build a scope chain manually.
    /// </summary>
    /// <param name="groupKey">The setting group key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="OperationResult{T}"/> containing a list of
    /// <see cref="ResolvedSettingDto"/> for the group, ordered by display order.
    /// </returns>
    Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> GetGroupAsync(
        string groupKey,
        CancellationToken cancellationToken = default);
}
