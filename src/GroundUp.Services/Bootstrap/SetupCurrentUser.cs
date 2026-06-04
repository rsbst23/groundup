using GroundUp.Core.Abstractions;
using GroundUp.Core.Constants;

namespace GroundUp.Services.Bootstrap;

/// <summary>
/// <see cref="ICurrentUser"/> implementation used during setup mode when no authenticated user exists.
/// Provides the "setup-wizard" sentinel identity so that the <c>AuditableInterceptor</c>
/// can populate <c>CreatedBy</c>/<c>UpdatedBy</c> fields during setup.
/// </summary>
public sealed class SetupCurrentUser : ICurrentUser
{
    /// <summary>The literal sentinel string persisted to CreatedBy/UpdatedBy during setup.</summary>
    public const string Sentinel = BootstrapConstants.SetupWizardSentinel;

    /// <summary>A deterministic Guid used to satisfy the ICurrentUser.UserId contract during setup.</summary>
    public static readonly Guid SetupSentinelUserId = BootstrapConstants.SetupSentinelUserId;

    /// <inheritdoc />
    public Guid UserId => SetupSentinelUserId;

    /// <inheritdoc />
    public string? Email => null;

    /// <inheritdoc />
    public string? DisplayName => Sentinel;
}
