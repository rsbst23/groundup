using GroundUp.Core.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Data.Postgres.Configurations.Settings;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="SettingValue"/> entity.
/// </summary>
public class SettingValueConfiguration : IEntityTypeConfiguration<SettingValue>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingValue> builder)
    {
        builder.ToTable("SettingValues");

        builder.HasOne(e => e.SettingDefinition)
            .WithMany(e => e.Values)
            .HasForeignKey(e => e.SettingDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Level)
            .WithMany()
            .HasForeignKey(e => e.LevelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.Value)
            .HasMaxLength(4000);

        builder.HasIndex(e => e.SettingDefinitionId);

        builder.HasIndex(e => new { e.SettingDefinitionId, e.LevelId, e.ScopeId })
            .IsUnique();
    }
}
