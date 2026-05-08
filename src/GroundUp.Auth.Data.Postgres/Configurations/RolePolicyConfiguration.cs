using GroundUp.Auth.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Auth.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="RolePolicy"/> junction entity.
/// </summary>
public class RolePolicyConfiguration : IEntityTypeConfiguration<RolePolicy>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RolePolicy> builder)
    {
        builder.ToTable("AuthRolePolicies");

        builder.HasIndex(e => new { e.RoleId, e.PolicyId })
            .IsUnique();

        builder.HasOne(rp => rp.Role)
            .WithMany(r => r.RolePolicies)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rp => rp.Policy)
            .WithMany(p => p.RolePolicies)
            .HasForeignKey(rp => rp.PolicyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.RoleId);

        builder.HasIndex(e => e.PolicyId);
    }
}
