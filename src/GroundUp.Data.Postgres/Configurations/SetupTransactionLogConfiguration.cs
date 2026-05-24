using GroundUp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="SetupTransactionLog"/> entity.
/// Configures column constraints, composite index on (Operation, Stage), and index on CreatedAt.
/// </summary>
public sealed class SetupTransactionLogConfiguration : IEntityTypeConfiguration<SetupTransactionLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SetupTransactionLog> builder)
    {
        builder.ToTable("SetupTransactionLogs");

        // ── Identity ──────────────────────────────────────────
        builder.HasKey(e => e.Id);

        // ── Properties ────────────────────────────────────────
        builder.Property(e => e.Operation)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.Stage)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.CorrelationId)
            .HasMaxLength(128);

        builder.Property(e => e.ExternalUserId)
            .HasMaxLength(256);

        builder.Property(e => e.Email)
            .HasMaxLength(320);

        builder.Property(e => e.ErrorMessage)
            .HasMaxLength(2048);

        // ── Indexes ───────────────────────────────────────────
        builder.HasIndex(e => new { e.Operation, e.Stage });

        builder.HasIndex(e => e.CreatedAt);

        // ── IAuditable columns ────────────────────────────────
        builder.Property(e => e.CreatedAt);

        builder.Property(e => e.CreatedBy)
            .HasMaxLength(256);

        builder.Property(e => e.UpdatedAt);

        builder.Property(e => e.UpdatedBy)
            .HasMaxLength(256);
    }
}
