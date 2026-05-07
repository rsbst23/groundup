using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Input DTO for updating an existing tenant.
/// </summary>
/// <param name="Name">The tenant's display name.</param>
/// <param name="Slug">The URL-friendly identifier for the tenant.</param>
/// <param name="TenantType">The type of tenant (Standard or Enterprise).</param>
/// <param name="OnboardingMode">How users join this tenant.</param>
/// <param name="RealmName">The IdP realm name for enterprise SSO routing.</param>
/// <param name="CustomDomain">The tenant-specific custom domain.</param>
/// <param name="IsActive">Whether the tenant is active.</param>
public record UpdateTenantDto(
    string Name,
    string Slug,
    TenantType TenantType,
    OnboardingMode OnboardingMode,
    string? RealmName,
    string? CustomDomain,
    bool IsActive);
