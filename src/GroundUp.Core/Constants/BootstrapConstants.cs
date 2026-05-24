namespace GroundUp.Core.Constants;

/// <summary>
/// Constants used during bootstrap/setup mode when no authenticated user exists.
/// Shared between <c>SetupCurrentUser</c> (in GroundUp.Services) and the
/// <c>AuditableInterceptor</c> (in GroundUp.Data.Postgres) so that the interceptor
/// can recognize the sentinel UserId and write the human-readable sentinel string.
/// </summary>
public static class BootstrapConstants
{
    /// <summary>
    /// The literal sentinel string persisted to CreatedBy/UpdatedBy during setup.
    /// </summary>
    public const string SetupWizardSentinel = "setup-wizard";

    /// <summary>
    /// A deterministic Guid used to satisfy the <c>ICurrentUser.UserId</c> contract during setup.
    /// When the <c>AuditableInterceptor</c> sees this UserId, it writes
    /// <see cref="SetupWizardSentinel"/> instead of the Guid string.
    /// </summary>
    public static readonly Guid SetupSentinelUserId = new("00000000-0000-0000-0000-00000000ABCD");
}
