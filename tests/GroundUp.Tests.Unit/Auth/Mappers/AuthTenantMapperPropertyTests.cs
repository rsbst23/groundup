using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Repositories.Mappers;

namespace GroundUp.Tests.Unit.Auth.Mappers;

/// <summary>
/// Property-based tests for <see cref="AuthTenantMapper"/> round-trip correctness.
/// Validates: Requirements 13.2
/// </summary>
public sealed class AuthTenantMapperPropertyTests
{
    /// <summary>
    /// Property 1: Mapper round-trip preserves entity fields.
    /// For any Tenant with valid field values, ToDto(entity) produces a TenantDto with matching
    /// Id, Name, Slug, TenantType, OnboardingMode, ParentTenantId, RealmName, CustomDomain, IsActive.
    /// Then ToEntity(dto) produces a Tenant with matching fields.
    /// **Validates: Requirements 13.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RoundTrip_PreservesAllDtoFields(
        Guid id,
        NonNull<string> name,
        NonNull<string> slug,
        TenantType tenantType,
        OnboardingMode onboardingMode,
        Guid? parentTenantId,
        string? realmName,
        string? customDomain,
        bool isActive)
    {
        var entity = new Tenant
        {
            Id = id,
            Name = name.Get,
            Slug = slug.Get,
            TenantType = tenantType,
            OnboardingMode = onboardingMode,
            ParentTenantId = parentTenantId,
            RealmName = realmName,
            CustomDomain = customDomain,
            IsActive = isActive
        };

        var dto = AuthTenantMapper.ToDto(entity);
        var roundTripped = AuthTenantMapper.ToEntity(dto);

        return (dto.Id == id
            && dto.Name == name.Get
            && dto.Slug == slug.Get
            && dto.TenantType == tenantType
            && dto.OnboardingMode == onboardingMode
            && dto.ParentTenantId == parentTenantId
            && dto.RealmName == realmName
            && dto.CustomDomain == customDomain
            && dto.IsActive == isActive
            && roundTripped.Id == id
            && roundTripped.Name == name.Get
            && roundTripped.Slug == slug.Get
            && roundTripped.TenantType == tenantType
            && roundTripped.OnboardingMode == onboardingMode
            && roundTripped.ParentTenantId == parentTenantId
            && roundTripped.RealmName == realmName
            && roundTripped.CustomDomain == customDomain
            && roundTripped.IsActive == isActive)
            .ToProperty();
    }
}
