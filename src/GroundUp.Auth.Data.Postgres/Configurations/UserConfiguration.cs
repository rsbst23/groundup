using GroundUp.Auth.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Auth.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="User"/> entity.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("AuthUsers");

        // Null for a pending user who has not signed in yet. Unique among users who have.
        builder.Property(e => e.ExternalUserId)
            .HasMaxLength(200);

        builder.HasIndex(e => e.ExternalUserId)
            .IsUnique()
            .HasFilter("\"ExternalUserId\" IS NOT NULL");

        builder.Property(e => e.Email)
            .IsRequired()
            .HasMaxLength(320);

        // Emails are not unique. Pending-user lookups match them ignoring case through the
        // expression index IX_AuthUsers_Email_Lower on lower("Email"), created in the
        // AddPendingMembersAndRoleSource migration (EF cannot model expression indexes).

        builder.Property(e => e.MergedIntoUserId);

        builder.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.IsActive)
            .HasDefaultValue(true);
    }
}
