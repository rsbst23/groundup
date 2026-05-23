using GroundUp.Auth.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroundUp.Auth.Data.Postgres.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="AuthFlowState"/> entity.
/// </summary>
public sealed class AuthFlowStateConfiguration : IEntityTypeConfiguration<AuthFlowState>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuthFlowState> builder)
    {
        builder.ToTable("AuthFlowStates");
        builder.HasKey(e => e.Id);

        // Indexes
        builder.HasIndex(e => new { e.Status, e.ExpiresAt });
        builder.HasIndex(e => e.TenantId);

        // Enum storage as int
        builder.Property(e => e.FlowType).HasConversion<int>();
        builder.Property(e => e.Status).HasConversion<int>();

        // String constraints
        builder.Property(e => e.Nonce).IsRequired().HasMaxLength(128);
        builder.Property(e => e.Realm).HasMaxLength(128);
        builder.Property(e => e.ReturnUrl).HasMaxLength(2048);
        builder.Property(e => e.CreatedByIp).HasMaxLength(64);
        builder.Property(e => e.CreatedByUserAgent).HasMaxLength(512);
        builder.Property(e => e.FailureReason).HasMaxLength(1024);
    }
}
