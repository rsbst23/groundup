using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Repositories.Mappers;

namespace GroundUp.Tests.Unit.Auth.Mappers;

/// <summary>
/// Property-based tests for <see cref="AuthPermissionMapper"/> round-trip correctness.
/// Validates: Requirements 13.5
/// </summary>
public sealed class AuthPermissionMapperPropertyTests
{
    /// <summary>
    /// Property 1: Mapper round-trip preserves entity fields.
    /// For any Permission with valid field values, ToDto(entity) produces a PermissionDto with matching
    /// Id, Key, Name, Description, Module. Then ToEntity(dto) produces a Permission with matching fields.
    /// **Validates: Requirements 13.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RoundTrip_PreservesAllDtoFields(
        Guid id,
        NonNull<string> key,
        NonNull<string> name,
        string? description,
        NonNull<string> module)
    {
        var entity = new Permission
        {
            Id = id,
            Key = key.Get,
            Name = name.Get,
            Description = description,
            Module = module.Get
        };

        var dto = AuthPermissionMapper.ToDto(entity);
        var roundTripped = AuthPermissionMapper.ToEntity(dto);

        return (dto.Id == id
            && dto.Key == key.Get
            && dto.Name == name.Get
            && dto.Description == description
            && dto.Module == module.Get
            && roundTripped.Id == id
            && roundTripped.Key == key.Get
            && roundTripped.Name == name.Get
            && roundTripped.Description == description
            && roundTripped.Module == module.Get)
            .ToProperty();
    }
}
