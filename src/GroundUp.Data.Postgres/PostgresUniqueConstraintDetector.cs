using GroundUp.Data.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Data.Postgres;

/// <summary>
/// Postgres <see cref="IUniqueConstraintDetector"/>: SQL state 23505 (unique_violation), using
/// <see cref="PostgresErrorHelper"/>.
/// </summary>
public sealed class PostgresUniqueConstraintDetector : IUniqueConstraintDetector
{
    /// <inheritdoc />
    public bool IsUniqueConstraintViolation(Exception exception)
        => PostgresErrorHelper.IsUniqueConstraintViolation(exception as DbUpdateException);
}
