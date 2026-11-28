using System.Net.Http.Json;
using System.Text.Json;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Shared setup for the admin API tests (GU-96): an organization with a TenantAdmin and a plain
/// member, permissions granted through a role and policy seeded directly, and JSON helpers.
/// </summary>
public abstract class AdminApiTestBase : AuthFlowTestBase
{
    protected AdminApiTestBase(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>A tenant with a TenantAdmin and a plain member, each with a token for the tenant.</summary>
    protected sealed record Organization(
        Guid TenantId, Guid AdminId, Guid AdminRoleId, string AdminToken, Guid MemberId, string MemberToken, string Suffix);

    protected async Task<Organization> SeedOrganizationAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"Admin Org {suffix}", $"admin-org-{suffix}");
        var adminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);

        var adminId = await SeedUserAsync($"kc-aadmin-{suffix}", $"aadmin-{suffix}@example.com");
        await SeedMembershipAsync(adminId, tenantId);
        await AssignRoleToUserAsync(adminId, adminRoleId, tenantId);

        var memberId = await SeedUserAsync($"kc-amember-{suffix}", $"amember-{suffix}@example.com");
        await SeedMembershipAsync(memberId, tenantId);

        return new Organization(
            tenantId, adminId, adminRoleId, Fixture.GenerateTestToken(adminId, tenantId),
            memberId, Fixture.GenerateTestToken(memberId, tenantId), suffix);
    }

    /// <summary>
    /// Gives <paramref name="userId"/> a new role whose policy holds <paramref name="keys"/>, seeded
    /// directly (not through the API, so the escalation guard does not apply).
    /// </summary>
    /// <returns>The role and policy ids.</returns>
    protected async Task<(Guid RoleId, Guid PolicyId)> GrantPermissionsAsync(Organization org, Guid userId, params string[] keys)
    {
        var roleId = await SeedRoleAsync(org.TenantId, $"Custom {Guid.NewGuid():N}");
        Guid policyId;
        await using (var context = Fixture.CreateAuthDbContext())
        {
            var policy = new Policy { Name = $"Seeded {Guid.NewGuid():N}", TenantId = org.TenantId };
            context.Policies.Add(policy);
            foreach (var key in keys)
            {
                var permissionId = await EnsurePermissionAsync(key);
                context.Set<PolicyPermission>().Add(new PolicyPermission { Policy = policy, PermissionId = permissionId });
            }

            context.Set<RolePolicy>().Add(new RolePolicy { RoleId = roleId, Policy = policy });
            await context.SaveChangesAsync();
            policyId = policy.Id;
        }

        await AssignRoleToUserAsync(userId, roleId, org.TenantId);
        return (roleId, policyId);
    }

    /// <summary>The id of the permission with this key, created when the catalogue does not have it yet.</summary>
    protected async Task<Guid> EnsurePermissionAsync(string key)
    {
        await using var context = Fixture.CreateAuthDbContext();
        var existing = await context.Permissions.AsNoTracking().FirstOrDefaultAsync(p => p.Key == key);
        if (existing is not null)
        {
            return existing.Id;
        }

        var permission = new Permission { Key = key, Name = key, Module = key.Split('.')[0] };
        context.Permissions.Add(permission);
        try
        {
            await context.SaveChangesAsync();
            return permission.Id;
        }
        catch (DbUpdateException)
        {
            // Another test class created it at the same time
            await using var again = Fixture.CreateAuthDbContext();
            return (await again.Permissions.AsNoTracking().SingleAsync(p => p.Key == key)).Id;
        }
    }

    protected Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = CreateBearerRequest(method, url, token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return Client.SendAsync(request);
    }

    protected static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    protected static async Task<JsonElement> DataAsync(HttpResponseMessage response)
        => (await BodyAsync(response)).GetProperty("data");

    protected static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
        => (await BodyAsync(response)).GetProperty("errorCode").GetString();

    protected static async Task<List<string>> ErrorsAsync(HttpResponseMessage response)
        => (await BodyAsync(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToList();
}
