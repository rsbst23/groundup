using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Authorization;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Attributes;
using GroundUp.Core.Exceptions;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services;

public sealed class AuthorizationInterceptorTests
{
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUser _currentUser;
    private readonly ITestService _implementation;
    private readonly ITestService _proxy;

    private readonly Guid _userId = Guid.NewGuid();

    public AuthorizationInterceptorTests()
    {
        _permissionService = Substitute.For<IPermissionService>();
        _currentUser = Substitute.For<ICurrentUser>();
        _implementation = Substitute.For<ITestService>();

        _currentUser.UserId.Returns(_userId);

        _proxy = AuthorizationInterceptor<ITestService>.Create(
            _implementation,
            _permissionService,
            _currentUser);
    }

    [Fact]
    public async Task PermissionProtectedMethod_UserLacksPermission_ReturnsForbidden()
    {
        // Arrange
        _permissionService.GetUserPermissionsAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        // Act
        var result = await _proxy.GetProtectedDataAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task PermissionProtectedMethod_UserHasPermissions_InvokesMethod()
    {
        // Arrange
        _permissionService.GetUserPermissionsAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { "data.read", "data.write" });
        _implementation.GetProtectedDataAsync()
            .Returns(OperationResult<string>.Ok("success"));

        // Act
        var result = await _proxy.GetProtectedDataAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal("success", result.Data);
        await _implementation.Received(1).GetProtectedDataAsync();
    }

    [Fact]
    public async Task RoleProtectedMethod_UserLacksRole_ReturnsForbidden()
    {
        // Arrange
        _permissionService.HasAnySystemRoleAsync(_userId, Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _proxy.GetAdminDataAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task RoleProtectedMethod_UserHasRole_InvokesMethod()
    {
        // Arrange
        _permissionService.HasAnySystemRoleAsync(_userId, Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _implementation.GetAdminDataAsync()
            .Returns(OperationResult<string>.Ok("admin data"));

        // Act
        var result = await _proxy.GetAdminDataAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal("admin data", result.Data);
        await _implementation.Received(1).GetAdminDataAsync();
    }

    [Fact]
    public async Task UndecoratedMethod_AlwaysPassesThrough()
    {
        // Arrange
        _implementation.GetPublicDataAsync()
            .Returns(OperationResult<string>.Ok("public"));

        // Act
        var result = await _proxy.GetPublicDataAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal("public", result.Data);
        await _implementation.Received(1).GetPublicDataAsync();
    }

    [Fact]
    public async Task NonOperationResultMethod_UserLacksPermission_ThrowsForbiddenAccessException()
    {
        // Arrange
        _permissionService.GetUserPermissionsAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => _proxy.GetNonResultProtectedAsync());
    }

    [Fact]
    public async Task NonOperationResultMethod_UserHasPermission_InvokesMethod()
    {
        // Arrange
        _permissionService.GetUserPermissionsAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { "data.read" });
        _implementation.GetNonResultProtectedAsync()
            .Returns("hello");

        // Act
        var result = await _proxy.GetNonResultProtectedAsync();

        // Assert
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task GuidEmptyUserId_ReturnsForbidden()
    {
        // Arrange
        _currentUser.UserId.Returns(Guid.Empty);

        var proxy = AuthorizationInterceptor<ITestService>.Create(
            _implementation,
            _permissionService,
            _currentUser);

        // Act
        var result = await proxy.GetProtectedDataAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task RoleProtectedMethod_CaseInsensitiveComparison_InvokesMethod()
    {
        // Arrange — the attribute specifies "Admin" but we verify the service is called with the attribute values
        _permissionService.HasAnySystemRoleAsync(_userId, Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _implementation.GetAdminDataAsync()
            .Returns(OperationResult<string>.Ok("admin data"));

        // Act
        var result = await _proxy.GetAdminDataAsync();

        // Assert
        Assert.True(result.Success);
        await _permissionService.Received(1).HasAnySystemRoleAsync(
            _userId,
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonGenericOperationResult_UserLacksPermission_ReturnsForbidden()
    {
        // Arrange
        _permissionService.GetUserPermissionsAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        // Act
        var result = await _proxy.DeleteProtectedAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task NonGenericOperationResult_UserHasPermission_InvokesMethod()
    {
        // Arrange
        _permissionService.GetUserPermissionsAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { "data.delete" });
        _implementation.DeleteProtectedAsync()
            .Returns(OperationResult.Ok());

        // Act
        var result = await _proxy.DeleteProtectedAsync();

        // Assert
        Assert.True(result.Success);
        await _implementation.Received(1).DeleteProtectedAsync();
    }

    [Fact]
    public async Task BothAttributesOnMethod_UserHasRoleButLacksPermission_ReturnsForbidden()
    {
        // Arrange — user has the required role but NOT the required permission
        _permissionService.HasAnySystemRoleAsync(_userId, Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _permissionService.GetUserPermissionsAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>()); // no permissions

        // Act
        var result = await _proxy.GetDualProtectedAsync();

        // Assert — should be forbidden because permission check fails (AND between attributes)
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task BothAttributesOnMethod_UserHasPermissionButLacksRole_ReturnsForbidden()
    {
        // Arrange — user has the required permission but NOT the required role
        _permissionService.HasAnySystemRoleAsync(_userId, Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _proxy.GetDualProtectedAsync();

        // Assert — should be forbidden because role check fails first (AND between attributes)
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task BothAttributesOnMethod_UserHasBoth_InvokesMethod()
    {
        // Arrange — user has both the required role AND the required permission
        _permissionService.HasAnySystemRoleAsync(_userId, Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _permissionService.GetUserPermissionsAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { "admin.execute" });
        _implementation.GetDualProtectedAsync()
            .Returns(OperationResult<string>.Ok("dual protected data"));

        // Act
        var result = await _proxy.GetDualProtectedAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal("dual protected data", result.Data);
        await _implementation.Received(1).GetDualProtectedAsync();
    }

    [Fact]
    public async Task PermissionProtectedMethod_UserHasPartialPermissions_ReturnsForbidden()
    {
        // Arrange — method requires "data.read" AND "data.write", user only has "data.read"
        _permissionService.GetUserPermissionsAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { "data.read" }); // missing "data.write"

        // Act
        var result = await _proxy.GetProtectedDataAsync();

        // Assert — should be forbidden because AND semantics requires ALL permissions
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        await _implementation.DidNotReceive().GetProtectedDataAsync();
    }

    // --- Test interface ---

    public interface ITestService
    {
        [RequiresPermission("data.read", "data.write")]
        Task<OperationResult<string>> GetProtectedDataAsync();

        [RequiresRole("Admin", "SuperAdmin")]
        Task<OperationResult<string>> GetAdminDataAsync();

        Task<OperationResult<string>> GetPublicDataAsync();

        [RequiresPermission("data.read")]
        Task<string> GetNonResultProtectedAsync();

        [RequiresPermission("data.delete")]
        Task<OperationResult> DeleteProtectedAsync();

        [RequiresRole("Admin")]
        [RequiresPermission("admin.execute")]
        Task<OperationResult<string>> GetDualProtectedAsync();
    }
}
