using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Repositories.Mappers;
using GroundUp.Core;
using GroundUp.Core.Results;
using GroundUp.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Repositories;

/// <summary>
/// Repository for AuthFlowState entities. Inherits standard CRUD from
/// <see cref="BaseRepository{TEntity, TDto}"/> and adds atomic one-shot
/// consumption, failure marking, and bulk sweeper operations.
/// </summary>
public sealed class AuthFlowStateRepository : BaseRepository<AuthFlowState, AuthFlowStateDto>, IAuthFlowStateRepository
{
    /// <summary>
    /// Initializes a new instance of <see cref="AuthFlowStateRepository"/>.
    /// </summary>
    /// <param name="context">The Auth module's EF Core database context.</param>
    public AuthFlowStateRepository(DbContext context)
        : base(context, AuthFlowStateMapper.ToDto, AuthFlowStateMapper.ToEntity)
    {
    }

    /// <inheritdoc />
    public async Task<OperationResult<AuthFlowStateDto>> MarkConsumedAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Atomic conditional UPDATE — only succeeds if row is Pending and not expired
        var rowsAffected = await DbSet
            .Where(x => x.Id == id && x.Status == FlowStatus.Pending && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, FlowStatus.Consumed)
                .SetProperty(x => x.ConsumedAt, now)
                .SetProperty(x => x.TerminatedAt, now)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);

        if (rowsAffected == 1)
        {
            // Reload the consumed row
            var entity = await DbSet.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (entity is null)
            {
                // Extremely rare: row was deleted between UPDATE and reload (e.g., sweeper with RetentionDays=0)
                return OperationResult<AuthFlowStateDto>.NotFound(
                    $"AuthFlowState '{id}' was consumed but could not be reloaded");
            }

            return OperationResult<AuthFlowStateDto>.Ok(AuthFlowStateMapper.ToDto(entity));
        }

        // Determine failure reason
        var existing = await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (existing is null)
        {
            return OperationResult<AuthFlowStateDto>.NotFound(
                $"AuthFlowState '{id}' not found");
        }

        if (existing.Status != FlowStatus.Pending)
        {
            return OperationResult<AuthFlowStateDto>.Fail(
                $"AuthFlowState '{id}' is already in terminal state '{existing.Status}'",
                409,
                ErrorCodes.Conflict);
        }

        // Must be expired
        return OperationResult<AuthFlowStateDto>.Fail(
            $"AuthFlowState '{id}' has expired",
            400);
    }

    /// <inheritdoc />
    public async Task<OperationResult<AuthFlowStateDto>> MarkFailedAsync(
        Guid id, string reason, CancellationToken cancellationToken = default)
    {
        // Truncate reason to 1024 chars if exceeded
        if (reason.Length > 1024)
        {
            reason = reason[..1024];
        }

        var now = DateTime.UtcNow;

        // Atomic conditional UPDATE: only a row that is still Pending in the database becomes Failed.
        // A read-then-save would trust a possibly stale tracked copy (for example after
        // MarkConsumedAsync, which also bypasses the change tracker) and overwrite a terminal state.
        var rowsAffected = await DbSet
            .Where(x => x.Id == id && x.Status == FlowStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, FlowStatus.Failed)
                .SetProperty(x => x.FailureReason, reason)
                .SetProperty(x => x.TerminatedAt, now)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);

        var current = await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (current is null)
        {
            return OperationResult<AuthFlowStateDto>.NotFound(
                $"AuthFlowState '{id}' not found");
        }

        if (rowsAffected == 0)
        {
            return OperationResult<AuthFlowStateDto>.Fail(
                $"AuthFlowState '{id}' is already in terminal state '{current.Status}'",
                409,
                ErrorCodes.Conflict);
        }

        return OperationResult<AuthFlowStateDto>.Ok(AuthFlowStateMapper.ToDto(current));
    }

    /// <inheritdoc />
    public async Task<OperationResult<int>> MarkExpiredOlderThanAsync(
        DateTime cutoff, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var count = await DbSet
            .Where(x => x.Status == FlowStatus.Pending && x.ExpiresAt <= cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, FlowStatus.Expired)
                .SetProperty(x => x.TerminatedAt, now)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);

        return OperationResult<int>.Ok(count);
    }

    /// <inheritdoc />
    public async Task<OperationResult<int>> DeleteTerminalOlderThanAsync(
        DateTime cutoff, CancellationToken cancellationToken = default)
    {
        var count = await DbSet
            .Where(x => x.Status != FlowStatus.Pending && x.TerminatedAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return OperationResult<int>.Ok(count);
    }

    /// <inheritdoc />
    public async Task<OperationResult<AuthFlowStateDto>> FindByStateTokenAsync(
        string stateToken, CancellationToken cancellationToken = default)
    {
        var entity = await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StateToken == stateToken, cancellationToken);

        if (entity is null)
        {
            return OperationResult<AuthFlowStateDto>.NotFound(
                $"AuthFlowState with StateToken not found");
        }

        return OperationResult<AuthFlowStateDto>.Ok(AuthFlowStateMapper.ToDto(entity));
    }
}
