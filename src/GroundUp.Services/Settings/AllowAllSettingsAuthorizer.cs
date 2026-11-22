using GroundUp.Core.Abstractions;
using GroundUp.Core.Enums;

namespace GroundUp.Services.Settings;

/// <summary>
/// Default <see cref="ISettingsAuthorizer"/> that allows every operation. It applies only when
/// no auth module is registered (the settings module must work without users or permissions).
/// The auth module registers an enforcing implementation that replaces this one.
/// </summary>
public sealed class AllowAllSettingsAuthorizer : ISettingsAuthorizer
{
    /// <inheritdoc />
    public Task<SettingsAuthorizationResult> AuthorizeWriteAsync(
        string key, Guid levelId, string? levelName, Guid? scopeId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(SettingsAuthorizationResult.Allowed);

    /// <inheritdoc />
    public Task<SettingsAuthorizationResult> AuthorizeDeleteAsync(
        string key, Guid levelId, string? levelName, Guid? scopeId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(SettingsAuthorizationResult.Allowed);
}
