using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Authorization;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Attributes;
using GroundUp.Core;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// The proxy asks IPermissionService.HasPermissionAsync (which includes the admin bypass),
    /// so the substitute answers true exactly for the permissions the user holds.
    /// </summary>
    private void GivenPermissions(params string[] held)
    {
        var set = new HashSet<string>(held, StringComparer.Ordinal);
        _permissionService.HasPermissionAsync(_userId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => set.Contains(call.ArgAt<string>(1)));
    }

    [Fact]
    public async Task PermissionProtectedMethod_AdminBypassGrantsEveryPermission_InvokesMethod()
    {
        // Arrange - HasPermissionAsync is true for SuperAdmin and for TenantAdmin of the current tenant,
        // even though neither holds the permission explicitly. The proxy must honour that.
        _permissionService.HasPermissionAsync(_userId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _implementation.GetProtectedDataAsync()
            .Returns(OperationResult<string>.Ok("success"));

        // Act
        var result = await _proxy.GetProtectedDataAsync();

        // Assert
        Assert.True(result.Success);
        await _implementation.Received(1).GetProtectedDataAsync();
        await _permissionService.DidNotReceive().GetUserPermissionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonGenericOperationResult_AdminBypass_InvokesMethod()
    {
        _permissionService.HasPermissionAsync(_userId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _implementation.DeleteProtectedAsync().Returns(OperationResult.Ok());

        var result = await _proxy.DeleteProtectedAsync();

        Assert.True(result.Success);
    }

    [Fact]
    public async Task PermissionProtectedMethod_UserLacksPermission_ReturnsForbidden()
    {
        // Arrange
        GivenPermissions();

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
        GivenPermissions("data.read", "data.write");
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
        GivenPermissions();

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
        GivenPermissions("data.delete");
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
        GivenPermissions(); // no permissions

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
        GivenPermissions("admin.execute");
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
        GivenPermissions("data.read"); // missing "data.write"

        // Act
        var result = await _proxy.GetProtectedDataAsync();

        // Assert — should be forbidden because AND semantics requires ALL permissions
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        await _implementation.DidNotReceive().GetProtectedDataAsync();
    }

    // --- Refusal codes (GU-98) ---

    [Fact]
    public async Task PermissionProtectedMethod_MissingSome_PermissionRequiredWithExactlyTheMissingKeys()
    {
        GivenPermissions("data.read");

        var result = await _proxy.GetProtectedDataAsync();

        Assert.Equal(403, result.StatusCode);
        Assert.Equal(ErrorCodes.PermissionRequired, result.ErrorCode);
        Assert.Equal(new[] { "data.write" }, result.Errors);
        Assert.Equal("You need the permission data.write.", result.Message);
    }

    [Fact]
    public async Task PermissionProtectedMethod_MissingAll_ChecksEveryKeyAndNamesThem()
    {
        GivenPermissions();

        var result = await _proxy.GetProtectedDataAsync();

        Assert.Equal(ErrorCodes.PermissionRequired, result.ErrorCode);
        Assert.Equal(new[] { "data.read", "data.write" }, result.Errors);
        Assert.Equal("You need the permissions data.read, data.write.", result.Message);
    }

    [Fact]
    public async Task NonGenericOperationResult_MissingPermission_PermissionRequired()
    {
        GivenPermissions();

        var result = await _proxy.DeleteProtectedAsync();

        Assert.Equal(ErrorCodes.PermissionRequired, result.ErrorCode);
        Assert.Equal(new[] { "data.delete" }, result.Errors);
    }

    [Fact]
    public async Task RoleProtectedMethod_NoneOfTheRoles_RoleRequiredWithAllRoles()
    {
        _permissionService.HasAnySystemRoleAsync(_userId, Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _proxy.GetAdminDataAsync();

        Assert.Equal(403, result.StatusCode);
        Assert.Equal(ErrorCodes.RoleRequired, result.ErrorCode);
        Assert.Equal(new[] { "Admin", "SuperAdmin" }, result.Errors);
        Assert.Equal("You need one of the roles Admin, SuperAdmin.", result.Message);
    }

    [Fact]
    public async Task RoleProtectedMethod_OneRole_MessageNamesIt()
    {
        _permissionService.HasAnySystemRoleAsync(_userId, Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _proxy.GetDualProtectedAsync();

        Assert.Equal(ErrorCodes.RoleRequired, result.ErrorCode);
        Assert.Equal("You need the role Admin.", result.Message);
    }

    [Fact]
    public async Task Unauthenticated_StaysForbidden()
    {
        _currentUser.UserId.Returns(Guid.Empty);

        var result = await _proxy.GetProtectedDataAsync();

        Assert.Equal(ErrorCodes.Forbidden, result.ErrorCode);
        Assert.Null(result.Errors);
        await _permissionService.DidNotReceive().HasPermissionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Return type rule (GU-98) ---

    [Fact]
    public void Create_AttributedMethodNotReturningOperationResult_ThrowsNamingTheMethod()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AuthorizationInterceptor<IBadService>.Create(
            Substitute.For<IBadService>(), _permissionService, _currentUser));

        Assert.Contains("IBadService.GetNameAsync", ex.Message);
    }

    [Fact]
    public void AddAuthorized_AttributedMethodNotReturningOperationResult_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddAuthorized<IBadService, BadService>());

        Assert.Contains("IBadService.GetNameAsync", ex.Message);
        Assert.Empty(services);
    }

    [Fact]
    public void AddAuthorized_UnattributedMethodsMayReturnAnything()
    {
        var services = new ServiceCollection();

        services.AddAuthorized<IPlainService, PlainService>();

        Assert.Equal(2, services.Count);
    }

    public interface IBadService
    {
        [RequiresPermission("data.read")]
        Task<OperationResult<string>> GetAsync();

        [RequiresPermission("data.read")]
        Task<string> GetNameAsync();
    }

    public sealed class BadService : IBadService
    {
        public Task<OperationResult<string>> GetAsync() => Task.FromResult(OperationResult<string>.Ok("x"));

        public Task<string> GetNameAsync() => Task.FromResult("x");
    }

    public interface IPlainService
    {
        [RequiresPermission("data.read")]
        Task<OperationResult> DoAsync();

        string Describe();
    }

    public sealed class PlainService : IPlainService
    {
        public Task<OperationResult> DoAsync() => Task.FromResult(OperationResult.Ok());

        public string Describe() => "plain";
    }

    // --- Test interface ---

    public interface ITestService
    {
        [RequiresPermission("data.read", "data.write")]
        Task<OperationResult<string>> GetProtectedDataAsync();

        [RequiresRole("Admin", "SuperAdmin")]
        Task<OperationResult<string>> GetAdminDataAsync();

        Task<OperationResult<string>> GetPublicDataAsync();

        [RequiresPermission("data.delete")]
        Task<OperationResult> DeleteProtectedAsync();

        [RequiresRole("Admin")]
        [RequiresPermission("admin.execute")]
        Task<OperationResult<string>> GetDualProtectedAsync();
    }
}
