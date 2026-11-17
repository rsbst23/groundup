using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// <see cref="AuthFlowTestFixture"/> for an app configured with a default tenant
/// (<c>auth.application.default-tenant-slug</c>), where new users are auto-joined to it.
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
        }

        await SeedSystemSettingAsync(
            ConnectionString,
            "auth.application.default-tenant-slug",
            DefaultTenantSlug,
            "Default Tenant Slug",
            "Slug of the tenant new users are auto-joined to");
    }
}
