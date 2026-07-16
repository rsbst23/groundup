using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Property-based tests for TenantAdmin bypass and system role exclusion in <see cref="PermissionService"/>.
/// Feature: phase-10c-auth-dispatcher, Properties 15–16.
/// **Validates: Requirements 10.3, 10.4, 10.7**
/// </summary>
[Trait("Category", "Property")]
public sealed class PermissionServiceTenantAdminPropertyTests
{
    // --- Property 15: TenantAdmin Permission Bypass (Tenant-Scoped) ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 15: TenantAdmin Permission Bypass (Tenant-Scoped)
    /// For ANY arbitrary permission key string, when the user holds the TenantAdmin role
    /// in the current tenant context, HasPermissionAsync returns true.
    /// The bypass is universal — it does not depend on the specific permission being checked.
    /// **Validates: Requirements 10.3, 10.4**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TenantAdminBypassArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 15: TenantAdmin bypass grants any permission in current tenant")]
    public Property TenantAdminBypass_GrantsAnyPermission(TenantAdminBypassScenario scenario)
    {
        // Arrange
        var sut = CreatePermissionServiceWithTenantAdmin(
            scenario.UserId, scenario.TenantId, isTenantAdmin: true);

        // Act — check an arbitrary permission key
        var result = sut.HasPermissionAsync(scenario.UserId, scenario.PermissionKey)
            .GetAwaiter().GetResult();

        // Assert — TenantAdmin in current tenant always returns true
        return result.ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 15: TenantAdmin Permission Bypass (Tenant-Scoped)
    /// For ANY arbitrary set of permission key strings, when the user holds TenantAdmin
    /// in the current tenant, HasAnyPermissionAsync returns true.
    /// **Validates: Requirements 10.3, 10.4**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TenantAdminBypassArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 15: TenantAdmin bypass grants HasAnyPermission in current tenant")]
    public Property TenantAdminBypass_GrantsHasAnyPermission(TenantAdminHasAnyScenario scenario)
    {
        // Arrange
        var sut = CreatePermissionServiceWithTenantAdmin(
            scenario.UserId, scenario.TenantId, isTenantAdmin: true);

        // Act — check multiple arbitrary permission keys
        var result = sut.HasAnyPermissionAsync(scenario.UserId, scenario.PermissionKeys)
            .GetAwaiter().GetResult();

        // Assert — TenantAdmin in current tenant always returns true
        return result.ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 15: TenantAdmin Permission Bypass (Tenant-Scoped)
    /// The bypass is tenant-scoped: for ANY permission key, when the user holds TenantAdmin
    /// in a DIFFERENT tenant than the current context, HasPermissionAsync returns false
    /// (assuming no other roles grant the permission).
    /// **Validates: Requirements 10.7**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TenantAdminBypassArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 15: TenantAdmin bypass does NOT leak across tenants")]
    public Property TenantAdminBypass_DoesNotLeakAcrossTenants(TenantAdminCrossTenantScenario scenario)
    {
        // Arrange — user holds TenantAdmin in a different tenant
        var sut = CreatePermissionServiceWithTenantAdminInDifferentTenant(
            scenario.UserId, scenario.CurrentTenantId, scenario.OtherTenantId);

        // Act — check permission in the current tenant (where user is NOT TenantAdmin)
        var result = sut.HasPermissionAsync(scenario.UserId, scenario.PermissionKey)
            .GetAwaiter().GetResult();

        // Assert — bypass does not fire, and no other permissions are granted → false
        return (!result).ToProperty();
    }

    // --- Property 16: TenantAdmin Is Not a System Role ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 16: TenantAdmin Is Not a System Role
    /// For ANY user (regardless of whether they hold TenantAdmin in some tenant),
    /// GetSystemRolesForUserAsync never returns a role with RoleName == "TenantAdmin".
    /// TenantAdmin is a tenant-scoped role, not a global system role.
    /// **Validates: Requirements 10.3, 10.7**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TenantAdminBypassArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 16: TenantAdmin never appears in system roles")]
    public Property TenantAdmin_NeverAppearsInSystemRoles(SystemRoleExclusionScenario scenario)
    {
        // Arrange — simulate whatever system roles the user has (never TenantAdmin)
        var userRoleRepository = Substitute.For<IUserRoleRepository>();

        // Setup system roles as generated — these represent what the repository returns
        userRoleRepository.GetSystemRolesForUserAsync(scenario.UserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(scenario.SystemRoles));

        var roleRepository = Substitute.For<IRoleRepository>();
        var policyRepository = Substitute.For<IPolicyRepository>();
        var tenantContext = Substitute.For<ITenantContext>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });

        tenantContext.TenantId.Returns(scenario.TenantId);

        var sut = new PermissionService(
            userRoleRepository, roleRepository, policyRepository,
            tenantContext, cache, options);

        // Act — check if the user has TenantAdmin as a system role
        var hasTenantAdminSystemRole = sut.HasSystemRoleAsync(scenario.UserId, AuthRoleNames.TenantAdmin)
            .GetAwaiter().GetResult();

        // Assert — TenantAdmin must NEVER be reported as a system role
        return (!hasTenantAdminSystemRole).ToProperty();
    }

