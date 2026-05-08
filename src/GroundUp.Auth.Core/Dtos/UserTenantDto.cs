namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of a user-tenant membership for API responses.
/// </summary>
/// <param name="Id">The unique identifier of the user-tenant record.</param>
/// <param name="UserId">The user identifier.</param>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="ExternalUserId">The identity provider user identifier specific to this tenant.</param>
/// <param name="IsActive">Whether this tenant membership is active.</param>
public record UserTenantDto(
    Guid Id,
    Guid UserId,
    Guid TenantId,
    string ExternalUserId,
    bool IsActive);
