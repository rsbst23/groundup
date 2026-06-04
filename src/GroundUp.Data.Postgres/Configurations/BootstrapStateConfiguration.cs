using GroundUp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="BootstrapState"/> entity.
/// Enforces the singleton invariant via a CHECK constraint and unique index on Id.
/// Configures the Postgres xmin system column as an optimistic concurrency token.
/// </summary>
public sealed class BootstrapStateConfiguration : IEntityTypeConfiguration<BootstrapState>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BootstrapState> builder)
    {
        builder.ToTable("BootstrapState", t =>
        {
            t.HasCheckConstraint(
                "CK_BootstrapState_Singleton",
                "\"Id\" = '00000000-0000-0000-0000-000000000001'");
        });

        // ── Singleton invariant ───────────────────────────────
        builder.HasIndex(e => e.Id)
            .IsUnique();

        // ── xmin concurrency token ────────────────────────────
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();

        // ── Properties ────────────────────────────────────────
        builder.Property(e => e.IsComplete)
            .HasDefaultValue(false);

        // ── IAuditable columns ────────────────────────────────
        builder.Property(e => e.CreatedAt);

        builder.Property(e => e.CreatedBy)
            .HasMaxLength(256);

        builder.Property(e => e.UpdatedAt);

        builder.Property(e => e.UpdatedBy)
            .HasMaxLength(256);
    }
}
