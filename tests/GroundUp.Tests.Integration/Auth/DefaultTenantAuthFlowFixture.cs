using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// <see cref="AuthFlowTestFixture"/> for an app configured with a default tenant
/// (<c>auth.application.default-tenant-slug</c>), where new users are auto-joined to it and get the
/// default role (<c>auth.application.default-role</c>).
/// <para>
/// Runs against its own Postgres container so the default-tenant setting cannot leak into
/// tests that expect no default tenant. The tenant and setting are seeded once, before the
/// host starts, because setting lookups are cached after the first read.
/// </para>
/// </summary>
public sealed class DefaultTenantAuthFlowFixture : AuthFlowTestFixture
{
    /// <summary>The slug configured as <c>auth.application.default-tenant-slug</c>.</summary>
    public const string DefaultTenantSlug = "community";

    /// <summary>The role configured as <c>auth.application.default-role</c>, seeded in the default tenant.</summary>
    public const string DefaultRoleName = "Member";

    /// <summary>The identifier of the seeded default role.</summary>
    public Guid DefaultRoleId { get; private set; }

    /// <summary>The identifier of the seeded default tenant.</summary>
    public Guid DefaultTenantId { get; private set; }

    /// <inheritdoc />
    protected override async Task SeedBeforeHostStartAsync()
    {
        await using (var context = CreateAuthDbContext())
        {
            var tenant = new Tenant
            {
                Name = "Community",
                Slug = DefaultTenantSlug,
                TenantType = TenantType.Standard,
                OnboardingMode = OnboardingMode.Open,
                IsActive = true
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
            DefaultTenantId = tenant.Id;

            var role = new Role { Name = DefaultRoleName, RoleType = RoleType.Application, TenantId = tenant.Id };
            context.Roles.Add(role);
            await context.SaveChangesAsync();
            DefaultRoleId = role.Id;
        }

        await SeedSystemSettingAsync(
            ConnectionString,
            "auth.application.default-tenant-slug",
            DefaultTenantSlug,
            "Default Tenant Slug",
            "Slug of the tenant new users are auto-joined to");

        await SeedSystemSettingAsync(
            ConnectionString,
            "auth.application.default-role",
            DefaultRoleName,
            "Default Role",
            "Role given to users who join the default tenant");
    }
}
