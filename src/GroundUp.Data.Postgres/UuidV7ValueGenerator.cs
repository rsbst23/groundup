using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace GroundUp.Data.Postgres;

/// <summary>
/// EF Core value generator that produces UUID v7 values using the UUIDNext package.
/// Configured as the default value generator for BaseEntity.Id properties.
/// Generates sequential, sortable identifiers suitable for Postgres primary keys.
/// </summary>
public sealed class UuidV7ValueGenerator : ValueGenerator<Guid>
{
    /// <summary>
    /// Returns false because UUID v7 values are permanent, not temporary.
    /// </summary>
    public override bool GeneratesTemporaryValues => false;

    /// <summary>
    /// Generates a new UUID v7 value optimized for PostgreSQL.
    /// </summary>
    public override Guid Next(EntityEntry entry)
        => UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql);
}