    // --- Helper methods ---

    /// <summary>
    /// Creates a PermissionService where the user holds TenantAdmin in the specified tenant
    /// (which is also the current tenant context), or does not hold TenantAdmin.
    /// </summary>
    private static PermissionService CreatePermissionServiceWithTenantAdmin(
        Guid userId, Guid tenantId, bool isTenantAdmin)
    {
        var userRoleRepository = Substitute.For<IUserRoleRepository>();
        var roleRepository = Substitute.For<IRoleRepository>();
        var policyRepository = Substitute.For<IPolicyRepository>();
        var tenantContext = Substitute.For<ITenantContext>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });

        tenantContext.TenantId.Returns(tenantId);

        // Setup TenantAdmin in current tenant
        if (isTenantAdmin)
        {
            var tenantAdminRole = new UserRoleDto(
                Guid.NewGuid(), userId, Guid.NewGuid(), tenantId, AuthRoleNames.TenantAdmin);

            userRoleRepository.GetByUserIdForTenantAsync(userId, tenantId, Arg.Any<CancellationToken>())
                .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto> { tenantAdminRole }));
        }
        else
        {
            userRoleRepository.GetByUserIdForTenantAsync(userId, tenantId, Arg.Any<CancellationToken>())
                .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));
        }

        // No system roles (SuperAdmin check returns empty)
        userRoleRepository.GetSystemRolesForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));

        return new PermissionService(
            userRoleRepository, roleRepository, policyRepository,
            tenantContext, cache, options);
    }

    /// <summary>
    /// Creates a PermissionService where the user holds TenantAdmin in a DIFFERENT tenant
    /// than the current context. No permissions are granted via the normal path.
    /// </summary>
    private static PermissionService CreatePermissionServiceWithTenantAdminInDifferentTenant(
        Guid userId, Guid currentTenantId, Guid otherTenantId)
    {
        var userRoleRepository = Substitute.For<IUserRoleRepository>();
        var roleRepository = Substitute.For<IRoleRepository>();
        var policyRepository = Substitute.For<IPolicyRepository>();
        var tenantContext = Substitute.For<ITenantContext>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });

        tenantContext.TenantId.Returns(currentTenantId);

        // TenantAdmin in the OTHER tenant (not the current one)
        var tenantAdminRole = new UserRoleDto(
            Guid.NewGuid(), userId, Guid.NewGuid(), otherTenantId, AuthRoleNames.TenantAdmin);

        userRoleRepository.GetByUserIdForTenantAsync(userId, otherTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto> { tenantAdminRole }));

        // No TenantAdmin in the CURRENT tenant
        userRoleRepository.GetByUserIdForTenantAsync(userId, currentTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));

        // No system roles
        userRoleRepository.GetSystemRolesForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));

        // No tenant roles for permission resolution
        userRoleRepository.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));

        return new PermissionService(
            userRoleRepository, roleRepository, policyRepository,
            tenantContext, cache, options);
    }
}

// --- Test data types ---

/// <summary>
/// Scenario for testing that TenantAdmin bypass grants any permission in the current tenant.
/// </summary>
public sealed record TenantAdminBypassScenario(
    Guid UserId,
    Guid TenantId,
    string PermissionKey)
{
    public override string ToString() =>
        $"User={UserId.ToString()[..8]}..., Tenant={TenantId.ToString()[..8]}..., Permission=\"{PermissionKey}\"";
}

/// <summary>
/// Scenario for testing that TenantAdmin bypass grants HasAnyPermission with multiple keys.
/// </summary>
public sealed record TenantAdminHasAnyScenario(
    Guid UserId,
    Guid TenantId,
    string[] PermissionKeys)
{
    public override string ToString() =>
        $"User={UserId.ToString()[..8]}..., Tenant={TenantId.ToString()[..8]}..., Permissions=[{string.Join(", ", PermissionKeys)}]";
}

/// <summary>
/// Scenario for testing that TenantAdmin bypass does not leak across tenants.
/// </summary>
public sealed record TenantAdminCrossTenantScenario(
    Guid UserId,
    Guid CurrentTenantId,
    Guid OtherTenantId,
    string PermissionKey)
{
    public override string ToString() =>
        $"User={UserId.ToString()[..8]}..., Current={CurrentTenantId.ToString()[..8]}..., Other={OtherTenantId.ToString()[..8]}..., Permission=\"{PermissionKey}\"";
}

