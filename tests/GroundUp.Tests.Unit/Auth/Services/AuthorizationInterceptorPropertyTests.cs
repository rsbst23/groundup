using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Authorization;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Attributes;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services;

/// <summary>
/// Property-based tests for <see cref="AuthorizationInterceptor{TInterface}"/>.
/// Feature: phase-9c-permission-service
/// </summary>
[Trait("Category", "Property")]
public sealed class AuthorizationInterceptorPropertyTests
{
    /// <summary>
    /// Feature: phase-9c-permission-service, Property 4:
    /// Authorization proxy enforces AND semantics for [RequiresPermission].
    /// For any service interface method decorated with [RequiresPermission(perm1, perm2, ...)]
    /// and any user with a resolved permission set P, the proxy allows iff required ⊆ P.
    /// **Validates: Requirements 4.1, 4.2, 4.3**
    /// </summary>
    [Property(MaxTest = 200, DisplayName = "Feature: phase-9c-permission-service, Property 4: AND semantics for RequiresPermission")]
    public Property RequiresPermission_EnforcesAndSemantics()
    {
        return Prop.ForAll(
            PermissionScenarioArbitrary(),
            scenario =>
            {
                // Arrange
                var userId = Guid.NewGuid();
                var permissionService = Substitute.For<IPermissionService>();
                var currentUser = Substitute.For<ICurrentUser>();
                var implementation = Substitute.For<IPermissionTestService>();

                currentUser.UserId.Returns(userId);
                permissionService.GetUserPermissionsAsync(userId, Arg.Any<CancellationToken>())
                    .Returns(new HashSet<string>(scenario.UserPermissions, StringComparer.Ordinal));

                implementation.RequiresAllAsync()
                    .Returns(OperationResult<string>.Ok("success"));

                var proxy = AuthorizationInterceptor<IPermissionTestService>.Create(
                    implementation, permissionService, currentUser);

                // Act
                var result = proxy.RequiresAllAsync().GetAwaiter().GetResult();

                // The required permissions are "perm.a" and "perm.b" (defined on the interface)
                var required = new HashSet<string>(StringComparer.Ordinal) { "perm.a", "perm.b" };
                var userPerms = new HashSet<string>(scenario.UserPermissions, StringComparer.Ordinal);
                var shouldAllow = required.IsSubsetOf(userPerms);

                // Assert
                if (shouldAllow)
                {
                    return (result.Success && result.Data == "success").ToProperty();
                }
                else
                {
                    return (!result.Success && result.StatusCode == 403).ToProperty();
                }
            });
    }

    /// <summary>
    /// Feature: phase-9c-permission-service, Property 5:
    /// Authorization proxy enforces OR semantics for [RequiresRole] using system roles only.
    /// For any service interface method decorated with [RequiresRole(role1, role2, ...)]
    /// and any user with system-level roles S, the proxy allows iff intersection is non-empty (case-insensitive).
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.5**
    /// </summary>
    [Property(MaxTest = 200, DisplayName = "Feature: phase-9c-permission-service, Property 5: OR semantics for RequiresRole")]
    public Property RequiresRole_EnforcesOrSemantics()
    {
        return Prop.ForAll(
            RoleScenarioArbitrary(),
            scenario =>
            {
                // Arrange
                var userId = Guid.NewGuid();
                var permissionService = Substitute.For<IPermissionService>();
                var currentUser = Substitute.For<ICurrentUser>();
                var implementation = Substitute.For<IRoleTestService>();

                currentUser.UserId.Returns(userId);

                // The required roles are "RoleAlpha" and "RoleBeta" (defined on the interface)
                var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RoleAlpha", "RoleBeta" };
                var userRoles = new HashSet<string>(scenario.UserSystemRoles, StringComparer.OrdinalIgnoreCase);
                var shouldAllow = required.Overlaps(userRoles);

                permissionService.HasAnySystemRoleAsync(userId, Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                    .Returns(shouldAllow);

                implementation.RequiresAnyRoleAsync()
                    .Returns(OperationResult<string>.Ok("role-success"));

                var proxy = AuthorizationInterceptor<IRoleTestService>.Create(
                    implementation, permissionService, currentUser);

                // Act
                var result = proxy.RequiresAnyRoleAsync().GetAwaiter().GetResult();

                // Assert
                if (shouldAllow)
                {
                    return (result.Success && result.Data == "role-success").ToProperty();
                }
                else
                {
                    return (!result.Success && result.StatusCode == 403).ToProperty();
                }
            });
    }

    /// <summary>
    /// Feature: phase-9c-permission-service, Property 6:
    /// Role name comparison is case-insensitive.
    /// For any role name string and any case variation, the [RequiresRole] check treats them as equivalent.
    /// **Validates: Requirements 5.6**
    /// </summary>
    [Property(MaxTest = 200, DisplayName = "Feature: phase-9c-permission-service, Property 6: Case-insensitive role comparison")]
    public Property RoleNameComparison_IsCaseInsensitive()
    {
        return Prop.ForAll(
            CaseVariationArbitrary(),
            variation =>
            {
                // Arrange
                var userId = Guid.NewGuid();
                var permissionService = Substitute.For<IPermissionService>();
                var currentUser = Substitute.For<ICurrentUser>();
                var implementation = Substitute.For<IRoleTestService>();

                currentUser.UserId.Returns(userId);

                // The interface requires "RoleAlpha" or "RoleBeta"
                // We test that HasAnySystemRoleAsync is called and the service does case-insensitive matching
                // The user has a case-varied version of "RoleAlpha"
                var userRole = variation.CaseVariedRoleName;

                // HasAnySystemRoleAsync should match case-insensitively
                permissionService.HasAnySystemRoleAsync(userId, Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        var requiredRoles = callInfo.ArgAt<IEnumerable<string>>(1);
                        var requiredSet = new HashSet<string>(requiredRoles, StringComparer.OrdinalIgnoreCase);
                        return requiredSet.Contains(userRole);
                    });

                implementation.RequiresAnyRoleAsync()
                    .Returns(OperationResult<string>.Ok("ok"));

                var proxy = AuthorizationInterceptor<IRoleTestService>.Create(
                    implementation, permissionService, currentUser);

                // Act
                var result = proxy.RequiresAnyRoleAsync().GetAwaiter().GetResult();

                // The user's role is a case variation of "RoleAlpha" which is in the required set
                // So it should always be allowed (case-insensitive match)
                var shouldMatch = string.Equals("RoleAlpha", userRole, StringComparison.OrdinalIgnoreCase)
                    || string.Equals("RoleBeta", userRole, StringComparison.OrdinalIgnoreCase);

                if (shouldMatch)
                {
                    return (result.Success).ToProperty();
                }
                else
                {
                    return (!result.Success && result.StatusCode == 403).ToProperty();
                }
            });
    }

