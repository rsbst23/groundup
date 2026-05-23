using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Input DTO for creating a new tenant.
/// </summary>
/// <param name="Name">The tenant's display name.</param>
/// <param name="Slug">The URL-friendly identifier for the tenant.</param>
/// <param name="TenantType">The type of tenant (Standard or Enterprise).</param>
/// <param name="OnboardingMode">How users join this tenant.</param>
/// <param name="ParentTenantId">The parent tenant identifier for hierarchical relationships.</param>
/// <param name="RealmName">The IdP realm name for enterprise SSO routing.</param>
public record CreateTenantDto(
    string Name,
    string Slug,
    TenantType TenantType,
    OnboardingMode OnboardingMode,
    Guid? ParentTenantId,
    string? RealmName);
