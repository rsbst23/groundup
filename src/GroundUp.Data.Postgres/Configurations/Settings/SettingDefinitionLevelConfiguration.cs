using GroundUp.Core.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Data.Postgres.Configurations.Settings;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="SettingDefinitionLevel"/> junction entity.
/// </summary>
public class SettingDefinitionLevelConfiguration : IEntityTypeConfiguration<SettingDefinitionLevel>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingDefinitionLevel> builder)
    {
        builder.ToTable("SettingDefinitionLevels");

        builder.HasOne(e => e.SettingDefinition)
            .WithMany(e => e.AllowedLevels)
            .HasForeignKey(e => e.SettingDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.SettingLevel)
            .WithMany()
            .HasForeignKey(e => e.SettingLevelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.SettingDefinitionId, e.SettingLevelId })
            .IsUnique();
    }
}
