using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Auth.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="Invitation"/> entity.
/// </summary>
public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("AuthInvitations");

        builder.Property(e => e.Email).IsRequired().HasMaxLength(320);
        builder.Property(e => e.NormalizedEmail).IsRequired().HasMaxLength(320);
        builder.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Status).HasConversion<int>();

        // The token is looked up by its hash
        builder.HasIndex(e => e.TokenHash).IsUnique();

        // One open invitation per email and tenant, so concurrent creates cannot both succeed.
        // An expired invitation is still Pending in storage and counts: resend or revoke it.
        builder.HasIndex(e => new { e.TenantId, e.NormalizedEmail })
            .IsUnique()
            .HasFilter($"\"Status\" = {(int)InvitationStatus.Pending} AND NOT \"IsDeleted\"")
            .HasDatabaseName("IX_AuthInvitations_TenantId_NormalizedEmail_Pending");

        builder.HasIndex(e => new { e.TenantId, e.UserId });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
