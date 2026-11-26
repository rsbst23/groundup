using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// GU-68 Requirement 3.4: removing TenantAdmin or SuperAdmin from the last active holder is refused;
/// removing it from anyone else is allowed.
/// </summary>
public sealed class LastAdminGuardRemovalTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SuperAdminRoleId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly IUserRoleRepository _userRoles = Substitute.For<IUserRoleRepository>();
    private readonly LastAdminGuard _sut;

    public LastAdminGuardRemovalTests() => _sut = new LastAdminGuard(_userRoles);

    private void GivenTenantAdmins(params Guid[] userIds)
        => _userRoles.GetTenantAdminHoldersAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(userIds
                .Select(id => new UserRoleDto(Guid.NewGuid(), id, Guid.NewGuid(), TenantId, AuthRoleNames.TenantAdmin))
                .ToList()));

    private void GivenSuperAdmins(params Guid[] userIds)
        => _userRoles.GetActiveHolderIdsAsync(SuperAdminRoleId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<Guid>>.Ok(userIds.ToList()));

    [Fact]
    public async Task CanRemoveTenantAdminAsync_LastHolder_RejectsWithLastAdmin()
    {
        GivenTenantAdmins(UserId);

        var result = await _sut.CanRemoveTenantAdminAsync(UserId, TenantId);

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(AuthErrorCodes.LastAdmin);
    }

    [Fact]
    public async Task CanRemoveTenantAdminAsync_UserIsNotAHolder_Allows()
    {
        // The only admin is someone else: removing a role the user does not actively hold changes nothing
        GivenTenantAdmins(Guid.NewGuid());

        var result = await _sut.CanRemoveTenantAdminAsync(UserId, TenantId);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CanRemoveTenantAdminAsync_HolderListedTwice_CountsOnce()
    {
        // The same admin holds the role from two sources
        GivenTenantAdmins(UserId, UserId);

        var result = await _sut.CanRemoveTenantAdminAsync(UserId, TenantId);

        result.ErrorCode.Should().Be(AuthErrorCodes.LastAdmin);
    }

    [Fact]
    public async Task CanRemoveSuperAdminAsync_LastHolder_RejectsWithLastSuperAdmin()
    {
        GivenSuperAdmins(UserId);

        var result = await _sut.CanRemoveSuperAdminAsync(UserId, SuperAdminRoleId);

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(AuthErrorCodes.LastSuperAdmin);
    }

    [Fact]
    public async Task CanRemoveSuperAdminAsync_AnotherSuperAdminRemains_Allows()
    {
        GivenSuperAdmins(UserId, Guid.NewGuid());

        var result = await _sut.CanRemoveSuperAdminAsync(UserId, SuperAdminRoleId);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CanRemoveSuperAdminAsync_UserIsNotAHolder_Allows()
    {
        GivenSuperAdmins(Guid.NewGuid());

        var result = await _sut.CanRemoveSuperAdminAsync(UserId, SuperAdminRoleId);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CanRemoveSuperAdminAsync_LookupFails_RejectsForSafety()
    {
        _userRoles.GetActiveHolderIdsAsync(SuperAdminRoleId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<Guid>>.Fail("db", 500));

        var result = await _sut.CanRemoveSuperAdminAsync(UserId, SuperAdminRoleId);

        result.ErrorCode.Should().Be(AuthErrorCodes.LastSuperAdmin);
    }
}
