using FluentAssertions;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Events;
using GroundUp.Auth.Services.Identity;
using GroundUp.Auth.Services.Membership;
using GroundUp.Core.Results;
using GroundUp.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Identity;

/// <summary>
/// GU-68 Requirements 2.4-2.6: finding or creating the user at sign-in, and claiming pending users.
/// </summary>
public sealed class SignInUserResolverTests
{
    private const string Sub = "kc-sub-1";
    private const string Email = "ana@example.com";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly SignInUserResolver _sut;

    public SignInUserResolverTests()
    {
        _users.GetByExternalUserIdAsync(Sub, Arg.Any<CancellationToken>()).Returns(OperationResult<UserDto>.NotFound());
        _users.GetUnlinkedByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserDto>>.Ok([]));
        _users.AddAsync(Arg.Any<UserDto>(), Arg.Any<CancellationToken>())
            .Returns(ci => OperationResult<UserDto>.Ok(ci.ArgAt<UserDto>(0) with { Id = Guid.NewGuid() }));

        _sut = new SignInUserResolver(
            _users,
            new TenantAccessRevoker(Substitute.For<ITokenRevocationStore>(), new MemoryCache(new MemoryCacheOptions()),
                NullLogger<TenantAccessRevoker>.Instance, _eventBus),
            NullLogger<SignInUserResolver>.Instance);
    }

    [Fact]
    public async Task ResolveAsync_KnownIdentity_ReturnsThatUser()
    {
        var known = GivenKnownUser();

        var user = await _sut.ResolveAsync(Info(verified: true));

        user.Should().Be(known);
        await _users.DidNotReceiveWithAnyArgs().GetUnlinkedByEmailAsync(default!, default);
        await _users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task ResolveAsync_KnownIdentityAcceptingAnotherUsersInvitation_MergesThePendingUser()
    {
        var known = GivenKnownUser();
        var pendingId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _users.MergePendingUserAsync(pendingId, known.Id, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<Guid>>.Ok([tenantId]));

        var user = await _sut.ResolveAsync(Info(), invitedPendingUserId: pendingId);

        user.Should().Be(known);
        await _users.Received(1).MergePendingUserAsync(pendingId, known.Id, Arg.Any<CancellationToken>());
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<PendingUserMerged>(e => e.FromUserId == pendingId && e.ToUserId == known.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_KnownIdentityIsTheInvitedUser_NoMerge()
    {
        var known = GivenKnownUser();

        await _sut.ResolveAsync(Info(), invitedPendingUserId: known.Id);

        await _users.DidNotReceiveWithAnyArgs().MergePendingUserAsync(default, default, default);
    }

    [Fact]
    public async Task ResolveAsync_InvitationClaim_LinksThePendingUserEvenWithoutVerifiedEmail()
    {
        var pendingId = Guid.NewGuid();
        var linked = new UserDto(pendingId, Sub, Email, "Ana", true);
        _users.LinkIdentityAsync(pendingId, Sub, "Ana", Arg.Any<CancellationToken>()).Returns(OperationResult<UserDto>.Ok(linked));

        var user = await _sut.ResolveAsync(Info(verified: false), invitedPendingUserId: pendingId);

        user.Should().Be(linked);
        await _users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task ResolveAsync_InvitationClaimLostToAConcurrentSignInOfTheSameIdentity_ReturnsThatUser()
    {
        var pendingId = Guid.NewGuid();
        var linkedByTheOtherRequest = new UserDto(pendingId, Sub, Email, "Ana", true);
        _users.LinkIdentityAsync(pendingId, Sub, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Fail("already linked", 409));
        _users.GetByExternalUserIdAsync(Sub, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.NotFound(), OperationResult<UserDto>.Ok(linkedByTheOtherRequest));

        var user = await _sut.ResolveAsync(Info(), invitedPendingUserId: pendingId);

        user.Should().Be(linkedByTheOtherRequest);
    }

    [Fact]
    public async Task ResolveAsync_VerifiedEmailMatchingOnePendingUser_LinksIt()
    {
        var pending = new UserDto(Guid.NewGuid(), null, Email, Email, true);
        _users.GetUnlinkedByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserDto>>.Ok([pending]));
        _users.LinkIdentityAsync(pending.Id, Sub, "Ana", Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(pending with { ExternalUserId = Sub, DisplayName = "Ana" }));

        var user = await _sut.ResolveAsync(Info(verified: true));

        user.Id.Should().Be(pending.Id);
        await _users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task ResolveAsync_UnverifiedEmail_DoesNotClaimAPendingUser()
    {
        var pending = new UserDto(Guid.NewGuid(), null, Email, Email, true);
        _users.GetUnlinkedByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserDto>>.Ok([pending]));

        var user = await _sut.ResolveAsync(Info(verified: false));

        user.Id.Should().NotBe(pending.Id);
        await _users.DidNotReceiveWithAnyArgs().LinkIdentityAsync(default, default!, default, default);
        await _users.Received(1).AddAsync(Arg.Is<UserDto>(u => u.ExternalUserId == Sub && u.Email == Email), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_VerifiedEmailMatchingSeveralPendingUsers_ClaimsNone()
    {
        _users.GetUnlinkedByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserDto>>.Ok(
        [
            new UserDto(Guid.NewGuid(), null, Email, Email, true),
            new UserDto(Guid.NewGuid(), null, Email, Email, true)
        ]));

        await _sut.ResolveAsync(Info(verified: true));

        await _users.DidNotReceiveWithAnyArgs().LinkIdentityAsync(default, default!, default, default);
        await _users.Received(1).AddAsync(Arg.Any<UserDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_ConcurrentFirstSignIn_ReReadsTheUserCreatedByTheOtherRequest()
    {
        var createdElsewhere = new UserDto(Guid.NewGuid(), Sub, Email, "Ana", true);
        _users.AddAsync(Arg.Any<UserDto>(), Arg.Any<CancellationToken>()).Returns(OperationResult<UserDto>.Fail("exists", 409));
        _users.GetByExternalUserIdAsync(Sub, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.NotFound(), OperationResult<UserDto>.Ok(createdElsewhere));

        var user = await _sut.ResolveAsync(Info());

        user.Should().Be(createdElsewhere);
    }

    private UserDto GivenKnownUser()
    {
        var known = new UserDto(Guid.NewGuid(), Sub, Email, "Ana", true);
        _users.GetByExternalUserIdAsync(Sub, Arg.Any<CancellationToken>()).Returns(OperationResult<UserDto>.Ok(known));
        return known;
    }

    private static ExternalUserInfo Info(bool verified = false)
        => new(Sub, Email, "Ana", new Dictionary<string, string> { ["email_verified"] = verified ? "true" : "false" });
}
