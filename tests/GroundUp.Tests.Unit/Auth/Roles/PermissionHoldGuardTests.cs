using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Roles;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Roles;

/// <summary>
/// GU-96 Requirement 4: a caller can only grant permissions they hold; admins hold everything.
/// </summary>
public sealed class PermissionHoldGuardTests
{
    private static readonly Guid CallerId = Guid.NewGuid();

    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly PermissionHoldGuard _sut;

    public PermissionHoldGuardTests()
    {
        _sut = new PermissionHoldGuard(_permissions);
    }

    [Fact]
    public async Task CheckHoldsAsync_NoKeys_PassesWithoutReading()
    {
        (await _sut.CheckHoldsAsync(CallerId, [])).Success.Should().BeTrue();

        await _permissions.DidNotReceiveWithAnyArgs().GetPermissionSummaryAsync(default, default);
    }

    [Fact]
    public async Task CheckHoldsAsync_AllHeld_Passes()
    {
        GivenHolds("docs.read", "docs.write");

        (await _sut.CheckHoldsAsync(CallerId, ["docs.write", "docs.read", "docs.read"])).Success.Should().BeTrue();
    }

    [Fact]
    public async Task CheckHoldsAsync_SomeMissing_PermissionNotHeldWithTheMissingKeysSorted()
    {
        GivenHolds("docs.read");

        var result = await _sut.CheckHoldsAsync(CallerId, ["docs.write", "docs.read", "admin.all", "docs.write"]);

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be(AuthErrorCodes.PermissionNotHeld);
        result.Errors.Should().Equal("admin.all", "docs.write");
    }

    [Fact]
    public async Task CheckHoldsAsync_AllPermissions_Passes()
    {
        _permissions.GetPermissionSummaryAsync(CallerId, Arg.Any<CancellationToken>())
            .Returns(new PermissionSummaryDto(true, []));

        (await _sut.CheckHoldsAsync(CallerId, ["anything.at.all"])).Success.Should().BeTrue();
    }

    [Fact]
    public async Task CheckHoldsAsync_KeysAreCaseSensitive()
    {
        GivenHolds("docs.read");

        (await _sut.CheckHoldsAsync(CallerId, ["Docs.Read"])).Success.Should().BeFalse();
    }

    private void GivenHolds(params string[] keys)
        => _permissions.GetPermissionSummaryAsync(CallerId, Arg.Any<CancellationToken>())
            .Returns(new PermissionSummaryDto(false, keys.ToList()));
}
