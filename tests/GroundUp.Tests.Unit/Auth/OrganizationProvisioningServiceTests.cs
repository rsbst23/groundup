using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Core.Validators;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

public sealed class OrganizationProvisioningServiceTests
{
    private readonly ITenantRepository _tenantRepository = Substitute.For<ITenantRepository>();
    private readonly IUserTenantRepository _userTenantRepository = Substitute.For<IUserTenantRepository>();
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IUserRoleRepository _userRoleRepository = Substitute.For<IUserRoleRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TenantContext _tenantContext = new();
    private readonly ILogger<OrganizationProvisioningService> _logger =
        Substitute.For<ILogger<OrganizationProvisioningService>>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly OrganizationProvisioningService _sut;

    private const string TestOrgName = "Acme Corp";
    private const string TestExternalUserId = "keycloak-sub-12345";

    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestRoleId = Guid.NewGuid();

    public OrganizationProvisioningServiceTests()
    {
        _sut = new OrganizationProvisioningService(
            _tenantRepository,
            _userTenantRepository,
            _roleRepository,
            _userRoleRepository,
            _unitOfWork,
            _tenantContext,
            _logger,
            _currentUser,
            _userRepository,
            new CreateOrganizationRequestDtoValidator());

        SetupSuccessfulRepositories();
        SetupUnitOfWorkThatRunsDelegate();
    }

    #region Happy path

    [Fact]
    public async Task ProvisionAsync_ValidRequest_ReturnsTenantIdNameAndSlug()
    {
        // Act
        var result = await _sut.ProvisionAsync(CreateRequest());

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.TenantId.Should().Be(TestTenantId);
        result.Data.Name.Should().Be(TestOrgName);
        result.Data.Slug.Should().Be("acme-corp");
    }

