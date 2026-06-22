using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Auth.Repositories;
using GroundUp.Core.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Base test class for auth integration tests. Uses the shared
/// <see cref="AuthPostgresFixture"/> collection fixture for the Postgres container.
/// Provides helper methods to create repositories with a specific tenant context.
/// </summary>
public abstract class AuthIntegrationTestBase
{
    private readonly AuthPostgresFixture _fixture;
    protected AuthDbContext DbContext;

    protected AuthIntegrationTestBase(AuthPostgresFixture fixture)
    {
        _fixture = fixture;
        DbContext = fixture.CreateContext();
    }

    /// <summary>
    /// Creates a mock ITenantContext that returns the specified tenant ID.
    /// </summary>
    protected ITenantContext CreateTenantContext(Guid tenantId) => new FakeTenantContext(tenantId);

    /// <summary>
    /// Creates a RoleRepository scoped to the specified tenant.
    /// </summary>
    protected RoleRepository CreateRoleRepository(Guid tenantId) =>
        new(DbContext, CreateTenantContext(tenantId));

    /// <summary>
    /// Creates a PolicyRepository scoped to the specified tenant.
    /// </summary>
    protected PolicyRepository CreatePolicyRepository(Guid tenantId) =>
        new(DbContext, CreateTenantContext(tenantId));

    /// <summary>
    /// Creates a UserTenantRepository scoped to the specified tenant.
    /// </summary>
    protected UserTenantRepository CreateUserTenantRepository(Guid tenantId) =>
        new(DbContext, CreateTenantContext(tenantId));

    /// <summary>
    /// Creates a UserRoleRepository scoped to the specified tenant.
    /// </summary>
    protected UserRoleRepository CreateUserRoleRepository(Guid tenantId) =>
        new(DbContext, CreateTenantContext(tenantId));

    /// <summary>
    /// Creates a TenantRepository scoped to the specified tenant.
    /// </summary>
    protected TenantRepository CreateTenantRepository(Guid tenantId) =>
        new(DbContext, CreateTenantContext(tenantId));

    /// <summary>
    /// Creates a UserRepository (no tenant scoping — users are global).
    /// </summary>
    protected UserRepository CreateUserRepository() => new(DbContext);

    /// <summary>
    /// Creates a PermissionRepository (no tenant scoping — permissions are global).
    /// </summary>
    protected PermissionRepository CreatePermissionRepository() => new(DbContext);

    /// <summary>
    /// Seeds a tenant directly into the database and returns its ID.
    /// </summary>
    protected async Task<Guid> SeedTenantAsync(
        string name,
        string slug,
        Guid? parentTenantId = null,
        TenantType tenantType = TenantType.Standard,
        OnboardingMode onboardingMode = OnboardingMode.InviteOnly)
    {
        var tenant = new GroundUp.Auth.Core.Entities.Tenant
        {
            Name = name,
            Slug = slug,
            TenantType = tenantType,
            OnboardingMode = onboardingMode,
            ParentTenantId = parentTenantId,
            IsActive = true
        };
        DbContext.Tenants.Add(tenant);
        await DbContext.SaveChangesAsync();
        return tenant.Id;
    }

    /// <summary>
    /// Seeds a user directly into the database and returns its ID.
    /// </summary>
    protected async Task<Guid> SeedUserAsync(
        string externalUserId,
        string email,
        string displayName = "Test User")
    {
        var user = new GroundUp.Auth.Core.Entities.User
        {
            ExternalUserId = externalUserId,
            Email = email,
            DisplayName = displayName,
            IsActive = true
        };
        DbContext.Users.Add(user);
        await DbContext.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// Seeds a permission directly into the database and returns its ID.
    /// </summary>
    protected async Task<Guid> SeedPermissionAsync(string key, string name, string module)
    {
        var permission = new GroundUp.Auth.Core.Entities.Permission
        {
            Key = key,
            Name = name,
            Module = module
        };
        DbContext.Permissions.Add(permission);
        await DbContext.SaveChangesAsync();
        return permission.Id;
    }

    /// <summary>
    /// Simple ITenantContext implementation for testing.
    /// </summary>
    private sealed class FakeTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
    }
}
