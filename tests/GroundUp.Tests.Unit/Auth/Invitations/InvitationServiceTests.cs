using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Core.Validators;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Invitations;
using GroundUp.Auth.Services.Membership;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Invitations;

/// <summary>
/// GU-68 Requirement 6: the refusals that happen before anything is written, and the expiry rule.
/// The writes themselves are covered by the integration tests against real Postgres.
/// </summary>
public sealed class InvitationServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly RoleDto Editor = new(Guid.NewGuid(), "Editor", null, RoleType.Application, TenantId, false);
    private static readonly RoleDto TenantAdmin = new(Guid.NewGuid(), AuthRoleNames.TenantAdmin, null, RoleType.Application, TenantId, true);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserTenantRepository _userTenants = Substitute.For<IUserTenantRepository>();
    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly IRoleGrantWriter _writer = Substitute.For<IRoleGrantWriter>();
    private readonly IUserRoleRepository _userRoles = Substitute.For<IUserRoleRepository>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IInvitationEmailSender _emailSender = Substitute.For<IInvitationEmailSender>();
    private readonly InvitationService _sut;

    public InvitationServiceTests()
    {
        _currentUser.UserId.Returns(CallerId);
        _tenantContext.TenantId.Returns(TenantId);
        GivenTenant(realmName: null);
        _roles.FindByIdAnyTenantAsync(Editor.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(Editor));
        _roles.FindByIdAnyTenantAsync(TenantAdmin.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(TenantAdmin));

        // The caller holds auth.roles.assign (not an admin)
        _userRoles.GetSystemRolesForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserRoleDto>>.Ok([]));
        _userRoles.GetByUserIdForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserRoleDto>>.Ok([]));
        _permissions.HasPermissionAsync(CallerId, PermissionKeysAuth.RolesAssign, Arg.Any<CancellationToken>()).Returns(true);

        GivenSetting(InvitationSettingKeys.AcceptUrl, "https://app.test/invite/{token}");
        _settings.GetAsync<int>(InvitationSettingKeys.LifetimeHours, Arg.Any<CancellationToken>()).Returns(OperationResult<int>.Ok(168));

        // A transaction that runs the work, as the real unit of work does
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                await ci.ArgAt<Func<CancellationToken, Task>>(0)(CancellationToken.None);
                return OperationResult.Ok();
            });
        _users.GetAllByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserDto>>.Ok([]));
        _users.AddAsync(Arg.Any<UserDto>(), Arg.Any<CancellationToken>())
            .Returns(ci => OperationResult<UserDto>.Ok(ci.ArgAt<UserDto>(0) with { Id = Guid.NewGuid() }));
        _userTenants.GetAllMembershipsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserTenantDto>>.Ok([]));
        _userTenants.AddForTenantAsync(Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>())
            .Returns(ci => OperationResult<UserTenantDto>.Ok(ci.ArgAt<UserTenantDto>(0)));
        _writer.GrantAsync(default, default, default, default, default, default)
            .ReturnsForAnyArgs(ci => OperationResult<RoleGrantResult>.Ok(new RoleGrantResult(
                new UserRoleDto(Guid.NewGuid(), ci.ArgAt<Guid>(0), ci.ArgAt<Guid>(1), ci.ArgAt<Guid>(2)), true)));
        _invitations.AddAsync(Arg.Any<InvitationDto>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => OperationResult<InvitationDto>.Ok(ci.ArgAt<InvitationDto>(0) with { Id = Guid.NewGuid() }, statusCode: 201));

        _sut = new InvitationService(
            _currentUser, _tenantContext, _tenants, _roles, _users, _userTenants, _invitations, _writer,
            new RoleGrantPolicy(_userRoles, GroundUp.Tests.Unit.Auth.TestHelpers.RoleRepositoryStubs.WithoutGrants(_roles), _permissions, _tenantContext, []),
            new TenantAccessRevoker(Substitute.For<ITokenRevocationStore>(), new MemoryCache(new MemoryCacheOptions()), NullLogger<TenantAccessRevoker>.Instance),
            _unitOfWork, _settings, _emailSender, new CreateInvitationDtoValidator(), NullLogger<InvitationService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_EnterpriseTenant_RefusedBeforeAnyWrite()
    {
        GivenTenant(realmName: "acme-realm");

        var result = await _sut.CreateAsync(Request());

        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be(AuthErrorCodes.EnterpriseNotSupported);
        await AssertNothingWrittenAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://app.test/invite")]
    public async Task CreateAsync_AcceptUrlMissingOrWithoutTokenPlaceholder_Refused(string template)
    {
        GivenSetting(InvitationSettingKeys.AcceptUrl, template);

        var result = await _sut.CreateAsync(Request());

        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be(AuthErrorCodes.InvitationUrlNotConfigured);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task CreateAsync_CallerMayNotGrantTheRole_Forbidden()
    {
        // auth.roles.assign does not cover TenantAdmin
        var result = await _sut.CreateAsync(Request(TenantAdmin.Id));

        result.StatusCode.Should().Be(403);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task CreateAsync_RoleOfAnotherTenant_NotFound()
    {
        var foreign = Editor with { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        _roles.FindByIdAnyTenantAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(foreign));

        (await _sut.CreateAsync(Request(foreign.Id))).StatusCode.Should().Be(404);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task CreateAsync_NoTenantSelected_TenantRequired()
    {
        _tenantContext.TenantId.Returns(Guid.Empty);

        (await _sut.CreateAsync(Request())).ErrorCode.Should().Be(GroundUp.Core.ErrorCodes.TenantRequired);
    }

    [Fact]
    public async Task CreateAsync_Success_GrantsWithSourceInvitation_AndSendsTheLink()
    {
        var result = await _sut.CreateAsync(Request());

        result.StatusCode.Should().Be(201);
        await _writer.Received(1).GrantAsync(
            Arg.Any<Guid>(), Editor.Id, TenantId, RoleAssignmentSource.Invitation, true, Arg.Any<CancellationToken>());
        await _writer.Received(1).PublishGrantedAsync(Arg.Any<RoleGrantResult>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendAsync(
            Arg.Is<InvitationEmail>(e => e.To == "ana@example.com" && e.AcceptUrl.StartsWith("https://app.test/invite/")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_RequestedExpiryLaterThanTheLifetime_UsesTheLifetime()
    {
        _settings.GetAsync<int>(InvitationSettingKeys.LifetimeHours, Arg.Any<CancellationToken>()).Returns(OperationResult<int>.Ok(24));

        var result = await _sut.CreateAsync(Request(expiresAt: DateTime.UtcNow.AddDays(10)));

        result.Data!.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CreateAsync_RequestedExpiryEarlierThanTheLifetime_UsesTheRequest()
    {
        var requested = DateTime.UtcNow.AddHours(2);

        var result = await _sut.CreateAsync(Request(expiresAt: requested));

        result.Data!.ExpiresAt.Should().BeCloseTo(requested, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CreateAsync_EmailSenderThrows_InvitationStillCreated()
    {
        _emailSender.SendAsync(Arg.Any<InvitationEmail>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("smtp down"));

        (await _sut.CreateAsync(Request())).StatusCode.Should().Be(201);
    }

    private static CreateInvitationDto Request(Guid? roleId = null, DateTime? expiresAt = null)
        => new("ana@example.com", roleId ?? Editor.Id, expiresAt);

    private void GivenTenant(string? realmName)
        => _tenants.GetByIdsBypassFilterAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<TenantDto>>.Ok(
                [new TenantDto(TenantId, "Acme", "acme", TenantType.Standard, OnboardingMode.InviteOnly, null, realmName, true)]));

    private void GivenSetting(string key, string value)
        => _settings.GetAsync<string>(key, Arg.Any<CancellationToken>()).Returns(OperationResult<string>.Ok(value));

    private async Task AssertNothingWrittenAsync()
    {
        await _unitOfWork.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync(default!, default);
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }
}