    [Fact]
    public async Task ProvisionAsync_ValidRequest_CreatesStandardInviteOnlyTenantWithoutRealm()
    {
        // Act
        await _sut.ProvisionAsync(CreateRequest());

        // Assert
        await _tenantRepository.Received(1).AddAsync(
            Arg.Is<TenantDto>(t =>
                t.Name == TestOrgName &&
                t.Slug == "acme-corp" &&
                t.TenantType == TenantType.Standard &&
                t.OnboardingMode == OnboardingMode.InviteOnly &&
                t.RealmName == null &&
                t.ParentTenantId == null &&
                t.IsActive),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_ValidRequest_CreatesActiveMembershipForFoundingUser()
    {
        // Act
        await _sut.ProvisionAsync(CreateRequest());

        // Assert
        await _userTenantRepository.Received(1).AddAsync(
            Arg.Is<UserTenantDto>(m =>
                m.UserId == TestUserId &&
                m.TenantId == TestTenantId &&
                m.ExternalUserId == TestExternalUserId &&
                m.IsActive),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_ValidRequest_CreatesTenantAdminSystemRoleInNewTenant()
    {
        // Act
        await _sut.ProvisionAsync(CreateRequest());

        // Assert
        await _roleRepository.Received(1).AddAsync(
            Arg.Is<RoleDto>(r =>
                r.Name == AuthRoleNames.TenantAdmin &&
                r.TenantId == TestTenantId &&
                r.IsSystem &&
                r.RoleType == RoleType.Application),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_ValidRequest_AssignsTenantAdminRoleToFoundingUser()
    {
        // Act
        await _sut.ProvisionAsync(CreateRequest());

        // Assert
        await _userRoleRepository.Received(1).AddAsync(
            Arg.Is<UserRoleDto>(ur =>
                ur.UserId == TestUserId &&
                ur.RoleId == TestRoleId &&
                ur.TenantId == TestTenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_ValidRequest_UsesSingleTransaction()
    {
        // Act
        await _sut.ProvisionAsync(CreateRequest());

        // Assert
        await _unitOfWork.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Acme Corp", "acme-corp")]
    [InlineData("  My   Org!! ", "my-org")]
    [InlineData("!!!", "org")]
    public async Task ProvisionAsync_OrganizationName_DerivesExpectedSlug(string name, string expectedSlug)
    {
        // Act
        var result = await _sut.ProvisionAsync(CreateRequest(name));

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Slug.Should().Be(expectedSlug);
    }

    #endregion

    #region Tenant context

    [Fact]
    public async Task ProvisionAsync_ValidRequest_PointsTenantContextAtNewTenantWhileWritingTenantScopedRows()
    {
        // Arrange — capture the tenant context at the moment the role is written
        Guid? contextDuringRoleWrite = null;
        _roleRepository.AddAsync(Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                contextDuringRoleWrite = _tenantContext.TenantId;
                return OperationResult<RoleDto>.Ok(callInfo.ArgAt<RoleDto>(0) with { Id = TestRoleId });
            });

        // Act
        await _sut.ProvisionAsync(CreateRequest());

        // Assert
        contextDuringRoleWrite.Should().Be(TestTenantId);
    }

    [Fact]
    public async Task ProvisionAsync_ValidRequest_RestoresPreviousTenantContext()
    {
        // Arrange — the caller was working inside some other tenant
        var callerTenantId = Guid.NewGuid();
        _tenantContext.TenantId = callerTenantId;

        // Act
        await _sut.ProvisionAsync(CreateRequest());

        // Assert
        _tenantContext.TenantId.Should().Be(callerTenantId);
    }

    [Fact]
    public async Task ProvisionAsync_SlugExhaustion_RestoresPreviousTenantContext()
    {
        // Arrange
        var callerTenantId = Guid.NewGuid();
        _tenantContext.TenantId = callerTenantId;
        _tenantRepository.AddAsync(Arg.Any<TenantDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<TenantDto>.Fail("Unique constraint violation", 409));

        // Act
        await _sut.ProvisionAsync(CreateRequest());

        // Assert
        _tenantContext.TenantId.Should().Be(callerTenantId);
    }

    [Fact]
    public async Task ProvisionAsync_UnexpectedFailure_RestoresPreviousTenantContext()
    {
        // Arrange
        var callerTenantId = Guid.NewGuid();
        _tenantContext.TenantId = callerTenantId;
        _roleRepository.AddAsync(Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<RoleDto>.Fail("Database unavailable", 500));

        // Act
        Func<Task> act = () => _sut.ProvisionAsync(CreateRequest());
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert
        _tenantContext.TenantId.Should().Be(callerTenantId);
    }

    #endregion

    #region Slug collisions

    [Fact]
    public async Task ProvisionAsync_SlugCollisionOnFirstAttempt_RetriesWithDisambiguatedSlug()
    {
        // Arrange — first tenant insert fails (slug taken), second succeeds
        var tenantInsertCount = 0;
        _tenantRepository.AddAsync(Arg.Any<TenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                tenantInsertCount++;
                var dto = callInfo.ArgAt<TenantDto>(0);
                return tenantInsertCount == 1
                    ? OperationResult<TenantDto>.Fail("Unique constraint violation", 409)
                    : OperationResult<TenantDto>.Ok(dto with { Id = TestTenantId });
            });

        // Act
        var result = await _sut.ProvisionAsync(CreateRequest());

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Slug.Should().Be("acme-corp-2");
        await _tenantRepository.Received(1).AddAsync(
            Arg.Is<TenantDto>(t => t.Slug == "acme-corp"), Arg.Any<CancellationToken>());
        await _tenantRepository.Received(1).AddAsync(
            Arg.Is<TenantDto>(t => t.Slug == "acme-corp-2"), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(2).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_TransactionReportsConflictOnFirstAttempt_RetriesWithDisambiguatedSlug()
    {
        // Arrange — the unit of work reports a unique-constraint conflict as a failed result
        var transactionCount = 0;
        _unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                transactionCount++;
                return transactionCount == 1
                    ? Task.FromResult(OperationResult.Fail("Unique constraint violation", 409))
                    : RunDelegateAsync(callInfo.ArgAt<Func<CancellationToken, Task>>(0));
            });

        // Act
        var result = await _sut.ProvisionAsync(CreateRequest());

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Slug.Should().Be("acme-corp-2");
    }

    [Fact]
    public async Task ProvisionAsync_SlugCollisionOnEveryAttempt_ReturnsSlugCollisionFailure()
    {
        // Arrange
        _tenantRepository.AddAsync(Arg.Any<TenantDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<TenantDto>.Fail("Unique constraint violation", 409));

        // Act
        var result = await _sut.ProvisionAsync(CreateRequest());

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("SLUG_COLLISION");
        await _unitOfWork.Received(5).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_TransactionAlwaysReportsConflict_ReturnsSlugCollisionFailure()
    {
        // Arrange
        _unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult.Fail("Unique constraint violation", 409)));

        // Act
        var result = await _sut.ProvisionAsync(CreateRequest());

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("SLUG_COLLISION");
        await _unitOfWork.Received(5).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_SlugCollisionOnEveryAttempt_DoesNotCreateMembershipOrRoles()
    {
        // Arrange
        _tenantRepository.AddAsync(Arg.Any<TenantDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<TenantDto>.Fail("Unique constraint violation", 409));

        // Act
        await _sut.ProvisionAsync(CreateRequest());

        // Assert
        await _userTenantRepository.DidNotReceive().AddAsync(
            Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>());
        await _roleRepository.DidNotReceive().AddAsync(
            Arg.Any<RoleDto>(), Arg.Any<CancellationToken>());
        await _userRoleRepository.DidNotReceive().AddAsync(
            Arg.Any<UserRoleDto>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Unexpected failures roll back instead of reporting success

    [Fact]
    public async Task ProvisionAsync_MembershipCreationFails_ThrowsSoTheTransactionRollsBack()
    {
        // Arrange
        _userTenantRepository.AddAsync(Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserTenantDto>.Fail("Database unavailable", 500));

        // Act
        Func<Task> act = () => _sut.ProvisionAsync(CreateRequest());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _roleRepository.DidNotReceive().AddAsync(
            Arg.Any<RoleDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_RoleCreationFails_ThrowsSoTheTransactionRollsBack()
    {
        // Arrange
        _roleRepository.AddAsync(Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<RoleDto>.Fail("Database unavailable", 500));

        // Act
        Func<Task> act = () => _sut.ProvisionAsync(CreateRequest());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _userRoleRepository.DidNotReceive().AddAsync(
            Arg.Any<UserRoleDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_RoleAssignmentFails_ThrowsSoTheTransactionRollsBack()
    {
        // Arrange
        _userRoleRepository.AddAsync(Arg.Any<UserRoleDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserRoleDto>.Fail("Database unavailable", 500));

        // Act
        Func<Task> act = () => _sut.ProvisionAsync(CreateRequest());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ProvisionAsync_UnexpectedFailure_DoesNotRetryWithAnotherSlug()
    {
        // Arrange
        _roleRepository.AddAsync(Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<RoleDto>.Fail("Database unavailable", 500));

        // Act
        Func<Task> act = () => _sut.ProvisionAsync(CreateRequest());
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert — a non-slug failure must not be misreported as a slug collision
        await _unitOfWork.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region CreateForCurrentUserAsync (POST /auth/organizations)

    private void GivenCurrentUser(bool exists = true, bool active = true)
    {
        _currentUser.UserId.Returns(TestUserId);
        _userRepository.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(exists
                ? OperationResult<UserDto>.Ok(new UserDto(TestUserId, TestExternalUserId, "creator@example.com", "Creator", active))
                : OperationResult<UserDto>.NotFound());
    }

    private async Task AssertNothingCreatedAsync()
        => await _tenantRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);

    [Fact]
    public async Task CreateForCurrentUserAsync_NoUser_Unauthorized()
    {
        _currentUser.UserId.Returns(Guid.Empty);

        var result = await _sut.CreateForCurrentUserAsync(new CreateOrganizationRequestDto(TestOrgName));

        result.StatusCode.Should().Be(401);
        await AssertNothingCreatedAsync();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CreateForCurrentUserAsync_UserMissingOrInactive_Unauthorized(bool exists, bool active)
    {
        GivenCurrentUser(exists, active);

        var result = await _sut.CreateForCurrentUserAsync(new CreateOrganizationRequestDto(TestOrgName));

        result.StatusCode.Should().Be(401);
        await AssertNothingCreatedAsync();
    }

    [Theory]
    [InlineData(null, "Organization name is required.")]
    [InlineData("   ", "Organization name is required.")]
    public async Task CreateForCurrentUserAsync_InvalidName_BadRequestWithMessage(string? name, string message)
    {
        GivenCurrentUser();

        var result = await _sut.CreateForCurrentUserAsync(new CreateOrganizationRequestDto(name));

        result.StatusCode.Should().Be(400);
        result.Message.Should().Be(message);
        await AssertNothingCreatedAsync();
    }

    [Fact]
    public async Task CreateForCurrentUserAsync_NameTooLong_BadRequest()
    {
        GivenCurrentUser();

        var result = await _sut.CreateForCurrentUserAsync(new CreateOrganizationRequestDto(new string('a', 201)));

        result.StatusCode.Should().Be(400);
        result.Message.Should().Be("Organization name must not exceed 200 characters.");
    }

    [Fact]
    public async Task CreateForCurrentUserAsync_UnauthenticatedWithInvalidName_UnauthorizedNotBadRequest()
    {
        _currentUser.UserId.Returns(Guid.Empty);

        var result = await _sut.CreateForCurrentUserAsync(new CreateOrganizationRequestDto(null));

        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task CreateForCurrentUserAsync_Valid_ProvisionsForUserWithTrimmedNameAndExternalId()
    {
        GivenCurrentUser();

        var result = await _sut.CreateForCurrentUserAsync(new CreateOrganizationRequestDto("  Acme Corp  "));

        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Acme Corp");
        await _tenantRepository.Received(1).AddAsync(
            Arg.Is<TenantDto>(t => t.Name == "Acme Corp" && t.Slug == "acme-corp"), Arg.Any<CancellationToken>());
        await _userTenantRepository.Received(1).AddAsync(
            Arg.Is<UserTenantDto>(m => m.UserId == TestUserId && m.ExternalUserId == TestExternalUserId),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helpers

    private static OrganizationProvisioningRequest CreateRequest(string organizationName = TestOrgName) =>
        new(TestUserId, TestExternalUserId, organizationName);

    private void SetupSuccessfulRepositories()
    {
        _tenantRepository.AddAsync(Arg.Any<TenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                OperationResult<TenantDto>.Ok(callInfo.ArgAt<TenantDto>(0) with { Id = TestTenantId }));

        _userTenantRepository.AddAsync(Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OperationResult<UserTenantDto>.Ok(callInfo.ArgAt<UserTenantDto>(0)));

        _roleRepository.AddAsync(Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                OperationResult<RoleDto>.Ok(callInfo.ArgAt<RoleDto>(0) with { Id = TestRoleId }));

        _userRoleRepository.AddAsync(Arg.Any<UserRoleDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OperationResult<UserRoleDto>.Ok(callInfo.ArgAt<UserRoleDto>(0)));
    }

    /// <summary>
    /// Mimics the production unit of work: runs the delegate and lets exceptions propagate.
    /// </summary>
    private void SetupUnitOfWorkThatRunsDelegate()
    {
        _unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => RunDelegateAsync(callInfo.ArgAt<Func<CancellationToken, Task>>(0)));
    }

    private static async Task<OperationResult> RunDelegateAsync(Func<CancellationToken, Task> operation)
    {
        await operation(CancellationToken.None);
        return OperationResult.Ok();
    }

    #endregion
}
