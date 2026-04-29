using GroundUp.Core.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Data.Postgres.Configurations.Settings;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="SettingOption"/> entity.
/// </summary>
public class SettingOptionConfiguration : IEntityTypeConfiguration<SettingOption>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingOption> builder)
    {
        builder.ToTable("SettingOptions");

        builder.HasOne(e => e.SettingDefinition)
            .WithMany(e => e.Options)
            .HasForeignKey(e => e.SettingDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.Value)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(e => e.Label)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.DisplayOrder)
            .HasDefaultValue(0);

        builder.Property(e => e.IsDefault)
            .HasDefaultValue(false);

        builder.Property(e => e.ParentOptionValue)
            .HasMaxLength(1000);
    }
}
