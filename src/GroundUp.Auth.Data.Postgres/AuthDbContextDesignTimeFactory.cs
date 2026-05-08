using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GroundUp.Auth.Data.Postgres;

/// <summary>
/// Design-time factory for AuthDbContext. Used by EF Core migration tooling
/// when no startup project is available. The connection string is only used
/// during migration generation and is never used at runtime.
/// </summary>
public sealed class AuthDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    /// <inheritdoc />
    public AuthDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuthDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=groundup_auth;Username=groundup;Password=groundup_dev");
        return new AuthDbContext(optionsBuilder.Options);
    }
}
