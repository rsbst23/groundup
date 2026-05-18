using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Data.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Data.Postgres.Seeders;

/// <summary>
/// Seeds the system-level SuperAdmin role, its "FullAccess" policy, and links
/// all built-in permissions to that policy. System roles are scoped to the
/// well-known System tenant (<see cref="AuthRoleNames.SystemTenantId"/>).
/// <para>
/// Must run after <see cref="DefaultPermissionSeeder"/> (Order 10) so that
/// permission records exist before being linked to the policy.
/// </para>
/// Idempotent — only creates records that don't already exist.
/// </summary>
public sealed class DefaultSystemRoleSeeder : IDataSeeder
{
    private const string FullAccessPolicyName = "FullAccess";
    private const string SystemTenantName = "System";
    private const string SystemTenantSlug = "system";

    private readonly AuthDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultSystemRoleSeeder"/>.
    /// </summary>
    /// <param name="dbContext">The auth database context.</param>
    public DefaultSystemRoleSeeder(AuthDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSystemTenantAsync(cancellationToken);
        var role = await EnsureRoleAsync(cancellationToken);
        var policy = await EnsurePolicyAsync(cancellationToken);
        await EnsureRolePolicyLinkAsync(role.Id, policy.Id, cancellationToken);
        await EnsureAllPermissionsLinkedAsync(policy.Id, cancellationToken);
    }

    private async Task EnsureSystemTenantAsync(CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Tenants
            .AnyAsync(t => t.Id == AuthRoleNames.SystemTenantId, cancellationToken);

        if (exists)
        {
            return;
        }

        var tenant = new Tenant
        {
            Id = AuthRoleNames.SystemTenantId,
            Name = SystemTenantName,
            Slug = SystemTenantSlug,
            TenantType = TenantType.Standard,
            OnboardingMode = OnboardingMode.InviteOnly,
            IsActive = true
        };

        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Role> EnsureRoleAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.Name == AuthRoleNames.SuperAdmin
                                      && r.TenantId == AuthRoleNames.SystemTenantId
                                      && r.RoleType == RoleType.System,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var role = new Role
        {
            Name = AuthRoleNames.SuperAdmin,
            Description = "Full system access. Bypasses all permission checks.",
            RoleType = RoleType.System,
            TenantId = AuthRoleNames.SystemTenantId,
            IsSystem = true
        };

        _dbContext.Roles.Add(role);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return role;
    }

    private async Task<Policy> EnsurePolicyAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Policies
            .FirstOrDefaultAsync(p => p.Name == FullAccessPolicyName
                                      && p.TenantId == AuthRoleNames.SystemTenantId,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var policy = new Policy
        {
            Name = FullAccessPolicyName,
            Description = "Grants all built-in permissions. Assigned to the SuperAdmin system role.",
            TenantId = AuthRoleNames.SystemTenantId
        };

        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return policy;
    }

    private async Task EnsureRolePolicyLinkAsync(Guid roleId, Guid policyId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.RolePolicies
            .AnyAsync(rp => rp.RoleId == roleId && rp.PolicyId == policyId, cancellationToken);

        if (exists)
        {
            return;
        }

        _dbContext.RolePolicies.Add(new RolePolicy
        {
            RoleId = roleId,
            PolicyId = policyId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAllPermissionsLinkedAsync(Guid policyId, CancellationToken cancellationToken)
    {
        var allPermissions = await _dbContext.Permissions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var existingLinks = await _dbContext.PolicyPermissions
            .Where(pp => pp.PolicyId == policyId)
            .Select(pp => pp.PermissionId)
            .ToListAsync(cancellationToken);

        var existingLinkSet = new HashSet<Guid>(existingLinks);

        var toLink = allPermissions
            .Where(p => !existingLinkSet.Contains(p.Id))
            .Select(p => new PolicyPermission
            {
                PolicyId = policyId,
                PermissionId = p.Id
            })
            .ToList();

        if (toLink.Count > 0)
        {
            _dbContext.PolicyPermissions.AddRange(toLink);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
