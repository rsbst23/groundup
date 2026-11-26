using GroundUp.Auth.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Auth.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="UserRole"/> junction entity.
/// </summary>
public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("AuthUserRoles");

        // Stored as int (see RoleAssignmentSource). A user can hold one role from several
        // sources, one row per source.
        builder.Property(e => e.Source)
            .HasConversion<int>();

        builder.HasIndex(e => new { e.UserId, e.RoleId, e.TenantId, e.Source })
            .IsUnique();

        builder.HasOne(ur => ur.User)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ur => ur.Tenant)
            .WithMany()
            .HasForeignKey(ur => ur.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.UserId);

        builder.HasIndex(e => e.RoleId);

        builder.HasIndex(e => e.TenantId);
    }
}
