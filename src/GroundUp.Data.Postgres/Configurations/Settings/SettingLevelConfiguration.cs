using GroundUp.Core.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Data.Postgres.Configurations.Settings;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="SettingLevel"/> entity.
/// </summary>
public class SettingLevelConfiguration : IEntityTypeConfiguration<SettingLevel>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingLevel> builder)
    {
        builder.ToTable("SettingLevels");

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.HasOne(e => e.Parent)
            .WithMany(e => e.Children)
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.DisplayOrder)
            .HasDefaultValue(0);
    }
}
