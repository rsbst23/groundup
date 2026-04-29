using GroundUp.Core.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Data.Postgres.Configurations.Settings;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="SettingGroup"/> entity.
/// </summary>
public class SettingGroupConfiguration : IEntityTypeConfiguration<SettingGroup>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingGroup> builder)
    {
        builder.ToTable("SettingGroups");

        builder.Property(e => e.Key)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(e => e.Key)
            .IsUnique();

        builder.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.Icon)
            .HasMaxLength(100);

        builder.Property(e => e.DisplayOrder)
            .HasDefaultValue(0);
    }
}
