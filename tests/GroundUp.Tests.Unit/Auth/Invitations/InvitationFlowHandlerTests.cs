using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Invitations;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Tests.Unit.Auth.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Invitations;

/// <summary>
/// GU-68 Requirement 7.3: the optional verified-email rule on the invitation callback. The rest of
/// the callback is covered end to end by the integration tests.
/// </summary>
public sealed class InvitationFlowHandlerTests
{
    private const string Email = "ana@example.com";
    private const string Sub = "kc-ana";

    private readonly IIdentityProviderService _idp = Substitute.For<IIdentityProviderService>();
    private readonly IAuthFlowStateService _flowStates = Substitute.For<IAuthFlowStateService>();
    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IUserTenantRepository _userTenants = Substitute.For<IUserTenantRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly InvitationDto _invitation;
    private readonly InvitationFlowHandler _sut;

    public InvitationFlowHandlerTests()
    {
        _invitation = new InvitationDto(Guid.NewGuid(), Guid.NewGuid(), Email, Guid.NewGuid(), "Editor", Guid.NewGuid(),
            Guid.NewGuid(), DateTime.UtcNow.AddDays(1), InvitationStatus.Pending, null, null, null, DateTime.UtcNow);
        _invitations.GetByIdAnyTenantAsync(_invitation.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<InvitationDto>.Ok(_invitation));

        _idp.ExchangeCodeForTokensAsync(default!, default!, default, default, default)
            .ReturnsForAnyArgs(new TokenResponseDto("access", "refresh", 300, IdTokenWithNonce("nonce-1")));

        // Past the email checks, the run stops at a recognizable point: the role cannot be found
        _users.GetByExternalUserIdAsync(Sub, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(new UserDto(_invitation.UserId, Sub, Email, "Ana", true)));
        _userTenants.GetAllMembershipsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok([]));
        _roles.FindByIdAnyTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.NotFound());

        var tenantContext = new TenantContext();
        _sut = new InvitationFlowHandler(
            _idp,
            FlowHandlerTestDoubles.DecodingIdTokenValidator(),
            FlowHandlerTestDoubles.SessionGuard(),
            _flowStates,
            _invitations,
            _roles,
            _userTenants,
            FlowHandlerTestDoubles.UserResolver(_users),
            new RoleGrantPolicy(Substitute.For<IUserRoleRepository>(), Substitute.For<IPermissionService>(), tenantContext, []),
            Substitute.For<IUnitOfWork>(),
            _settings,
            Substitute.For<ITokenService>(),
            Substitute.For<IAuthCookieWriter>(),
            Substitute.For<IHttpContextAccessor>(),
            tenantContext,
            FlowHandlerTestDoubles.AccessRevoker(),
            NullLogger<InvitationFlowHandler>.Instance);
    }

    [Fact]
    public async Task Callback_VerificationRequiredAndEmailUnverified_Refused()
    {
        GivenRequireVerified(true);
        GivenUserInfo(verified: false);

        var result = await _sut.HandleCallbackAsync(Context());

        result.ErrorCode.Should().Be(AuthErrorCodes.EmailNotVerified);
        result.HttpStatus.Should().Be(403);
    }

    [Fact]
    public async Task Callback_VerificationRequiredAndEmailVerified_PassesTheEmailChecks()
    {
        GivenRequireVerified(true);
        GivenUserInfo(verified: true);

        var result = await _sut.HandleCallbackAsync(Context());

        result.ErrorCode.Should().Be(AuthErrorCodes.InvitationNoLongerValid, "the run reaches the creator re-check");
    }

    [Fact]
    public async Task Callback_VerificationNotRequired_UnverifiedEmailPassesBecauseTheLinkIsProof()
    {
        GivenRequireVerified(false);
        GivenUserInfo(verified: false);

        var result = await _sut.HandleCallbackAsync(Context());

        result.ErrorCode.Should().Be(AuthErrorCodes.InvitationNoLongerValid, "the run reaches the creator re-check");
    }

    [Fact]
    public async Task Callback_SettingCannotBeRead_RequiresVerification()
    {
        _settings.GetAsync<bool>(InvitationSettingKeys.RequireVerifiedEmail, Arg.Any<CancellationToken>())
            .Returns<Task<OperationResult<bool>>>(_ => throw new InvalidOperationException("db down"));
        GivenUserInfo(verified: false);

        var result = await _sut.HandleCallbackAsync(Context());

        result.ErrorCode.Should().Be(AuthErrorCodes.EmailNotVerified);
    }

    private void GivenRequireVerified(bool value)
        => _settings.GetAsync<bool>(InvitationSettingKeys.RequireVerifiedEmail, Arg.Any<CancellationToken>())
            .Returns(OperationResult<bool>.Ok(value));

    private void GivenUserInfo(bool verified)
        => _idp.GetUserInfoAsync(Arg.Any<string>()).Returns(new ExternalUserInfo(Sub, Email, "Ana",
            new Dictionary<string, string> { ["email_verified"] = verified ? "true" : "false" }));

    private FlowCallbackContext Context()
        => new("code", "verifier", "https://app.test/auth/callback",
            new AuthFlowStateDto(
                Id: Guid.NewGuid(), FlowType: FlowType.Invitation, Status: FlowStatus.Consumed, TenantId: null,
                InvitationId: _invitation.Id, JoinLinkId: null, Realm: null, ReturnUrl: null, StateToken: "state",
                CodeVerifier: "verifier", RedirectUri: "https://app.test/auth/callback", OrganizationName: null,
                Nonce: "nonce-1", CreatedByIp: null, CreatedByUserAgent: null, ExpiresAt: DateTime.UtcNow.AddMinutes(10),
                ConsumedAt: DateTime.UtcNow, TerminatedAt: null, FailureReason: null, CreatedAt: DateTime.UtcNow, UpdatedAt: null),
            HostResolvedTenant: null);

    /// <summary>An unsigned ID token; the test validator only decodes it and compares the nonce.</summary>
    private static string IdTokenWithNonce(string nonce)
    {
        static string Encode(string json) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"RS256\",\"typ\":\"JWT\"}")}.{Encode($"{{\"nonce\":\"{nonce}\",\"sub\":\"{Sub}\"}}")}.fake-signature";
    }
}
