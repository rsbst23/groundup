using Microsoft.EntityFrameworkCore;

namespace GroundUp.Data.Postgres;

/// <summary>
/// Static helper for detecting Postgres-specific database errors.
/// This is the ONLY place in the framework that references Npgsql types directly.
/// </summary>
public static class PostgresErrorHelper
{
    /// <summary>
    /// Determines whether the specified <see cref="DbUpdateException"/> was caused
    /// by a Postgres unique constraint violation (SqlState 23505).
    /// </summary>
    /// <param name="exception">The DbUpdateException to inspect. May be null.</param>
    /// <returns>True if the inner exception is a PostgresException with SqlState "23505".</returns>
    public static bool IsUniqueConstraintViolation(DbUpdateException? exception)
    {
        if (exception?.InnerException is not Npgsql.PostgresException pgEx)
            return false;

        return pgEx.SqlState == "23505";
    }
}
