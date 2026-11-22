using FluentAssertions;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Membership;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GroundUp.Tests.Unit.Auth.Services;

public sealed class MembershipServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IUserTenantRepository _userTenants = Substitute.For<IUserTenantRepository>();
    private readonly IUserRoleRepository _userRoles = Substitute.For<IUserRoleRepository>();
    private readonly ITokenRevocationStore _store = Substitute.For<ITokenRevocationStore>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly IAuthSessionService _sessions = Substitute.For<IAuthSessionService>();
    private readonly MembershipService _sut;

    private readonly Guid _target = Guid.NewGuid();

    public MembershipServiceTests()
    {
        _currentUser.UserId.Returns(UserId);
        _tenantContext.TenantId.Returns(Guid.Empty);

        _sut = new MembershipService(
            _currentUser, _tenantContext, _userTenants, new LastAdminGuard(_userRoles),
            _store, _cache, _sessions, NullLogger<MembershipService>.Instance);
    }

    // ── Arrange helpers ───────────────────────────────────────────

    private void GivenMemberships(params (Guid TenantId, bool IsActive)[] memberships)
        => _userTenants.GetAllMembershipsForUserAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships
                .Select(m => new UserTenantDto(Guid.NewGuid(), UserId, m.TenantId, "ext", m.IsActive))
                .ToList()));

    private void GivenAdmins(params Guid[] adminUserIds)
        => _userRoles.GetTenantAdminHoldersAsync(_target, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(adminUserIds
                .Select(id => new UserRoleDto(Guid.NewGuid(), id, Guid.NewGuid(), _target, "TenantAdmin"))
                .ToList()));

    private void GivenDeactivation(OperationResult result)
        => _userTenants.DeactivateMembershipAsync(UserId, _target, Arg.Any<CancellationToken>()).Returns(result);

    private void GivenLeavable()
    {
        GivenMemberships((_target, true));
        GivenAdmins(UserId, Guid.NewGuid()); // two admins: leaving is allowed
        GivenDeactivation(OperationResult.Ok());
    }

    private void GivenCachedPermissions(Guid tenantId)
        => _cache.Set(PermissionCacheKeys.For(UserId, tenantId), new HashSet<string> { "orders.read" });

    private async Task AssertNoAccessEndedAsync()
    {
        await _store.DidNotReceiveWithAnyArgs().RevokeTenantAccessAsync(default, default, default);
        _cache.TryGetValue(PermissionCacheKeys.For(UserId, _target), out _).Should().BeTrue();
    }

    // ── Failures ──────────────────────────────────────────────────

    [Fact]
    public async Task LeaveTenantAsync_NoUser_ReturnsUnauthorized()
    {
        _currentUser.UserId.Returns(Guid.Empty);

        var result = await _sut.LeaveTenantAsync(_target, null);

        result.StatusCode.Should().Be(401);
        await _userTenants.DidNotReceiveWithAnyArgs().DeactivateMembershipAsync(default, default, default);
    }

    [Fact]
    public async Task LeaveTenantAsync_NoMembership_ReturnsNotFound()
    {
        GivenMemberships((Guid.NewGuid(), true));

        var result = await _sut.LeaveTenantAsync(_target, null);

        result.StatusCode.Should().Be(404);
        result.Message.Should().Be("User is not a member of this tenant.");
    }

    [Fact]
    public async Task LeaveTenantAsync_InactiveMembership_ReturnsNotFound()
    {
        GivenMemberships((_target, false));

        var result = await _sut.LeaveTenantAsync(_target, null);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task LeaveTenantAsync_MembershipLookupFails_ReturnsNotFound()
    {
        _userTenants.GetAllMembershipsForUserAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Fail("db", 500));

        var result = await _sut.LeaveTenantAsync(_target, null);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task LeaveTenantAsync_SoleAdmin_ReturnsForbiddenAndEndsNoAccess()
    {
        GivenMemberships((_target, true));
        GivenAdmins(UserId);
        GivenCachedPermissions(_target);

        var result = await _sut.LeaveTenantAsync(_target, null);

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().NotBeNullOrEmpty();
        await _userTenants.DidNotReceiveWithAnyArgs().DeactivateMembershipAsync(default, default, default);
        await AssertNoAccessEndedAsync();
    }

    [Fact]
    public async Task LeaveTenantAsync_SaveFails_ReturnsFailureAndEndsNoAccess()
    {
        GivenMemberships((_target, true));
        GivenAdmins();
        GivenDeactivation(OperationResult.Fail("A conflict occurred while leaving the tenant.", 409));
        GivenCachedPermissions(_target);

        var result = await _sut.LeaveTenantAsync(_target, null);

        result.StatusCode.Should().Be(409);
        await AssertNoAccessEndedAsync();
    }

    // ── Success: access ends ──────────────────────────────────────

    [Fact]
    public async Task LeaveTenantAsync_RegularMemberWithOneOtherAdmin_Succeeds()
    {
        GivenMemberships((_target, true));
        GivenAdmins(Guid.NewGuid());
        GivenDeactivation(OperationResult.Ok());

        var result = await _sut.LeaveTenantAsync(_target, null);

        result.Success.Should().BeTrue();
        await _userTenants.Received(1).DeactivateMembershipAsync(UserId, _target, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveTenantAsync_Success_RevokesTokensForThatTenantOnly()
    {
        GivenLeavable();

        await _sut.LeaveTenantAsync(_target, null);

        await _store.Received(1).RevokeTenantAccessAsync(UserId, _target, Arg.Any<CancellationToken>());
        await _store.DidNotReceiveWithAnyArgs().RevokeAllAccessAsync(default, default);
    }

    [Fact]
    public async Task LeaveTenantAsync_Success_EvictsCachedPermissionsForThatTenantOnly()
    {
        var otherTenant = Guid.NewGuid();
        GivenLeavable();
        GivenCachedPermissions(_target);
        GivenCachedPermissions(otherTenant);

        await _sut.LeaveTenantAsync(_target, null);

        _cache.TryGetValue(PermissionCacheKeys.For(UserId, _target), out _).Should().BeFalse();
        _cache.TryGetValue(PermissionCacheKeys.For(UserId, otherTenant), out _).Should().BeTrue();
    }

    [Fact]
    public async Task LeaveTenantAsync_RevocationStoreThrows_LeaveStillSucceedsAndCacheIsEvicted()
    {
        GivenLeavable();
        GivenCachedPermissions(_target);
        _store.RevokeTenantAccessAsync(UserId, _target, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("store down"));

        var result = await _sut.LeaveTenantAsync(_target, null);

        result.Success.Should().BeTrue();
        _cache.TryGetValue(PermissionCacheKeys.For(UserId, _target), out _).Should().BeFalse();
    }

    // ── Success: session outcome ──────────────────────────────────

    [Fact]
    public async Task LeaveTenantAsync_CurrentTenant_EndsSessionWithTenantlessTokenAndKeepsAuthTime()
    {
        var authTime = DateTimeOffset.UtcNow.AddHours(-1);
        _tenantContext.TenantId.Returns(_target);
        GivenLeavable();
        _sessions.IssueTenantlessSessionAsync(UserId, authTime).Returns(OperationResult<string>.Ok("tenantless-token"));

        var result = await _sut.LeaveTenantAsync(_target, authTime);

        result.Data.Should().Be(new LeaveTenantResult(null, true, "tenantless-token"));
        await _sessions.DidNotReceiveWithAnyArgs().SetTenantAsync(default, default, default);
    }

    [Fact]
    public async Task LeaveTenantAsync_CurrentTenant_TokenCannotBeIssued_EndsSessionWithoutToken()
    {
        _tenantContext.TenantId.Returns(_target);
        GivenLeavable();
        _sessions.IssueTenantlessSessionAsync(UserId, Arg.Any<DateTimeOffset?>())
            .Returns(OperationResult<string>.NotFound("User not found"));

        var result = await _sut.LeaveTenantAsync(_target, null);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(new LeaveTenantResult(null, true, null));
    }

    [Fact]
    public async Task LeaveTenantAsync_DifferentTenant_SessionUnchangedAndCurrentTenantReported()
    {
        var current = Guid.NewGuid();
        _tenantContext.TenantId.Returns(current);
        GivenLeavable();

        var result = await _sut.LeaveTenantAsync(_target, null);

        result.Data.Should().Be(new LeaveTenantResult(current, false, null));
        await _sessions.DidNotReceiveWithAnyArgs().IssueTenantlessSessionAsync(default, default);
    }

    [Fact]
    public async Task LeaveTenantAsync_FromTenantlessSession_SessionUnchangedAndNoCurrentTenant()
    {
        GivenLeavable();

        var result = await _sut.LeaveTenantAsync(_target, null);

        result.Data.Should().Be(new LeaveTenantResult(null, false, null));
        await _sessions.DidNotReceiveWithAnyArgs().IssueTenantlessSessionAsync(default, default);
    }

    [Fact]
    public void PermissionCacheKeys_MatchesTheFormatPermissionServiceUses()
    {
        var tenant = Guid.NewGuid();

        PermissionCacheKeys.For(UserId, tenant).Should().Be($"permissions:{UserId}:{tenant}");
    }
}
