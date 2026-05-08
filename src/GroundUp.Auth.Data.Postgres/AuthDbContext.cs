using System.Linq.Expressions;
using GroundUp.Auth.Core.Entities;
using GroundUp.Core.Entities;
using GroundUp.Data.Postgres;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Data.Postgres;

/// <summary>
/// EF Core DbContext for the authentication and authorization module.
/// Inherits from <see cref="GroundUpDbContext"/> to get UUID v7 value generation,
/// global query filters for soft-deletable entities, and audit interceptors.
/// <para>
/// Overrides <see cref="OnModelCreating"/> to apply only the Auth module's entity
/// configurations, avoiding the base assembly's configurations (e.g., Settings)
/// which belong to the main application context.
/// </para>
/// </summary>
public class AuthDbContext : GroundUpDbContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="AuthDbContext"/>.
    /// </summary>
    /// <param name="options">The typed DbContext options for AuthDbContext.</param>
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    /// <summary>Gets the Users DbSet.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Gets the Tenants DbSet.</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Gets the UserTenants DbSet.</summary>
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();

    /// <summary>Gets the Roles DbSet.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Gets the Policies DbSet.</summary>
    public DbSet<Policy> Policies => Set<Policy>();

    /// <summary>Gets the Permissions DbSet.</summary>
    public DbSet<Permission> Permissions => Set<Permission>();

    /// <summary>Gets the RolePolicies DbSet.</summary>
    public DbSet<RolePolicy> RolePolicies => Set<RolePolicy>();

    /// <summary>Gets the PolicyPermissions DbSet.</summary>
    public DbSet<PolicyPermission> PolicyPermissions => Set<PolicyPermission>();

    /// <summary>Gets the UserRoles DbSet.</summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    /// <summary>
    /// Configures the Auth module's entity model. Applies only the Auth assembly's
    /// configurations and the framework conventions (UUID v7, soft delete filters),
    /// deliberately skipping the base GroundUpDbContext assembly's configurations
    /// to avoid pulling in unrelated module tables (e.g., Settings).
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply Auth module entity configurations only
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);

        // Apply framework conventions: UUID v7 value generation and soft delete filters
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(BaseEntity).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType)
                    .Property(nameof(BaseEntity.Id))
                    .HasValueGenerator<UuidV7ValueGenerator>();
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                ApplySoftDeleteFilter(modelBuilder, clrType);
            }
        }
    }

    /// <summary>
    /// Builds and applies a HasQueryFilter(e => !e.IsDeleted) expression
    /// dynamically for the given entity type.
    /// </summary>
    private static void ApplySoftDeleteFilter(ModelBuilder modelBuilder, Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "e");
        var property = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
        var condition = Expression.Not(property);
        var lambda = Expression.Lambda(condition, parameter);

        modelBuilder.Entity(entityType).HasQueryFilter(lambda);
    }
}
