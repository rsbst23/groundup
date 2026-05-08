using GroundUp.Auth.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Auth.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="Tenant"/> entity.
/// </summary>
public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("AuthTenants");

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(e => e.Slug)
            .IsUnique();

        builder.Property(e => e.RealmName)
            .HasMaxLength(200);

        builder.Property(e => e.CustomDomain)
            .HasMaxLength(500);

        builder.Property(e => e.TenantType)
            .HasConversion<int>();

        builder.Property(e => e.OnboardingMode)
            .HasConversion<int>();

        builder.Property(e => e.IsActive)
            .HasDefaultValue(true);

        builder.HasOne(t => t.Parent)
            .WithMany(t => t.Children)
            .HasForeignKey(t => t.ParentTenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ParentTenantId);
    }
}
