using GroundUp.Auth.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Auth.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="JoinLink"/> entity.
/// </summary>
public class JoinLinkConfiguration : IEntityTypeConfiguration<JoinLink>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<JoinLink> builder)
    {
        builder.ToTable("AuthJoinLinks");

        builder.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Status).HasConversion<int>();

        // The token is looked up by its hash
        builder.HasIndex(e => e.TokenHash).IsUnique();
        builder.HasIndex(e => e.TenantId);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
