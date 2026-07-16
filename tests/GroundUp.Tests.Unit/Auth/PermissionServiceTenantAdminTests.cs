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
/// Unit tests for the TenantAdmin bypass logic in PermissionService.
/// Validates Requirements 10.4–10.7.
/// </summary>
public sealed class PermissionServiceTenantAdminTests
{
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPolicyRepository _policyRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly PermissionService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();

    public PermissionServiceTenantAdminTests()
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

        // Default: no system roles (SuperAdmin check returns empty)
        SetupSystemRolesEmpty();
    }

    // --- Bypass fires for TenantAdmin in current tenant ---

    [Fact]
    public async Task HasPermissionAsync_TenantAdminInCurrentTenant_ReturnsTrue()
    {
        // Arrange
        SetupTenantAdminInTenant(_tenantId);

        // Act
        var result = await _sut.HasPermissionAsync(_userId, "any.permission");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasAnyPermissionAsync_TenantAdminInCurrentTenant_ReturnsTrue()
    {
        // Arrange
        SetupTenantAdminInTenant(_tenantId);

        // Act
        var result = await _sut.HasAnyPermissionAsync(_userId, new[] { "some.permission", "other.permission" });

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasPermissionAsync_TenantAdminInCurrentTenant_ReturnsTrueForAnyPermissionKey()
    {
        // Arrange — TenantAdmin bypass should work regardless of the permission being checked
        SetupTenantAdminInTenant(_tenantId);

        // Act & Assert
        Assert.True(await _sut.HasPermissionAsync(_userId, "settings.delete"));
        Assert.True(await _sut.HasPermissionAsync(_userId, "admin.manage-users"));
        Assert.True(await _sut.HasPermissionAsync(_userId, "billing.view-invoices"));
    }

    // --- Bypass does NOT fire for TenantAdmin in different tenant ---

    [Fact]
    public async Task HasPermissionAsync_TenantAdminInDifferentTenant_ReturnsFalse()
    {
        // Arrange — user holds TenantAdmin but in a different tenant
        var differentTenantId = Guid.NewGuid();
        SetupTenantAdminInTenant(differentTenantId);

        // The tenant-scoped query for the CURRENT tenant returns no TenantAdmin
        _userRoleRepository.GetByUserIdForTenantAsync(_userId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));

        // No permissions granted via normal path
        SetupTenantRolesEmpty();

        // Act
        var result = await _sut.HasPermissionAsync(_userId, "any.permission");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasAnyPermissionAsync_TenantAdminInDifferentTenant_ReturnsFalse()
    {
        // Arrange — user holds TenantAdmin but in a different tenant
        var differentTenantId = Guid.NewGuid();
        SetupTenantAdminInTenant(differentTenantId);

        // The tenant-scoped query for the CURRENT tenant returns no TenantAdmin
        _userRoleRepository.GetByUserIdForTenantAsync(_userId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));

        // No permissions granted via normal path
        SetupTenantRolesEmpty();

        // Act
        var result = await _sut.HasAnyPermissionAsync(_userId, new[] { "some.permission", "other.permission" });

        // Assert
        Assert.False(result);
    }

    // --- Bypass does NOT fire for non-TenantAdmin roles ---

    [Fact]
    public async Task HasPermissionAsync_UserHasRegularRoleButNotTenantAdmin_ReturnsFalse()
    {
        // Arrange — user has a role in the current tenant, but it's not TenantAdmin
        var regularRoleId = Guid.NewGuid();
        SetupNonTenantAdminRolesInCurrentTenant("Editor", "Viewer");

        // No permissions via the normal resolution path
        SetupTenantRolesEmpty();

        // Act
        var result = await _sut.HasPermissionAsync(_userId, "some.ungranted.permission");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasAnyPermissionAsync_UserHasRegularRoleButNotTenantAdmin_ReturnsFalse()
    {
        // Arrange — user has roles but none are TenantAdmin
        SetupNonTenantAdminRolesInCurrentTenant("Manager");

        // No permissions via the normal resolution path
        SetupTenantRolesEmpty();

        // Act
        var result = await _sut.HasAnyPermissionAsync(_userId, new[] { "admin.manage", "billing.view" });

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_NoRolesAtAll_ReturnsFalse()
    {
        // Arrange — user has no roles in the current tenant
        _userRoleRepository.GetByUserIdForTenantAsync(_userId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));

        // No permissions via the normal resolution path
        SetupTenantRolesEmpty();

        // Act
        var result = await _sut.HasPermissionAsync(_userId, "any.permission");

        // Assert
        Assert.False(result);
    }

    // --- Bypass short-circuits before cache (no calls to _roleRepository or _policyRepository) ---

    [Fact]
    public async Task HasPermissionAsync_TenantAdminBypass_DoesNotCallRoleRepository()
    {
        // Arrange
        SetupTenantAdminInTenant(_tenantId);

        // Act
        await _sut.HasPermissionAsync(_userId, "any.permission");

        // Assert — _roleRepository should never be called because the bypass short-circuits
        await _roleRepository.DidNotReceive().GetPoliciesForRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HasPermissionAsync_TenantAdminBypass_DoesNotCallPolicyRepository()
    {
        // Arrange
        SetupTenantAdminInTenant(_tenantId);

        // Act
        await _sut.HasPermissionAsync(_userId, "any.permission");

        // Assert — _policyRepository should never be called because the bypass short-circuits
        await _policyRepository.DidNotReceive().GetPermissionsForPolicyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HasAnyPermissionAsync_TenantAdminBypass_DoesNotCallRoleOrPolicyRepositories()
    {
        // Arrange
        SetupTenantAdminInTenant(_tenantId);

        // Act
        await _sut.HasAnyPermissionAsync(_userId, new[] { "a.b", "c.d" });

        // Assert — neither repository should be called
        await _roleRepository.DidNotReceive().GetPoliciesForRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _policyRepository.DidNotReceive().GetPermissionsForPolicyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HasPermissionAsync_TenantAdminBypass_DoesNotQueryTenantRolesForPermissionResolution()
    {
        // Arrange — TenantAdmin bypass should not call the tenant-scoped GetByUserIdAsync
        // which is used for permission resolution (distinct from GetByUserIdForTenantAsync used for the bypass check)
        SetupTenantAdminInTenant(_tenantId);

        // Act
        await _sut.HasPermissionAsync(_userId, "any.permission");

        // Assert — GetByUserIdAsync (used by GetUserPermissionsAsync) should NOT be called
        await _userRoleRepository.DidNotReceive().GetByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HasPermissionAsync_TenantAdminBypass_DoesNotPopulateCache()
    {
        // Arrange
        SetupTenantAdminInTenant(_tenantId);

        // Act
        await _sut.HasPermissionAsync(_userId, "any.permission");

        // Assert — cache should remain empty (bypass is never cached)
        var cacheKey = $"permissions:{_userId}:{_tenantId}";
        Assert.False(_cache.TryGetValue(cacheKey, out _));
    }

    // --- Edge case: TenantId is Guid.Empty (no tenant context, e.g., pending-selection) ---

    [Fact]
    public async Task HasPermissionAsync_NoTenantContext_TenantAdminBypassSkipped()
    {
        // Arrange — TenantId is Guid.Empty (pending-selection state)
        _tenantContext.TenantId.Returns(Guid.Empty);

        // No permissions via normal path
        SetupTenantRolesEmpty();

        // Act
        var result = await _sut.HasPermissionAsync(_userId, "any.permission");

        // Assert — bypass does not fire when there's no tenant context
        Assert.False(result);
        // GetByUserIdForTenantAsync should not be called when TenantId is empty
        await _userRoleRepository.DidNotReceive()
            .GetByUserIdForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // --- Helper methods ---

    private void SetupTenantAdminInTenant(Guid tenantId)
    {
        var tenantAdminRole = new UserRoleDto(
            Guid.NewGuid(), _userId, Guid.NewGuid(), tenantId, AuthRoleNames.TenantAdmin);

        _userRoleRepository.GetByUserIdForTenantAsync(_userId, tenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto> { tenantAdminRole }));
    }

    private void SetupNonTenantAdminRolesInCurrentTenant(params string[] roleNames)
    {
        var roles = roleNames.Select(name =>
            new UserRoleDto(Guid.NewGuid(), _userId, Guid.NewGuid(), _tenantId, name)).ToList();

        _userRoleRepository.GetByUserIdForTenantAsync(_userId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(roles));
    }

    private void SetupSystemRolesEmpty()
    {
        _userRoleRepository.GetSystemRolesForUserAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));
    }

    private void SetupTenantRolesEmpty()
    {
        _userRoleRepository.GetByUserIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));
    }
}
