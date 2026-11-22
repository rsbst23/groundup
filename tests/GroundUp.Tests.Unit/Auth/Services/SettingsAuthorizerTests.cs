using GroundUp.Auth.Core;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Settings;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Enums;
using GroundUp.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GroundUp.Tests.Unit.Auth.Services;

public sealed class SettingsAuthorizerTests
{
    private const string AllowlistedKey = "auth.keycloak.public-base-url";
    private const string SessionKey = "auth.session.token-lifetime-minutes";

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IBootstrapStateService _bootstrap = Substitute.For<IBootstrapStateService>();
    private readonly ISettingScopeOwnerResolver _resolver = Substitute.For<ISettingScopeOwnerResolver>();

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _levelId = Guid.NewGuid();

    public SettingsAuthorizerTests()
    {
        _currentUser.UserId.Returns(_userId);
        _tenantContext.TenantId.Returns(_tenantId);
        _bootstrap.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    private SettingsAuthorizer Create(bool withBootstrap = true, bool withResolver = false)
        => new(_currentUser, _tenantContext, _permissions, NullLogger<SettingsAuthorizer>.Instance,
            withBootstrap ? _bootstrap : null,
            withResolver ? _resolver : null);

    private void MakeSuperAdmin()
        => _permissions.HasSystemRoleAsync(_userId, AuthRoleNames.SuperAdmin, Arg.Any<CancellationToken>()).Returns(true);

    private void GrantManageTenant()
        => _permissions.HasPermissionAsync(_userId, PermissionKeysAuth.SettingsManageTenant, Arg.Any<CancellationToken>()).Returns(true);

    private void SignedOut() => _currentUser.UserId.Returns(Guid.Empty);

    // ── Setup exception ───────────────────────────────────────────

    [Fact]
    public async Task Write_SetupIncompleteAllowlistedKeySystemLevelNoUser_Allowed()
    {
        SignedOut();
        _bootstrap.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await Create().AuthorizeWriteAsync(AllowlistedKey, _levelId, "System", null);

        Assert.Equal(SettingsAuthorizationResult.Allowed, result);
    }

    [Fact]
    public async Task Write_SetupCompleteAllowlistedKeyNoUser_Unauthenticated()
    {
        SignedOut();

        var result = await Create().AuthorizeWriteAsync(AllowlistedKey, _levelId, "System", null);

        Assert.Equal(SettingsAuthorizationResult.Unauthenticated, result);
    }

    [Fact]
    public async Task Write_SetupIncompleteKeyNotAllowlisted_Unauthenticated()
    {
        SignedOut();
        _bootstrap.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, "System", null);

        Assert.Equal(SettingsAuthorizationResult.Unauthenticated, result);
    }

    [Fact]
    public async Task Write_SetupIncompleteAllowlistedKeyAtTenantLevel_NotAllowed()
    {
        SignedOut();
        _bootstrap.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await Create().AuthorizeWriteAsync(AllowlistedKey, _levelId, "Tenant", _tenantId);

        Assert.Equal(SettingsAuthorizationResult.Unauthenticated, result);
    }

    [Fact]
    public async Task Write_SetupIncompleteAllowlistedKeyWithScope_NotAllowed()
    {
        SignedOut();
        _bootstrap.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await Create().AuthorizeWriteAsync(AllowlistedKey, _levelId, "System", Guid.NewGuid());

        Assert.Equal(SettingsAuthorizationResult.Unauthenticated, result);
    }

    [Fact]
    public async Task Delete_SetupIncompleteAllowlistedKeyNoUser_Unauthenticated()
    {
        SignedOut();
        _bootstrap.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await Create().AuthorizeDeleteAsync(AllowlistedKey, _levelId, "System", null);

        Assert.Equal(SettingsAuthorizationResult.Unauthenticated, result);
    }

