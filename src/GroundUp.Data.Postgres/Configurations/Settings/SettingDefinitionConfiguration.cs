using GroundUp.Core.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Data.Postgres.Configurations.Settings;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="SettingDefinition"/> entity.
/// </summary>
public class SettingDefinitionConfiguration : IEntityTypeConfiguration<SettingDefinition>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingDefinition> builder)
    {
        builder.ToTable("SettingDefinitions");

        // ── Identity ──────────────────────────────────────────
        builder.Property(e => e.Key)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(e => e.Key)
            .IsUnique();

        builder.Property(e => e.DataType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.DefaultValue)
            .HasMaxLength(4000);

        // ── Group FK ──────────────────────────────────────────
        builder.HasOne(e => e.Group)
            .WithMany(e => e.Settings)
            .HasForeignKey(e => e.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.GroupId);

        // ── UI metadata ───────────────────────────────────────
        builder.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.Placeholder)
            .HasMaxLength(500);

        builder.Property(e => e.Category)
            .HasMaxLength(200);

        builder.Property(e => e.DisplayOrder)
            .HasDefaultValue(0);

        builder.Property(e => e.IsVisible)
            .HasDefaultValue(true);

        builder.Property(e => e.IsReadOnly)
            .HasDefaultValue(false);

        // ── Multi-value ───────────────────────────────────────
        builder.Property(e => e.AllowMultiple)
            .HasDefaultValue(false);

        // ── Encryption ────────────────────────────────────────
        builder.Property(e => e.IsEncrypted)
            .HasDefaultValue(false);

        builder.Property(e => e.IsSecret)
            .HasDefaultValue(false);

        // ── Validation ────────────────────────────────────────
        builder.Property(e => e.IsRequired)
            .HasDefaultValue(false);

        builder.Property(e => e.MinValue)
            .HasMaxLength(100);

        builder.Property(e => e.MaxValue)
            .HasMaxLength(100);

        builder.Property(e => e.RegexPattern)
            .HasMaxLength(500);

        builder.Property(e => e.ValidationMessage)
            .HasMaxLength(500);

        // ── Dependencies ──────────────────────────────────────
        builder.Property(e => e.DependsOnKey)
            .HasMaxLength(200);

        builder.Property(e => e.DependsOnOperator)
            .HasMaxLength(20);

        builder.Property(e => e.DependsOnValue)
            .HasMaxLength(1000);

        // ── Custom validation ─────────────────────────────────
        builder.Property(e => e.CustomValidatorType)
            .HasMaxLength(500);
    }
}
