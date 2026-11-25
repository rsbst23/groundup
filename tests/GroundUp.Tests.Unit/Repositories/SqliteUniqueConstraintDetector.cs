using GroundUp.Data.Abstractions;
using Microsoft.Data.Sqlite;

namespace GroundUp.Tests.Unit.Repositories;

/// <summary>
/// <see cref="IUniqueConstraintDetector"/> for unit tests on SQLite: extended error code 2067
/// (SQLITE_CONSTRAINT_UNIQUE), the counterpart of the Postgres detector's SQL state 23505.
/// </summary>
internal sealed class SqliteUniqueConstraintDetector : IUniqueConstraintDetector
{
    /// <summary>A shared instance (the detector is stateless).</summary>
    public static readonly SqliteUniqueConstraintDetector Instance = new();

    private const int SqliteConstraintUnique = 2067;

    /// <inheritdoc />
    public bool IsUniqueConstraintViolation(Exception exception)
        => exception.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUnique };
}
