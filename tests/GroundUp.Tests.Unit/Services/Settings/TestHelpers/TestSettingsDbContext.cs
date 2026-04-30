using GroundUp.Data.Postgres;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Services.Settings.TestHelpers;

/// <summary>
/// Concrete DbContext for testing SettingsService with SQLite in-memory provider.
/// Inherits from GroundUpDbContext so all entity configurations are auto-discovered.
/// </summary>
public sealed class TestSettingsDbContext : GroundUpDbContext
{
    public TestSettingsDbContext(DbContextOptions<TestSettingsDbContext> options)
        : base(options)
    {
    }
}
