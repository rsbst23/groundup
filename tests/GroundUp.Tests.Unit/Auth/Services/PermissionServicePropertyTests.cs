using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services;

/// <summary>
/// Property-based tests for <see cref="PermissionService"/>.
/// Feature: phase-9c-permission-service
/// </summary>
[Trait("Category", "Property")]
public sealed class PermissionServicePropertyTests
{
    /// <summary>
    /// Feature: phase-9c-permission-service, Property 1:
    /// Permission resolution produces the correct union of tenant and system role permissions.
    /// For any user with a set of tenant-scoped roles and a set of system roles,
    /// GetUserPermissionsAsync returns exactly the union of all permission keys reachable
    /// through both hierarchies.
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PermissionResolution_ProducesCorrectUnion()
    {
        return Prop.ForAll(
            PermissionGraphArbitrary(),
            graph =>
            {
                // Arrange
                var userId = Guid.NewGuid();
                var tenantId = Guid.NewGuid();

                var userRoleRepository = Substitute.For<IUserRoleRepository>();
                var roleRepository = Substitute.For<IRoleRepository>();
                var policyRepository = Substitute.For<IPolicyRepository>();
                var tenantContext = Substitute.For<ITenantContext>();
                var cache = new MemoryCache(new MemoryCacheOptions());
                var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });

                tenantContext.TenantId.Returns(tenantId);

                // Default returns for unconfigured calls
                roleRepository.GetPoliciesForRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(OperationResult<List<PolicyDto>>.Ok(new List<PolicyDto>()));
                policyRepository.GetPermissionsForPolicyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(OperationResult<List<PermissionDto>>.Ok(new List<PermissionDto>()));

                // Setup tenant roles
                var tenantRoleDtos = graph.TenantRoles
                    .Select(r => new UserRoleDto(Guid.NewGuid(), userId, r.RoleId, tenantId))
                    .ToList();
                userRoleRepository.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
                    .Returns(OperationResult<List<UserRoleDto>>.Ok(tenantRoleDtos));

                // Setup system roles
                var systemRoleDtos = graph.SystemRoles
                    .Select(r => new UserRoleDto(Guid.NewGuid(), userId, r.RoleId, Guid.Empty, r.RoleName))
                    .ToList();
                userRoleRepository.GetSystemRolesForUserAsync(userId, Arg.Any<CancellationToken>())
                    .Returns(OperationResult<List<UserRoleDto>>.Ok(systemRoleDtos));

                // Setup policies for each role
                foreach (var role in graph.TenantRoles.Concat(graph.SystemRoles))
                {
                    var policyDtos = role.PolicyIds
                        .Select(pid => new PolicyDto(pid, "Policy", null, tenantId))
                        .ToList();
                    roleRepository.GetPoliciesForRoleAsync(role.RoleId, Arg.Any<CancellationToken>())
                        .Returns(OperationResult<List<PolicyDto>>.Ok(policyDtos));
                }

                // Setup permissions for each policy
                foreach (var mapping in graph.PolicyPermissions)
                {
                    var permDtos = mapping.PermissionKeys
                        .Select(key => new PermissionDto(Guid.NewGuid(), key, key, null, "test"))
                        .ToList();
                    policyRepository.GetPermissionsForPolicyAsync(mapping.PolicyId, Arg.Any<CancellationToken>())
                        .Returns(OperationResult<List<PermissionDto>>.Ok(permDtos));
                }

                var sut = new PermissionService(
                    userRoleRepository, roleRepository, policyRepository,
                    tenantContext, cache, options);

                // Act
                var result = sut.GetUserPermissionsAsync(userId).GetAwaiter().GetResult();

                // Compute expected: union of all permission keys reachable through both paths
                var expected = new HashSet<string>(StringComparer.Ordinal);
                foreach (var role in graph.TenantRoles.Concat(graph.SystemRoles))
                {
                    foreach (var policyId in role.PolicyIds)
                    {
                        var mapping = graph.PolicyPermissions.FirstOrDefault(m => m.PolicyId == policyId);
                        if (mapping != null)
                        {
                            foreach (var key in mapping.PermissionKeys)
                            {
                                expected.Add(key);
                            }
                        }
                    }
                }

                // Assert
                return result.SetEquals(expected).ToProperty();
            });
    }

    /// <summary>
    /// Feature: phase-9c-permission-service, Property 2:
    /// System role permissions transcend tenant boundaries.
    /// For any user with system-level role assignments, the permissions granted by those
    /// system roles appear in the resolved permission set regardless of which tenant is the current context.
    /// **Validates: Requirements 1.5, 1.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SystemRolePermissions_TranscendTenantBoundaries()
    {
        return Prop.ForAll(
            SystemRoleGraphArbitrary(),
            graph =>
            {
                var userId = Guid.NewGuid();
                var tenantA = Guid.NewGuid();
                var tenantB = Guid.NewGuid();

                // Resolve permissions in tenant A
                var permissionsA = ResolvePermissionsForTenant(userId, tenantA, graph);

                // Resolve permissions in tenant B
                var permissionsB = ResolvePermissionsForTenant(userId, tenantB, graph);

                // System role permissions should be present in both
                var expectedSystemPerms = new HashSet<string>(StringComparer.Ordinal);
                foreach (var role in graph.SystemRoles)
                {
                    foreach (var policyId in role.PolicyIds)
                    {
                        var mapping = graph.PolicyPermissions.FirstOrDefault(m => m.PolicyId == policyId);
                        if (mapping != null)
                        {
                            foreach (var key in mapping.PermissionKeys)
                            {
                                expectedSystemPerms.Add(key);
                            }
                        }
                    }
                }

                return (expectedSystemPerms.IsSubsetOf(permissionsA)
                    && expectedSystemPerms.IsSubsetOf(permissionsB))
                    .ToProperty();
            });
    }

    /// <summary>
    /// Feature: phase-9c-permission-service, Property 3:
    /// Permission set deduplication invariant.
    /// For any user whose tenant-scoped roles and system roles grant overlapping permission keys,
    /// GetUserPermissionsAsync returns a set where each permission key appears exactly once.
    /// The count equals the count of distinct permission keys across all reachable paths.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PermissionSet_DeduplicationInvariant()
    {
        return Prop.ForAll(
            OverlappingPermissionGraphArbitrary(),
            graph =>
            {
                var userId = Guid.NewGuid();
                var tenantId = Guid.NewGuid();

                var userRoleRepository = Substitute.For<IUserRoleRepository>();
                var roleRepository = Substitute.For<IRoleRepository>();
                var policyRepository = Substitute.For<IPolicyRepository>();
                var tenantContext = Substitute.For<ITenantContext>();
                var cache = new MemoryCache(new MemoryCacheOptions());
                var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });

                tenantContext.TenantId.Returns(tenantId);

                // Default returns for unconfigured calls
                roleRepository.GetPoliciesForRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(OperationResult<List<PolicyDto>>.Ok(new List<PolicyDto>()));
                policyRepository.GetPermissionsForPolicyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(OperationResult<List<PermissionDto>>.Ok(new List<PermissionDto>()));

                // Setup tenant roles
                var tenantRoleDtos = graph.TenantRoles
                    .Select(r => new UserRoleDto(Guid.NewGuid(), userId, r.RoleId, tenantId))
                    .ToList();
                userRoleRepository.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
                    .Returns(OperationResult<List<UserRoleDto>>.Ok(tenantRoleDtos));

                // Setup system roles
                var systemRoleDtos = graph.SystemRoles
                    .Select(r => new UserRoleDto(Guid.NewGuid(), userId, r.RoleId, Guid.Empty, r.RoleName))
                    .ToList();
                userRoleRepository.GetSystemRolesForUserAsync(userId, Arg.Any<CancellationToken>())
                    .Returns(OperationResult<List<UserRoleDto>>.Ok(systemRoleDtos));

                // Setup policies and permissions
                foreach (var role in graph.TenantRoles.Concat(graph.SystemRoles))
                {
                    var policyDtos = role.PolicyIds
                        .Select(pid => new PolicyDto(pid, "Policy", null, tenantId))
                        .ToList();
                    roleRepository.GetPoliciesForRoleAsync(role.RoleId, Arg.Any<CancellationToken>())
                        .Returns(OperationResult<List<PolicyDto>>.Ok(policyDtos));
                }

                foreach (var mapping in graph.PolicyPermissions)
                {
                    var permDtos = mapping.PermissionKeys
                        .Select(key => new PermissionDto(Guid.NewGuid(), key, key, null, "test"))
                        .ToList();
                    policyRepository.GetPermissionsForPolicyAsync(mapping.PolicyId, Arg.Any<CancellationToken>())
                        .Returns(OperationResult<List<PermissionDto>>.Ok(permDtos));
                }

                var sut = new PermissionService(
                    userRoleRepository, roleRepository, policyRepository,
                    tenantContext, cache, options);

                // Act
                var result = sut.GetUserPermissionsAsync(userId).GetAwaiter().GetResult();

                // Compute expected distinct count
                var allKeys = new List<string>();
                foreach (var role in graph.TenantRoles.Concat(graph.SystemRoles))
                {
                    foreach (var policyId in role.PolicyIds)
                    {
                        var mapping = graph.PolicyPermissions.FirstOrDefault(m => m.PolicyId == policyId);
                        if (mapping != null)
                        {
                            allKeys.AddRange(mapping.PermissionKeys);
                        }
                    }
                }

                var expectedDistinctCount = allKeys.Distinct(StringComparer.Ordinal).Count();

                // Assert: result count equals distinct key count (deduplication works)
                return (result.Count == expectedDistinctCount).ToProperty();
            });
    }

    // --- Helper methods and arbitraries ---

    private static HashSet<string> ResolvePermissionsForTenant(
        Guid userId, Guid tenantId, SystemRoleGraph graph)
    {
        var userRoleRepository = Substitute.For<IUserRoleRepository>();
        var roleRepository = Substitute.For<IRoleRepository>();
        var policyRepository = Substitute.For<IPolicyRepository>();
        var tenantContext = Substitute.For<ITenantContext>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });

        tenantContext.TenantId.Returns(tenantId);

        // Default returns for unconfigured calls
        roleRepository.GetPoliciesForRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<PolicyDto>>.Ok(new List<PolicyDto>()));
        policyRepository.GetPermissionsForPolicyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<PermissionDto>>.Ok(new List<PermissionDto>()));

        // No tenant roles for this test — only system roles matter
        userRoleRepository.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));

        var systemRoleDtos = graph.SystemRoles
            .Select(r => new UserRoleDto(Guid.NewGuid(), userId, r.RoleId, Guid.Empty, r.RoleName))
            .ToList();
        userRoleRepository.GetSystemRolesForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(systemRoleDtos));

        foreach (var role in graph.SystemRoles)
        {
            var policyDtos = role.PolicyIds
                .Select(pid => new PolicyDto(pid, "Policy", null, tenantId))
                .ToList();
            roleRepository.GetPoliciesForRoleAsync(role.RoleId, Arg.Any<CancellationToken>())
                .Returns(OperationResult<List<PolicyDto>>.Ok(policyDtos));
        }

        foreach (var mapping in graph.PolicyPermissions)
        {
            var permDtos = mapping.PermissionKeys
                .Select(key => new PermissionDto(Guid.NewGuid(), key, key, null, "test"))
                .ToList();
            policyRepository.GetPermissionsForPolicyAsync(mapping.PolicyId, Arg.Any<CancellationToken>())
                .Returns(OperationResult<List<PermissionDto>>.Ok(permDtos));
        }

        var sut = new PermissionService(
            userRoleRepository, roleRepository, policyRepository,
            tenantContext, cache, options);

        return sut.GetUserPermissionsAsync(userId).GetAwaiter().GetResult();
    }

    private static Arbitrary<PermissionGraph> PermissionGraphArbitrary()
    {
        return Gen.Sized(size =>
        {
            var roleCount = Math.Max(1, size % 4 + 1); // 1-4 roles
            var policyCount = Math.Max(1, size % 3 + 1); // 1-3 policies per role
            var permCount = Math.Max(1, size % 4 + 1); // 1-4 permissions per policy

            return from tenantRoleCount in Gen.Choose(0, roleCount)
                   from systemRoleCount in Gen.Choose(0, roleCount)
                   let allPolicyIds = Enumerable.Range(0, (tenantRoleCount + systemRoleCount) * policyCount)
                       .Select(_ => Guid.NewGuid()).ToList()
                   from permKeys in Gen.ListOf(
                       (tenantRoleCount + systemRoleCount) * policyCount * permCount,
                       Gen.Elements("read", "write", "delete", "manage", "view", "create", "update", "admin"))
                   select BuildGraph(tenantRoleCount, systemRoleCount, policyCount, allPolicyIds, permKeys.ToList());
        }).ToArbitrary();
    }

    private static Arbitrary<SystemRoleGraph> SystemRoleGraphArbitrary()
    {
        return Gen.Sized(size =>
        {
            var roleCount = Math.Max(1, size % 3 + 1);
            var policyCount = Math.Max(1, size % 3 + 1);

            return from systemRoleCount in Gen.Choose(1, roleCount)
                   let allPolicyIds = Enumerable.Range(0, systemRoleCount * policyCount)
                       .Select(_ => Guid.NewGuid()).ToList()
                   from permKeys in Gen.ListOf(
                       systemRoleCount * policyCount * 2,
                       Gen.Elements("sys.read", "sys.write", "sys.admin", "sys.manage"))
                   select BuildSystemGraph(systemRoleCount, policyCount, allPolicyIds, permKeys.ToList());
        }).ToArbitrary();
    }

    private static Arbitrary<PermissionGraph> OverlappingPermissionGraphArbitrary()
    {
        // Generate graphs where tenant and system roles share some permission keys
        var sharedKeys = new[] { "shared.read", "shared.write", "shared.manage" };

        return Gen.Sized(size =>
        {
            return from tenantRoleCount in Gen.Choose(1, 2)
                   from systemRoleCount in Gen.Choose(1, 2)
                   let allPolicyIds = Enumerable.Range(0, (tenantRoleCount + systemRoleCount) * 2)
                       .Select(_ => Guid.NewGuid()).ToList()
                   from permKeys in Gen.ListOf(
                       (tenantRoleCount + systemRoleCount) * 2 * 2,
                       Gen.Elements(sharedKeys.Concat(new[] { "unique.a", "unique.b" }).ToArray()))
                   select BuildGraph(tenantRoleCount, systemRoleCount, 2, allPolicyIds, permKeys.ToList());
        }).ToArbitrary();
    }

    private static PermissionGraph BuildGraph(
        int tenantRoleCount, int systemRoleCount, int policiesPerRole,
        List<Guid> allPolicyIds, List<string> permKeys)
    {
        var tenantRoles = new List<RoleNode>();
        var systemRoles = new List<RoleNode>();
        var policyPermissions = new List<PolicyPermissionMapping>();
        var policyIndex = 0;
        var permIndex = 0;

        for (var i = 0; i < tenantRoleCount; i++)
        {
            var roleId = Guid.NewGuid();
            var policyIds = new List<Guid>();
            for (var j = 0; j < policiesPerRole && policyIndex < allPolicyIds.Count; j++)
            {
                policyIds.Add(allPolicyIds[policyIndex++]);
            }
            tenantRoles.Add(new RoleNode(roleId, policyIds, null));
        }

        for (var i = 0; i < systemRoleCount; i++)
        {
            var roleId = Guid.NewGuid();
            var policyIds = new List<Guid>();
            for (var j = 0; j < policiesPerRole && policyIndex < allPolicyIds.Count; j++)
            {
                policyIds.Add(allPolicyIds[policyIndex++]);
            }
            systemRoles.Add(new RoleNode(roleId, policyIds, $"SystemRole{i}"));
        }

        // Assign permission keys to policies
        foreach (var policyId in allPolicyIds)
        {
            var keys = new List<string>();
            var keysForPolicy = Math.Max(1, permKeys.Count > 0 ? 2 : 0);
            for (var k = 0; k < keysForPolicy && permIndex < permKeys.Count; k++)
            {
                keys.Add(permKeys[permIndex++]);
            }
            if (keys.Count > 0)
            {
                policyPermissions.Add(new PolicyPermissionMapping(policyId, keys));
            }
        }

        return new PermissionGraph(tenantRoles, systemRoles, policyPermissions);
    }

    private static SystemRoleGraph BuildSystemGraph(
        int systemRoleCount, int policiesPerRole,
        List<Guid> allPolicyIds, List<string> permKeys)
    {
        var systemRoles = new List<RoleNode>();
        var policyPermissions = new List<PolicyPermissionMapping>();
        var policyIndex = 0;
        var permIndex = 0;

        for (var i = 0; i < systemRoleCount; i++)
        {
            var roleId = Guid.NewGuid();
            var policyIds = new List<Guid>();
            for (var j = 0; j < policiesPerRole && policyIndex < allPolicyIds.Count; j++)
            {
                policyIds.Add(allPolicyIds[policyIndex++]);
            }
            systemRoles.Add(new RoleNode(roleId, policyIds, $"SysRole{i}"));
        }

        foreach (var policyId in allPolicyIds)
        {
            var keys = new List<string>();
            var keysForPolicy = Math.Max(1, permKeys.Count > 0 ? 2 : 0);
            for (var k = 0; k < keysForPolicy && permIndex < permKeys.Count; k++)
            {
                keys.Add(permKeys[permIndex++]);
            }
            if (keys.Count > 0)
            {
                policyPermissions.Add(new PolicyPermissionMapping(policyId, keys));
            }
        }

        return new SystemRoleGraph(systemRoles, policyPermissions);
    }

    // --- Data models for property tests ---

    private sealed record RoleNode(Guid RoleId, List<Guid> PolicyIds, string? RoleName);
    private sealed record PolicyPermissionMapping(Guid PolicyId, List<string> PermissionKeys);
    private sealed record PermissionGraph(List<RoleNode> TenantRoles, List<RoleNode> SystemRoles, List<PolicyPermissionMapping> PolicyPermissions);
    private sealed record SystemRoleGraph(List<RoleNode> SystemRoles, List<PolicyPermissionMapping> PolicyPermissions);
}
