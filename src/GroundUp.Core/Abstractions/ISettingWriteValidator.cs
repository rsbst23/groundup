using GroundUp.Core.Models;

namespace GroundUp.Core.Abstractions;

/// <summary>
/// Optional extension point for rules that depend on more than one value, such as "a tenant
/// value may not exceed the System value". The settings service calls every registered
/// validator from <c>SetAsync</c> after the built-in checks (read-only, allowed levels,
/// required, numeric range, length, regex) and before saving. Modules register their own
/// validators; with none registered, nothing changes.
/// </summary>
public interface ISettingWriteValidator
{
    /// <summary>
    /// Validates a pending write.
    /// </summary>
    /// <param name="context">The pending write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An error message when the write must be rejected (returned to the caller as
    /// a 400 Bad Request), or <c>null</c> when this validator has no objection.</returns>
    Task<string?> ValidateAsync(
        SettingWriteContext context,
        CancellationToken cancellationToken = default);
}
