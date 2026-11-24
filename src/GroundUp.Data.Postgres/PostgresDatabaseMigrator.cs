using GroundUp.Data.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Data.Postgres;

/// <summary>
/// <see cref="IDatabaseMigrator"/> for the application's <see cref="GroundUpDbContext"/>:
/// runs EF Core <c>MigrateAsync</c>.
/// </summary>
public sealed class PostgresDatabaseMigrator : IDatabaseMigrator
{
    private readonly GroundUpDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresDatabaseMigrator"/> class.
    /// </summary>
    /// <param name="context">The application's context (registered as <see cref="GroundUpDbContext"/>).</param>
    public PostgresDatabaseMigrator(GroundUpDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task MigrateAsync(CancellationToken cancellationToken = default)
        => _context.Database.MigrateAsync(cancellationToken);
}
