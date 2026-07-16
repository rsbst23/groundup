using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Data.Postgres;

/// <summary>
/// EF Core-based <see cref="IUnitOfWork"/> implementation using <see cref="AuthDbContext"/>.
/// Wraps the delegate in a database transaction. Commits on success, rolls back on failure.
/// Unique constraint violations (Postgres error code 23505) are caught and returned as
/// conflict (409) results rather than thrown — the caller is expected to handle retries.
/// </summary>
public sealed class AuthDbContextUnitOfWork : IUnitOfWork
{
    private readonly AuthDbContext _context;
    private readonly ILogger<AuthDbContextUnitOfWork> _logger;

    public AuthDbContextUnitOfWork(AuthDbContext context, ILogger<AuthDbContextUnitOfWork> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            try
            {
                await operation(ct);
                await transaction.CommitAsync(ct);
                return OperationResult.Ok();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _logger.LogDebug(ex, "Unique constraint violation during transactional operation");
                await transaction.RollbackAsync(ct);
                return OperationResult.Fail("Unique constraint violation", 409);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception during transactional operation, rolling back");
                await transaction.RollbackAsync(ct);
                throw;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Detects Postgres unique constraint violations (error code 23505).
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // Check the inner exception for Postgres error code
        var inner = ex.InnerException;
        if (inner is null)
        {
            return false;
        }

        // Npgsql throws PostgresException with SqlState = "23505"
        var message = inner.Message;
        return message.Contains("23505") ||
               message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}
