using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of a tenant entity for API responses.
/// </summary>
/// <param name="Id">The unique identifier of the tenant.</param>
/// <param name="Name">The tenant's display name.</param>
/// <param name="Slug">The URL-friendly identifier for the tenant.</param>
/// <param name="TenantType">The type of tenant (Standard or Enterprise).</param>
/// <param name="OnboardingMode">How users join this tenant.</param>
/// <param name="ParentTenantId">The parent tenant identifier for hierarchical relationships.</param>
/// <param name="RealmName">The IdP realm name for enterprise SSO routing.</param>
/// <param name="IsActive">Whether the tenant is active.</param>
public record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    TenantType TenantType,
    OnboardingMode OnboardingMode,
    Guid? ParentTenantId,
    string? RealmName,
    bool IsActive);
