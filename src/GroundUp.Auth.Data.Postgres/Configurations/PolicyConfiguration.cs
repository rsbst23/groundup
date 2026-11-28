using GroundUp.Auth.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Auth.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="Policy"/> entity.
/// </summary>
public class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Policy> builder)
    {
        builder.ToTable("AuthPolicies");

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.TenantId);

        // GU-96: names are unique per tenant ignoring case. The expression index
        // IX_AuthPolicies_TenantId_LowerName on (TenantId, lower(Name)) is created in the
        // AddPolicyNameIndexAndRestrictPolicyDelete migration (EF cannot express it).
    }
}
