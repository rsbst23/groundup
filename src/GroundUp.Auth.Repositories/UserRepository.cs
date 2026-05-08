using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Repositories.Mappers;
using GroundUp.Core.Results;
using GroundUp.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Repositories;

/// <summary>
/// Repository implementation for User entities. Extends BaseRepository with
/// custom lookup methods by external user ID and email address.
/// </summary>
public sealed class UserRepository : BaseRepository<User, UserDto>, IUserRepository
{
    /// <summary>
    /// Initializes a new instance of <see cref="UserRepository"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    public UserRepository(DbContext context)
        : base(context, AuthUserMapper.ToDto, AuthUserMapper.ToEntity)
    {
    }

    /// <inheritdoc />
    public async Task<OperationResult<UserDto>> GetByExternalUserIdAsync(
        string externalUserId,
        CancellationToken cancellationToken = default)
    {
        var entity = await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(u => u.ExternalUserId == externalUserId, cancellationToken);

        if (entity is null)
            return OperationResult<UserDto>.NotFound();

        return OperationResult<UserDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult<UserDto>> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.ToLower();

        var entity = await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (entity is null)
            return OperationResult<UserDto>.NotFound();

        return OperationResult<UserDto>.Ok(MapToDto(entity));
    }
}