    /// <summary>
    /// Feature: phase-9c-permission-service, Property 7:
    /// Proxy pass-through for undecorated methods.
    /// For any user permission state, invoking undecorated methods always passes through.
    /// **Validates: Requirements 4.6, 6.3**
    /// </summary>
    [Property(MaxTest = 200, DisplayName = "Feature: phase-9c-permission-service, Property 7: Pass-through for undecorated methods")]
    public Property UndecoratedMethods_AlwaysPassThrough()
    {
        return Prop.ForAll(
            Arb.From<NonEmptyArray<string>>(),
            randomPerms =>
            {
                // Arrange
                var userId = Guid.NewGuid();
                var permissionService = Substitute.For<IPermissionService>();
                var currentUser = Substitute.For<ICurrentUser>();
                var implementation = Substitute.For<IPassThroughTestService>();

                currentUser.UserId.Returns(userId);

                // Give the user random permissions (shouldn't matter for undecorated methods)
                var userPermissions = new HashSet<string>(
                    randomPerms.Get.Where(s => s != null),
                    StringComparer.Ordinal);
                permissionService.GetUserPermissionsAsync(userId, Arg.Any<CancellationToken>())
                    .Returns(userPermissions);

                implementation.UndecoratedMethodAsync()
                    .Returns(OperationResult<string>.Ok("pass-through"));

                var proxy = AuthorizationInterceptor<IPassThroughTestService>.Create(
                    implementation, permissionService, currentUser);

                // Act
                var result = proxy.UndecoratedMethodAsync().GetAwaiter().GetResult();

                // Assert — undecorated methods always pass through regardless of permissions
                return (result.Success && result.Data == "pass-through").ToProperty();
            });
    }

    // --- Test interfaces ---

    public interface IPermissionTestService
    {
        [RequiresPermission("perm.a", "perm.b")]
        Task<OperationResult<string>> RequiresAllAsync();
    }

    public interface IRoleTestService
    {
        [RequiresRole("RoleAlpha", "RoleBeta")]
        Task<OperationResult<string>> RequiresAnyRoleAsync();
    }

    public interface IPassThroughTestService
    {
        Task<OperationResult<string>> UndecoratedMethodAsync();
    }

    // --- Arbitraries ---

    private static Arbitrary<PermissionScenario> PermissionScenarioArbitrary()
    {
        // Generate random user permission sets (0-10 permissions)
        // from a pool that includes the required ones ("perm.a", "perm.b") and extras
        var allPossiblePerms = new[] { "perm.a", "perm.b", "perm.c", "perm.d", "perm.e", "other.x", "other.y", "other.z" };

        var gen = from count in Gen.Choose(0, 8)
                  from perms in Gen.ArrayOf(count, Gen.Elements(allPossiblePerms))
                  select new PermissionScenario(perms.Distinct().ToList());

        return gen.ToArbitrary();
    }

    private static Arbitrary<RoleScenario> RoleScenarioArbitrary()
    {
        // Generate random user system role sets (0-5 roles)
        // from a pool that includes the required ones and extras
        var allPossibleRoles = new[] { "RoleAlpha", "RoleBeta", "RoleGamma", "RoleDelta", "RoleEpsilon", "Unrelated" };

        var gen = from count in Gen.Choose(0, 5)
                  from roles in Gen.ArrayOf(count, Gen.Elements(allPossibleRoles))
                  select new RoleScenario(roles.Distinct().ToList());

        return gen.ToArbitrary();
    }

    private static Arbitrary<CaseVariation> CaseVariationArbitrary()
    {
        // Generate case variations of known role names
        var baseRoles = new[] { "RoleAlpha", "RoleBeta" };

        var gen = from baseRole in Gen.Elements(baseRoles)
                  from transform in Gen.Choose(0, 3)
                  select new CaseVariation(ApplyCaseTransform(baseRole, transform));

        return gen.ToArbitrary();
    }

    private static string ApplyCaseTransform(string input, int transform)
    {
        return transform switch
        {
            0 => input.ToUpperInvariant(),
            1 => input.ToLowerInvariant(),
            2 => string.Concat(input.Select((c, i) => i % 2 == 0 ? char.ToUpper(c) : char.ToLower(c))),
            _ => input // original case
        };
    }

    // --- Data models ---

    private sealed record PermissionScenario(List<string> UserPermissions);
    private sealed record RoleScenario(List<string> UserSystemRoles);
    private sealed record CaseVariation(string CaseVariedRoleName);
}
