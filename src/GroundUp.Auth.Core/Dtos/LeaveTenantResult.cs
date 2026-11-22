namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Result of leaving a tenant.
/// </summary>
/// <param name="CurrentTenantId">The session's current tenant after the leave, or null when the session
/// is (now) tenantless.</param>
/// <param name="SessionEnded">True when the user left the tenant their session was scoped to: the old
/// tenant-scoped token must not be kept by the client.</param>
/// <param name="SessionToken">The new tenantless token when <paramref name="SessionEnded"/> is true and
/// it could be issued; otherwise null. Never log this value.</param>
public sealed record LeaveTenantResult(
    Guid? CurrentTenantId,
    bool SessionEnded,
    string? SessionToken);
