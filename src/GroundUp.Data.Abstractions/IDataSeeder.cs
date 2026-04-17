namespace GroundUp.Data.Abstractions;

/// <summary>
/// Interface for reference data seeding on application startup.
/// Implementations must be idempotent — calling <see cref="SeedAsync"/> multiple times
/// produces the same result as calling it once.
/// </summary>
public interface IDataSeeder
{
    /// <summary>
    /// Execution order for this seeder. Lower values execute first.
    /// Use this to control dependencies between seeders (e.g., roles before users).
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Seeds reference data. Must be idempotent.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
