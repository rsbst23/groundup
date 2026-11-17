using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Unit tests for <see cref="LastAdminGuard.CanLeaveTenantAsync"/>: a tenant must always keep at
/// least one active TenantAdmin, so only the last TenantAdmin is blocked from leaving.
/// </summary>
public sealed class LastAdminGuardTests
{
    private readonly IUserRoleRepository _userRoleRepository = Substitute.For<IUserRoleRepository>();
    private readonly LastAdminGuard _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LeavingUserId = Guid.NewGuid();

    public LastAdminGuardTests()
    {
        _sut = new LastAdminGuard(_userRoleRepository);
    }

    [Fact]
    public async Task CanLeaveTenantAsync_RegularMemberAndOneOtherAdmin_Allows()
    {
        // Arrange — the usual case: one admin plus regular members
        SetupAdmins(Guid.NewGuid());

        // Act
        var result = await _sut.CanLeaveTenantAsync(LeavingUserId, TenantId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeTrue();
    }

    [Fact]
    public async Task CanLeaveTenantAsync_RegularMemberAndNoAdmins_Allows()
    {
        // Arrange — a member leaving does not change the admin count
        SetupAdmins();

        // Act
        var result = await _sut.CanLeaveTenantAsync(LeavingUserId, TenantId);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CanLeaveTenantAsync_UserIsTheOnlyAdmin_RejectsWithLastAdmin()
    {
        // Arrange
        SetupAdmins(LeavingUserId);

        // Act
        var result = await _sut.CanLeaveTenantAsync(LeavingUserId, TenantId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("LAST_ADMIN");
    }

    [Fact]
    public async Task CanLeaveTenantAsync_UserIsOneOfTwoAdmins_Allows()
    {
        // Arrange
        SetupAdmins(LeavingUserId, Guid.NewGuid());

        // Act
        var result = await _sut.CanLeaveTenantAsync(LeavingUserId, TenantId);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CanLeaveTenantAsync_OnlyAdminListedTwice_StillCountsAsOneAdmin()
    {
        // Arrange — duplicate role rows for the same user must not count as a second admin
        SetupAdmins(LeavingUserId, LeavingUserId);

        // Act
        var result = await _sut.CanLeaveTenantAsync(LeavingUserId, TenantId);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("LAST_ADMIN");
    }

    [Fact]
    public async Task CanLeaveTenantAsync_AdminLookupFails_RejectsForSafety()
    {
        // Arrange
        _userRoleRepository.GetTenantAdminHoldersAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Fail("Database unavailable", 500));

        // Act
        var result = await _sut.CanLeaveTenantAsync(LeavingUserId, TenantId);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("LAST_ADMIN");
    }

    private void SetupAdmins(params Guid[] adminUserIds)
    {
        var holders = adminUserIds
            .Select(id => new UserRoleDto(Guid.NewGuid(), id, Guid.NewGuid(), TenantId, AuthRoleNames.TenantAdmin))
            .ToList();

        _userRoleRepository.GetTenantAdminHoldersAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(holders));
    }
}
