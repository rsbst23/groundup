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

public sealed class PermissionServiceTests
{
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPolicyRepository _policyRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly PermissionService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();

    public PermissionServiceTests()
    {
        _userRoleRepository = Substitute.For<IUserRoleRepository>();
        _roleRepository = Substitute.For<IRoleRepository>();
        _policyRepository = Substitute.For<IPolicyRepository>();
        _tenantContext = Substitute.For<ITenantContext>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        _tenantContext.TenantId.Returns(_tenantId);

        var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });

        _sut = new PermissionService(
            _userRoleRepository,
            _roleRepository,
            _policyRepository,
            _tenantContext,
            _cache,
            options);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_FirstCall_ResolvesFromRepositories()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var policyId = Guid.NewGuid();

        SetupTenantRoles(roleId);
        SetupSystemRolesEmpty();
        SetupPoliciesForRole(roleId, policyId);
        SetupPermissionsForPolicy(policyId, "settings.read", "settings.write");

        // Act
        var result = await _sut.GetUserPermissionsAsync(_userId);

        // Assert
        Assert.Contains("settings.read", result);
        Assert.Contains("settings.write", result);
        await _userRoleRepository.Received(1).GetByUserIdAsync(_userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserPermissionsAsync_SecondCall_UsesCacheWithoutRepositoryCalls()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var policyId = Guid.NewGuid();

        SetupTenantRoles(roleId);
        SetupSystemRolesEmpty();
        SetupPoliciesForRole(roleId, policyId);
        SetupPermissionsForPolicy(policyId, "settings.read");

        // Act
        await _sut.GetUserPermissionsAsync(_userId);
        _userRoleRepository.ClearReceivedCalls();
        _roleRepository.ClearReceivedCalls();
        _policyRepository.ClearReceivedCalls();

        var result = await _sut.GetUserPermissionsAsync(_userId);

        // Assert
        Assert.Contains("settings.read", result);
        await _userRoleRepository.DidNotReceive().GetByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserPermissionsAsync_CacheKeyFormat_IsCorrect()
    {
        // Arrange
        SetupTenantRolesEmpty();
        SetupSystemRolesEmpty();

        // Act
        await _sut.GetUserPermissionsAsync(_userId);

        // Assert — verify the cache key format by checking the cache directly
        var cacheKey = $"permissions:{_userId}:{_tenantId}";
        Assert.True(_cache.TryGetValue(cacheKey, out HashSet<string>? cached));
        Assert.NotNull(cached);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_UserHasNoRoles_ReturnsEmptySet()
    {
        // Arrange
        SetupTenantRolesEmpty();
        SetupSystemRolesEmpty();

        // Act
        var result = await _sut.GetUserPermissionsAsync(_userId);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_SystemRolesIncludedRegardlessOfTenant()
    {
        // Arrange
        var systemRoleId = Guid.NewGuid();
        var policyId = Guid.NewGuid();

        SetupTenantRolesEmpty();
        SetupSystemRoles(systemRoleId, "Admin");
        SetupPoliciesForRole(systemRoleId, policyId);
        SetupPermissionsForPolicy(policyId, "admin.manage");

        // Act
        var result = await _sut.GetUserPermissionsAsync(_userId);

        // Assert
        Assert.Contains("admin.manage", result);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_DeduplicatesOverlappingPermissions()
    {
        // Arrange
        var roleId1 = Guid.NewGuid();
        var roleId2 = Guid.NewGuid();
        var policyId1 = Guid.NewGuid();
        var policyId2 = Guid.NewGuid();

        // Tenant roles grant "settings.read"
        SetupTenantRoles(roleId1);
        SetupPoliciesForRole(roleId1, policyId1);
        SetupPermissionsForPolicy(policyId1, "settings.read", "settings.write");

        // System roles also grant "settings.read"
        SetupSystemRoles(roleId2, "Admin");
        SetupPoliciesForRole(roleId2, policyId2);
        SetupPermissionsForPolicy(policyId2, "settings.read", "admin.manage");

        // Act
        var result = await _sut.GetUserPermissionsAsync(_userId);

        // Assert — "settings.read" appears only once in the set
        Assert.Equal(3, result.Count); // settings.read, settings.write, admin.manage
    }

    [Fact]
    public async Task HasPermissionAsync_UserHasPermission_ReturnsTrue()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var policyId = Guid.NewGuid();

        SetupTenantRoles(roleId);
        SetupSystemRolesEmpty();
        SetupPoliciesForRole(roleId, policyId);
        SetupPermissionsForPolicy(policyId, "settings.read");

        // Act
        var result = await _sut.HasPermissionAsync(_userId, "settings.read");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasPermissionAsync_UserLacksPermission_ReturnsFalse()
    {
        // Arrange
        SetupTenantRolesEmpty();
        SetupSystemRolesEmpty();

        // Act
        var result = await _sut.HasPermissionAsync(_userId, "settings.read");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasAnyPermissionAsync_UserHasOneOfMany_ReturnsTrue()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var policyId = Guid.NewGuid();

        SetupTenantRoles(roleId);
        SetupSystemRolesEmpty();
        SetupPoliciesForRole(roleId, policyId);
        SetupPermissionsForPolicy(policyId, "settings.read");

        // Act
        var result = await _sut.HasAnyPermissionAsync(_userId, new[] { "settings.read", "settings.write" });

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasAnyPermissionAsync_UserHasNone_ReturnsFalse()
    {
        // Arrange
        SetupTenantRolesEmpty();
        SetupSystemRolesEmpty();

        // Act
        var result = await _sut.HasAnyPermissionAsync(_userId, new[] { "settings.read", "settings.write" });

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasSystemRoleAsync_UserHasRole_ReturnsTrue()
    {
        // Arrange
        SetupSystemRoles(Guid.NewGuid(), "Admin");

        // Act
        var result = await _sut.HasSystemRoleAsync(_userId, "Admin");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasSystemRoleAsync_CaseInsensitiveComparison_ReturnsTrue()
    {
        // Arrange
        SetupSystemRoles(Guid.NewGuid(), "Admin");

        // Act
        var result = await _sut.HasSystemRoleAsync(_userId, "admin");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasSystemRoleAsync_UserLacksRole_ReturnsFalse()
    {
        // Arrange
        SetupSystemRolesEmpty();

        // Act
        var result = await _sut.HasSystemRoleAsync(_userId, "Admin");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasAnySystemRoleAsync_UserHasOneOfMany_ReturnsTrue()
    {
        // Arrange
        SetupSystemRoles(Guid.NewGuid(), "Admin");

        // Act
        var result = await _sut.HasAnySystemRoleAsync(_userId, new[] { "Admin", "SuperAdmin" });

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasAnySystemRoleAsync_CaseInsensitiveComparison_ReturnsTrue()
    {
        // Arrange
        SetupSystemRoles(Guid.NewGuid(), "Admin");

        // Act
        var result = await _sut.HasAnySystemRoleAsync(_userId, new[] { "ADMIN", "SUPERADMIN" });

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasAnySystemRoleAsync_UserHasNone_ReturnsFalse()
    {
        // Arrange
        SetupSystemRolesEmpty();

        // Act
        var result = await _sut.HasAnySystemRoleAsync(_userId, new[] { "Admin", "SuperAdmin" });

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_RepositoryReturnsFailure_SkipsGracefully()
    {
        // Arrange — tenant roles call fails, system roles succeed
        _userRoleRepository.GetByUserIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Fail("DB error", 500));

        var systemRoleId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        SetupSystemRoles(systemRoleId, "Admin");
        SetupPoliciesForRole(systemRoleId, policyId);
        SetupPermissionsForPolicy(policyId, "admin.manage");

        // Act
        var result = await _sut.GetUserPermissionsAsync(_userId);

        // Assert — should still return system role permissions despite tenant role failure
        Assert.Contains("admin.manage", result);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_GetPoliciesForRoleFails_SkipsRole()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        SetupTenantRoles(roleId);
        SetupSystemRolesEmpty();

        // GetPoliciesForRoleAsync returns failure
        _roleRepository.GetPoliciesForRoleAsync(roleId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<PolicyDto>>.Fail("Not found", 404));

        // Act
        var result = await _sut.GetUserPermissionsAsync(_userId);

        // Assert — empty set, no exception thrown
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_GetPermissionsForPolicyFails_SkipsPolicy()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        SetupTenantRoles(roleId);
        SetupSystemRolesEmpty();
        SetupPoliciesForRole(roleId, policyId);

        // GetPermissionsForPolicyAsync returns failure
        _policyRepository.GetPermissionsForPolicyAsync(policyId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<PermissionDto>>.Fail("Error", 500));

        // Act
        var result = await _sut.GetUserPermissionsAsync(_userId);

        // Assert — empty set, no exception thrown
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_MultipleRolesMultiplePolicies_ResolvesAllPermissions()
    {
        // Arrange — 2 tenant roles, each with 2 policies, each policy with 2 permissions
        var roleId1 = Guid.NewGuid();
        var roleId2 = Guid.NewGuid();
        var policyId1A = Guid.NewGuid();
        var policyId1B = Guid.NewGuid();
        var policyId2A = Guid.NewGuid();
        var policyId2B = Guid.NewGuid();

        SetupTenantRoles(roleId1, roleId2);
        SetupSystemRolesEmpty();

        // Role 1 has policies 1A and 1B
        SetupPoliciesForRole(roleId1, policyId1A, policyId1B);
        // Role 2 has policies 2A and 2B
        SetupPoliciesForRole(roleId2, policyId2A, policyId2B);

        SetupPermissionsForPolicy(policyId1A, "orders.read", "orders.write");
        SetupPermissionsForPolicy(policyId1B, "customers.read");
        SetupPermissionsForPolicy(policyId2A, "settings.read", "settings.write");
        SetupPermissionsForPolicy(policyId2B, "reports.view");

        // Act
        var result = await _sut.GetUserPermissionsAsync(_userId);

        // Assert — all 6 unique permissions resolved
        Assert.Equal(6, result.Count);
        Assert.Contains("orders.read", result);
        Assert.Contains("orders.write", result);
        Assert.Contains("customers.read", result);
        Assert.Contains("settings.read", result);
        Assert.Contains("settings.write", result);
        Assert.Contains("reports.view", result);
    }

    // --- Helper methods ---

    private void SetupTenantRoles(params Guid[] roleIds)
    {
        var roles = roleIds.Select(id => new UserRoleDto(Guid.NewGuid(), _userId, id, _tenantId)).ToList();
        _userRoleRepository.GetByUserIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(roles));
    }

    private void SetupTenantRolesEmpty()
    {
        _userRoleRepository.GetByUserIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));
    }

    private void SetupSystemRoles(Guid roleId, string roleName)
    {
        var roles = new List<UserRoleDto>
        {
            new(Guid.NewGuid(), _userId, roleId, Guid.Empty, roleName)
        };
        _userRoleRepository.GetSystemRolesForUserAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(roles));
    }

    private void SetupSystemRolesEmpty()
    {
        _userRoleRepository.GetSystemRolesForUserAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));
    }

    private void SetupPoliciesForRole(Guid roleId, params Guid[] policyIds)
    {
        var policies = policyIds.Select(id => new PolicyDto(id, "Policy", null, _tenantId)).ToList();
        _roleRepository.GetPoliciesForRoleAsync(roleId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<PolicyDto>>.Ok(policies));
    }

    private void SetupPermissionsForPolicy(Guid policyId, params string[] permissionKeys)
    {
        var permissions = permissionKeys.Select(key => new PermissionDto(Guid.NewGuid(), key, key, null, "test")).ToList();
        _policyRepository.GetPermissionsForPolicyAsync(policyId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<PermissionDto>>.Ok(permissions));
    }
}