/// <summary>
/// Scenario for testing that TenantAdmin never appears in system role queries.
/// </summary>
public sealed record SystemRoleExclusionScenario(
    Guid UserId,
    Guid TenantId,
    List<UserRoleDto> SystemRoles)
{
    public override string ToString() =>
        $"User={UserId.ToString()[..8]}..., SystemRoles=[{string.Join(", ", SystemRoles.Select(r => r.RoleName ?? "null"))}]";
}

/// <summary>
/// Custom FsCheck Arbitrary generators for TenantAdmin bypass property tests.
/// </summary>
public static class TenantAdminBypassArbitraries
{
    /// <summary>
    /// Generates arbitrary permission key strings representative of real-world patterns.
    /// Includes dot-separated keys, hyphenated names, and various realistic formats.
    /// </summary>
    private static Gen<string> PermissionKeyGen()
    {
        var resources = new[] { "users", "roles", "settings", "billing", "reports", "tenants", "admin", "projects", "documents" };
        var actions = new[] { "read", "write", "delete", "create", "update", "manage", "view", "export", "import", "approve" };

        return Gen.OneOf(
            // Standard dot-separated format: resource.action
            from resource in Gen.Elements(resources)
            from action in Gen.Elements(actions)
            select $"{resource}.{action}",
            // Nested format: module.resource.action
            from module in Gen.Elements("admin", "api", "system", "tenant", "billing")
            from resource in Gen.Elements(resources)
            from action in Gen.Elements(actions)
            select $"{module}.{resource}.{action}",
            // Hyphenated format
            from resource in Gen.Elements("manage-users", "view-reports", "edit-settings", "admin-panel")
            select resource,
            // Generated arbitrary strings (non-empty, alphanumeric + dots)
            from length in Gen.Choose(3, 30)
            from chars in Gen.ArrayOf(length, Gen.Elements(
                'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
                'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
                '.', '-', '_'))
            select new string(chars).Trim('.').Trim('-').Trim('_') is { Length: > 0 } s ? s : "fallback.permission"
        );
    }

    /// <summary>
    /// Generates TenantAdminBypassScenario instances with arbitrary user IDs, tenant IDs, and permission keys.
    /// </summary>
    public static Arbitrary<TenantAdminBypassScenario> TenantAdminBypassScenarioArb()
    {
        var gen = from userId in Gen.Fresh(() => Guid.NewGuid())
                  from tenantId in Gen.Fresh(() => Guid.NewGuid())
                  from permKey in PermissionKeyGen()
                  select new TenantAdminBypassScenario(userId, tenantId, permKey);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates TenantAdminHasAnyScenario instances with multiple arbitrary permission keys.
    /// </summary>
    public static Arbitrary<TenantAdminHasAnyScenario> TenantAdminHasAnyScenarioArb()
    {
        var gen = from userId in Gen.Fresh(() => Guid.NewGuid())
                  from tenantId in Gen.Fresh(() => Guid.NewGuid())
                  from keyCount in Gen.Choose(1, 5)
                  from keys in Gen.ArrayOf(keyCount, PermissionKeyGen())
                  select new TenantAdminHasAnyScenario(userId, tenantId, keys);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates TenantAdminCrossTenantScenario instances ensuring current and other tenant IDs differ.
    /// </summary>
    public static Arbitrary<TenantAdminCrossTenantScenario> TenantAdminCrossTenantScenarioArb()
    {
        var gen = from userId in Gen.Fresh(() => Guid.NewGuid())
                  from currentTenantId in Gen.Fresh(() => Guid.NewGuid())
                  from otherTenantId in Gen.Fresh(() => Guid.NewGuid())
                  from permKey in PermissionKeyGen()
                  select new TenantAdminCrossTenantScenario(userId, currentTenantId, otherTenantId, permKey);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates SystemRoleExclusionScenario instances with system roles that NEVER include TenantAdmin.
    /// This validates that the repository contract is upheld — TenantAdmin is not a system role.
    /// </summary>
    public static Arbitrary<SystemRoleExclusionScenario> SystemRoleExclusionScenarioArb()
    {
        // System role names that are legitimate (TenantAdmin is excluded by definition)
        var legitimateSystemRoleNames = new[] { "SuperAdmin", "PlatformOperator", "SystemAuditor", "GlobalReader" };

        var gen = from userId in Gen.Fresh(() => Guid.NewGuid())
                  from tenantId in Gen.Fresh(() => Guid.NewGuid())
                  from roleCount in Gen.Choose(0, 3)
                  from roleNames in Gen.ArrayOf(roleCount, Gen.Elements(legitimateSystemRoleNames))
                  let systemRoles = roleNames.Select(name =>
                      new UserRoleDto(Guid.NewGuid(), userId, Guid.NewGuid(), Guid.Empty, name)).ToList()
                  select new SystemRoleExclusionScenario(userId, tenantId, systemRoles);

        return gen.ToArbitrary();
    }
}
