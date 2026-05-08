using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Repositories.Mappers;

namespace GroundUp.Tests.Unit.Auth.Mappers;

/// <summary>
/// Property-based tests for <see cref="AuthRoleMapper"/> round-trip correctness.
/// Validates: Requirements 13.3
/// </summary>
public sealed class AuthRoleMapperPropertyTests
{
    /// <summary>
    /// Property 1: Mapper round-trip preserves entity fields.
    /// For any Role with valid field values, ToDto(entity) produces a RoleDto with matching
    /// Id, Name, Description, RoleType, TenantId, IsSystem. Then ToEntity(dto) produces a Role
    /// with matching fields.
    /// **Validates: Requirements 13.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RoundTrip_PreservesAllDtoFields(
        Guid id,
        NonNull<string> name,
        string? description,
        RoleType roleType,
        Guid tenantId,
        bool isSystem)
    {
        var entity = new Role
        {
            Id = id,
            Name = name.Get,
            Description = description,
            RoleType = roleType,
            TenantId = tenantId,
            IsSystem = isSystem
        };

        var dto = AuthRoleMapper.ToDto(entity);
        var roundTripped = AuthRoleMapper.ToEntity(dto);

        return (dto.Id == id
            && dto.Name == name.Get
            && dto.Description == description
            && dto.RoleType == roleType
            && dto.TenantId == tenantId
            && dto.IsSystem == isSystem
            && roundTripped.Id == id
            && roundTripped.Name == name.Get
            && roundTripped.Description == description
            && roundTripped.RoleType == roleType
            && roundTripped.TenantId == tenantId
            && roundTripped.IsSystem == isSystem)
            .ToProperty();
    }
}
