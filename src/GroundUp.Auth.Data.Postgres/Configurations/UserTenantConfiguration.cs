using GroundUp.Auth.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Auth.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="UserTenant"/> entity.
/// </summary>
public class UserTenantConfiguration : IEntityTypeConfiguration<UserTenant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserTenant> builder)
    {
        builder.ToTable("AuthUserTenants");

        builder.Property(e => e.ExternalUserId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.IsActive)
            .HasDefaultValue(true);

        builder.HasIndex(e => new { e.UserId, e.TenantId })
            .IsUnique();

        builder.HasOne(ut => ut.User)
            .WithMany(u => u.UserTenants)
            .HasForeignKey(ut => ut.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ut => ut.Tenant)
            .WithMany(t => t.UserTenants)
            .HasForeignKey(ut => ut.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.UserId);

        builder.HasIndex(e => e.TenantId);
    }
}
