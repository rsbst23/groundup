namespace GroundUp.Data.Abstractions;

/// <summary>
/// Applies pending schema migrations to the application database. Each database provider
/// (for example <c>GroundUp.Data.Postgres</c>) registers its own implementation, so callers such as
/// the startup migration hosted service need no EF Core or provider dependency.
/// </summary>
public interface IDatabaseMigrator
{
    /// <summary>
    /// Applies all pending migrations. Completes when the database schema is up to date.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
