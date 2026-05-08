using GroundUp.Auth.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Auth.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="PolicyPermission"/> junction entity.
/// </summary>
public class PolicyPermissionConfiguration : IEntityTypeConfiguration<PolicyPermission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PolicyPermission> builder)
    {
        builder.ToTable("AuthPolicyPermissions");

        builder.HasIndex(e => new { e.PolicyId, e.PermissionId })
            .IsUnique();

        builder.HasOne(pp => pp.Policy)
            .WithMany(p => p.PolicyPermissions)
            .HasForeignKey(pp => pp.PolicyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pp => pp.Permission)
            .WithMany(p => p.PolicyPermissions)
            .HasForeignKey(pp => pp.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.PolicyId);

        builder.HasIndex(e => e.PermissionId);
    }
}
