namespace GroundUp.Auth.Services.Bootstrap;

/// <summary>
/// Result of provisioning the first SuperAdmin user.
/// </summary>
/// <param name="UserId">The database user ID of the provisioned admin.</param>
/// <param name="Email">The email address of the provisioned admin.</param>
/// <param name="AlreadyExisted">Whether the user already existed (idempotent retry).</param>
public sealed record BootstrapAdminResultDto(
    Guid UserId,
    string Email,
    bool AlreadyExisted);
