using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Repositories.Mappers;

namespace GroundUp.Tests.Unit.Auth.Mappers;

/// <summary>
/// Property-based tests for <see cref="AuthUserRoleMapper"/> round-trip correctness.
/// Validates: Requirements 13.6
/// </summary>
public sealed class AuthUserRoleMapperPropertyTests
{
    /// <summary>
    /// Property 1: Mapper round-trip preserves entity fields.
    /// For any UserRole with valid field values, ToDto(entity) produces a UserRoleDto with matching
    /// Id, UserId, RoleId, TenantId. Then ToEntity(dto) produces a UserRole with matching fields.
    /// **Validates: Requirements 13.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RoundTrip_PreservesAllDtoFields(
        Guid id,
        Guid userId,
        Guid roleId,
        Guid tenantId)
    {
        var entity = new UserRole
        {
            Id = id,
            UserId = userId,
            RoleId = roleId,
            TenantId = tenantId
        };

        var dto = AuthUserRoleMapper.ToDto(entity);
        var roundTripped = AuthUserRoleMapper.ToEntity(dto);

        return (dto.Id == id
            && dto.UserId == userId
            && dto.RoleId == roleId
            && dto.TenantId == tenantId
            && roundTripped.Id == id
            && roundTripped.UserId == userId
            && roundTripped.RoleId == roleId
            && roundTripped.TenantId == tenantId)
            .ToProperty();
    }
}
