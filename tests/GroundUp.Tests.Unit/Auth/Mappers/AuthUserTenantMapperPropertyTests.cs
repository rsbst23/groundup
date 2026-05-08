using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Repositories.Mappers;

namespace GroundUp.Tests.Unit.Auth.Mappers;

/// <summary>
/// Property-based tests for <see cref="AuthUserTenantMapper"/> round-trip correctness.
/// Validates: Requirements 13.6
/// </summary>
public sealed class AuthUserTenantMapperPropertyTests
{
    /// <summary>
    /// Property 1: Mapper round-trip preserves entity fields.
    /// For any UserTenant with valid field values, ToDto(entity) produces a UserTenantDto with matching
    /// Id, UserId, TenantId, ExternalUserId, IsActive. Then ToEntity(dto) produces a UserTenant
    /// with matching fields.
    /// **Validates: Requirements 13.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RoundTrip_PreservesAllDtoFields(
        Guid id,
        Guid userId,
        Guid tenantId,
        NonNull<string> externalUserId,
        bool isActive)
    {
        var entity = new UserTenant
        {
            Id = id,
            UserId = userId,
            TenantId = tenantId,
            ExternalUserId = externalUserId.Get,
            IsActive = isActive
        };

        var dto = AuthUserTenantMapper.ToDto(entity);
        var roundTripped = AuthUserTenantMapper.ToEntity(dto);

        return (dto.Id == id
            && dto.UserId == userId
            && dto.TenantId == tenantId
            && dto.ExternalUserId == externalUserId.Get
            && dto.IsActive == isActive
            && roundTripped.Id == id
            && roundTripped.UserId == userId
            && roundTripped.TenantId == tenantId
            && roundTripped.ExternalUserId == externalUserId.Get
            && roundTripped.IsActive == isActive)
            .ToProperty();
    }
}
