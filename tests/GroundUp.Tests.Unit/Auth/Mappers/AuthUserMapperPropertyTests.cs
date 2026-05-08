using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Repositories.Mappers;

namespace GroundUp.Tests.Unit.Auth.Mappers;

/// <summary>
/// Property-based tests for <see cref="AuthUserMapper"/> round-trip correctness.
/// Validates: Requirements 13.1
/// </summary>
public sealed class AuthUserMapperPropertyTests
{
    /// <summary>
    /// Property 1: Mapper round-trip preserves entity fields.
    /// For any User with valid field values, ToDto(entity) produces a UserDto with matching
    /// Id, ExternalUserId, Email, DisplayName, IsActive. Then ToEntity(dto) produces a User
    /// with matching fields.
    /// **Validates: Requirements 13.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RoundTrip_PreservesAllDtoFields(
        Guid id,
        NonNull<string> externalUserId,
        NonNull<string> email,
        NonNull<string> displayName,
        bool isActive)
    {
        var entity = new User
        {
            Id = id,
            ExternalUserId = externalUserId.Get,
            Email = email.Get,
            DisplayName = displayName.Get,
            IsActive = isActive
        };

        var dto = AuthUserMapper.ToDto(entity);
        var roundTripped = AuthUserMapper.ToEntity(dto);

        return (dto.Id == id
            && dto.ExternalUserId == externalUserId.Get
            && dto.Email == email.Get
            && dto.DisplayName == displayName.Get
            && dto.IsActive == isActive
            && roundTripped.Id == id
            && roundTripped.ExternalUserId == externalUserId.Get
            && roundTripped.Email == email.Get
            && roundTripped.DisplayName == displayName.Get
            && roundTripped.IsActive == isActive)
            .ToProperty();
    }
}