    [Fact]
    public async Task Write_BootstrapStateThrows_FailsClosed()
    {
        SignedOut();
        _bootstrap.IsCompleteAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await Create().AuthorizeWriteAsync(AllowlistedKey, _levelId, "System", null);

        Assert.Equal(SettingsAuthorizationResult.Unauthenticated, result);
    }

    [Fact]
    public async Task Write_NoBootstrapServiceRegistered_SetupExceptionNeverApplies()
    {
        SignedOut();

        var result = await Create(withBootstrap: false).AuthorizeWriteAsync(AllowlistedKey, _levelId, "System", null);

        Assert.Equal(SettingsAuthorizationResult.Unauthenticated, result);
    }

    [Fact]
    public async Task Write_NonAllowlistedKey_DoesNotReadBootstrapState()
    {
        SignedOut();

        await Create().AuthorizeWriteAsync(SessionKey, _levelId, "System", null);

        await _bootstrap.DidNotReceive().IsCompleteAsync(Arg.Any<CancellationToken>());
    }

    // ── Authentication and SuperAdmin ─────────────────────────────

    [Fact]
    public async Task Write_NoUser_Unauthenticated()
    {
        SignedOut();

        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, "Tenant", _tenantId);

        Assert.Equal(SettingsAuthorizationResult.Unauthenticated, result);
    }

    [Theory]
    [InlineData("System", false)]
    [InlineData("Tenant", true)]
    [InlineData("Group", true)]
    public async Task Write_SuperAdmin_AllowedAtAnyLevelAndTenant(string levelName, bool withScope)
    {
        MakeSuperAdmin();
        Guid? scope = withScope ? Guid.NewGuid() : null; // a tenant that is not the current one

        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, levelName, scope);

        Assert.Equal(SettingsAuthorizationResult.Allowed, result);
    }

    [Fact]
    public async Task Delete_SuperAdmin_Allowed()
    {
        MakeSuperAdmin();

        var result = await Create().AuthorizeDeleteAsync(SessionKey, _levelId, "System", null);

        Assert.Equal(SettingsAuthorizationResult.Allowed, result);
    }

    // ── System level ──────────────────────────────────────────────

    [Fact]
    public async Task Write_SystemLevelWithManageTenantPermission_Forbidden()
    {
        GrantManageTenant();

        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, "System", null);

        Assert.Equal(SettingsAuthorizationResult.Forbidden, result);
    }

    [Fact]
    public async Task Write_UnknownLevelWithoutResolver_Forbidden()
    {
        GrantManageTenant();

        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, "Region", Guid.NewGuid());

        Assert.Equal(SettingsAuthorizationResult.Forbidden, result);
    }

    // ── Tenant level ──────────────────────────────────────────────

    [Fact]
    public async Task Write_OwnTenantWithPermission_Allowed()
    {
        GrantManageTenant();

        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, "Tenant", _tenantId);

        Assert.Equal(SettingsAuthorizationResult.Allowed, result);
    }

    [Fact]
    public async Task Write_LevelNameIsCaseInsensitive()
    {
        GrantManageTenant();

        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, "tenant", _tenantId);

        Assert.Equal(SettingsAuthorizationResult.Allowed, result);
    }

    [Fact]
    public async Task Write_OwnTenantWithoutPermission_Forbidden()
    {
        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, "Tenant", _tenantId);

        Assert.Equal(SettingsAuthorizationResult.Forbidden, result);
    }

    [Fact]
    public async Task Write_OtherTenantWithPermission_Forbidden()
    {
        GrantManageTenant();

        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, "Tenant", Guid.NewGuid());

        Assert.Equal(SettingsAuthorizationResult.Forbidden, result);
    }

    [Fact]
    public async Task Write_TenantLevelWithoutScope_Forbidden()
    {
        GrantManageTenant();

        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, "Tenant", null);

        Assert.Equal(SettingsAuthorizationResult.Forbidden, result);
    }

    [Fact]
    public async Task Write_NoCurrentTenantAndEmptyScope_Forbidden()
    {
        _tenantContext.TenantId.Returns(Guid.Empty);
        GrantManageTenant();

        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, "Tenant", Guid.Empty);

        Assert.Equal(SettingsAuthorizationResult.Forbidden, result);
    }

    [Fact]
    public async Task Delete_OwnTenantWithPermission_Allowed()
    {
        GrantManageTenant();

        var result = await Create().AuthorizeDeleteAsync(SessionKey, _levelId, "Tenant", _tenantId);

        Assert.Equal(SettingsAuthorizationResult.Allowed, result);
    }

    [Fact]
    public async Task Delete_OtherTenant_Forbidden()
    {
        GrantManageTenant();

        var result = await Create().AuthorizeDeleteAsync(SessionKey, _levelId, "Tenant", Guid.NewGuid());

        Assert.Equal(SettingsAuthorizationResult.Forbidden, result);
    }

    // ── Levels below Tenant ───────────────────────────────────────

    [Fact]
    public async Task Write_BelowTenantResolverReturnsCurrentTenant_Allowed()
    {
        GrantManageTenant();
        var groupId = Guid.NewGuid();
        _resolver.GetOwningTenantIdAsync(_levelId, "Group", groupId, Arg.Any<CancellationToken>()).Returns(_tenantId);

        var result = await Create(withResolver: true).AuthorizeWriteAsync(SessionKey, _levelId, "Group", groupId);

        Assert.Equal(SettingsAuthorizationResult.Allowed, result);
    }

    [Fact]
    public async Task Write_BelowTenantResolverReturnsOtherTenant_Forbidden()
    {
        GrantManageTenant();
        var groupId = Guid.NewGuid();
        _resolver.GetOwningTenantIdAsync(_levelId, "Group", groupId, Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        var result = await Create(withResolver: true).AuthorizeWriteAsync(SessionKey, _levelId, "Group", groupId);

        Assert.Equal(SettingsAuthorizationResult.Forbidden, result);
    }

    [Fact]
    public async Task Write_BelowTenantResolverReturnsNull_Forbidden()
    {
        GrantManageTenant();
        var groupId = Guid.NewGuid();
        _resolver.GetOwningTenantIdAsync(_levelId, "Group", groupId, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await Create(withResolver: true).AuthorizeWriteAsync(SessionKey, _levelId, "Group", groupId);

        Assert.Equal(SettingsAuthorizationResult.Forbidden, result);
    }

    [Fact]
    public async Task Write_BelowTenantNoResolver_Forbidden()
    {
        GrantManageTenant();

        var result = await Create().AuthorizeWriteAsync(SessionKey, _levelId, "Group", Guid.NewGuid());

        Assert.Equal(SettingsAuthorizationResult.Forbidden, result);
    }

    [Fact]
    public async Task Write_BelowTenantResolverOwnerButNoPermission_Forbidden()
    {
        var groupId = Guid.NewGuid();
        _resolver.GetOwningTenantIdAsync(_levelId, "Group", groupId, Arg.Any<CancellationToken>()).Returns(_tenantId);

        var result = await Create(withResolver: true).AuthorizeWriteAsync(SessionKey, _levelId, "Group", groupId);

        Assert.Equal(SettingsAuthorizationResult.Forbidden, result);
    }

    // ── Registration ──────────────────────────────────────────────

    [Fact]
    public void AddGroundUpAuth_AfterSettings_ReplacesDefaultAuthorizer()
    {
        var services = new ServiceCollection();
        services.AddGroundUpSettings();

        services.AddGroundUpAuth(_ => { });

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ISettingsAuthorizer));
        Assert.Equal(typeof(SettingsAuthorizer), descriptor.ImplementationType);
    }

    [Fact]
    public void AddGroundUpAuth_BeforeSettings_KeepsEnforcingAuthorizer()
    {
        var services = new ServiceCollection();
        services.AddGroundUpAuth(_ => { });

        services.AddGroundUpSettings();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ISettingsAuthorizer));
        Assert.Equal(typeof(SettingsAuthorizer), descriptor.ImplementationType);
    }
}
