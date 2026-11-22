using GroundUp.Core.Abstractions;

namespace GroundUp.Tests.Integration.Settings;

/// <summary>
/// Signed-in SuperAdmin for the settings CRUD and cascade tests. Those tests cover settings
/// behavior, not authorization, so every request runs as a user who passes every check
/// (together with <see cref="SettingsTestGrantAllPermissions"/>).
/// Authorization itself is covered by <c>SettingsAuthorizationIntegrationTests</c>.
/// </summary>
internal sealed class SettingsTestIdentity : ICurrentUser
{
    public static readonly Guid Id = Guid.Parse("0193a000-0000-7000-8000-00000000a001");

    public Guid UserId => Id;

    public string? Email => "settings-test-admin@example.com";

    public string? DisplayName => "Settings Test Admin";
}
