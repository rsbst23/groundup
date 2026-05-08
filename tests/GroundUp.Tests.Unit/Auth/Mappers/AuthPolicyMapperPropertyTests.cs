using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Repositories.Mappers;

namespace GroundUp.Tests.Unit.Auth.Mappers;

/// <summary>
/// Property-based tests for <see cref="AuthPolicyMapper"/> round-trip correctness.
/// Validates: Requirements 13.4
/// </summary>
public sealed class AuthPolicyMapperPropertyTests
{
    /// <summary>
    /// Property 1: Mapper round-trip preserves entity fields.
    /// For any Policy with valid field values, ToDto(entity) produces a PolicyDto with matching
    /// Id, Name, Description, TenantId. Then ToEntity(dto) produces a Policy with matching fields.
    /// **Validates: Requirements 13.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RoundTrip_PreservesAllDtoFields(
        Guid id,
        NonNull<string> name,
        string? description,
        Guid tenantId)
    {
        var entity = new Policy
        {
            Id = id,
            Name = name.Get,
            Description = description,
            TenantId = tenantId
        };

        var dto = AuthPolicyMapper.ToDto(entity);
        var roundTripped = AuthPolicyMapper.ToEntity(dto);

        return (dto.Id == id
            && dto.Name == name.Get
            && dto.Description == description
            && dto.TenantId == tenantId
            && roundTripped.Id == id
            && roundTripped.Name == name.Get
            && roundTripped.Description == description
            && roundTripped.TenantId == tenantId)
            .ToProperty();
    }
}
