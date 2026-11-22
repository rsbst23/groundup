using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Bootstrap;
using GroundUp.Core;
using GroundUp.Core.Results;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services;

/// <summary>
/// Decision paths of <see cref="IdentityBootstrapService"/> that a real database does not easily reach
/// (missing role, concurrent conflict). The full lifecycle runs against Postgres in
/// <c>IdentityBootstrapServiceIntegrationTests</c>.
/// </summary>
public sealed class IdentityBootstrapServiceTests
{
    private readonly IIdentityBootstrapRepository _repository = Substitute.For<IIdentityBootstrapRepository>();
    private readonly IdentityBootstrapService _sut;
    private readonly Guid _roleId = Guid.NewGuid();

    private static readonly ProvisionFirstSuperAdminRequest Request =
        new("admin@example.com", "Admin", "kc-admin", AuthRoleNames.SystemTenantId);

    public IdentityBootstrapServiceTests()
    {
        _sut = new IdentityBootstrapService(_repository, NullLogger<IdentityBootstrapService>.Instance);
        _repository.FindRoleIdByNameAsync(AuthRoleNames.SuperAdmin, Arg.Any<CancellationToken>()).Returns(_roleId);
    }

    [Fact]
    public async Task Provision_RoleMissing_Returns500AndWritesNothing()
    {
        _repository.FindRoleIdByNameAsync(AuthRoleNames.SuperAdmin, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await _sut.ProvisionFirstSuperAdminAsync(Request);

        result.StatusCode.Should().Be(500);
        await _repository.DidNotReceiveWithAnyArgs().EnsureSuperAdminAsync(default!, default);
    }

    [Fact]
    public async Task Provision_ExistingUserSameDisplayName_IdempotentWithoutWriting()
    {
        var existing = new UserDto(Guid.NewGuid(), "kc-admin", Request.Email, Request.DisplayName, true);
        _repository.FindUserByEmailAsync(Request.Email, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _sut.ProvisionFirstSuperAdminAsync(Request);

        result.Data.Should().Be(new BootstrapAdminResultDto(existing.Id, existing.Email, AlreadyExisted: true));
        await _repository.DidNotReceiveWithAnyArgs().EnsureSuperAdminAsync(default!, default);
    }

    [Fact]
    public async Task Provision_ExistingUserOtherDisplayName_409WithoutWriting()
    {
        _repository.FindUserByEmailAsync(Request.Email, Arg.Any<CancellationToken>())
            .Returns(new UserDto(Guid.NewGuid(), "kc-admin", Request.Email, "Someone Else", true));

        var result = await _sut.ProvisionFirstSuperAdminAsync(Request);

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        await _repository.DidNotReceiveWithAnyArgs().EnsureSuperAdminAsync(default!, default);
    }

    [Fact]
    public async Task Provision_NewUser_WritesUserMembershipAndRoleInSystemTenant()
    {
        var userId = Guid.NewGuid();
        _repository.EnsureSuperAdminAsync(Arg.Any<SuperAdminRecord>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<Guid>.Ok(userId));

        var result = await _sut.ProvisionFirstSuperAdminAsync(Request);

        result.Data.Should().Be(new BootstrapAdminResultDto(userId, Request.Email, AlreadyExisted: false));
        await _repository.Received(1).EnsureSuperAdminAsync(
            new SuperAdminRecord(Request.Email, Request.DisplayName, Request.ExternalUserId, AuthRoleNames.SystemTenantId, _roleId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Provision_ConcurrentInsertConflict_409()
    {
        _repository.EnsureSuperAdminAsync(Arg.Any<SuperAdminRecord>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<Guid>.Fail("A super admin user already exists with conflicting attributes.", 409, ErrorCodes.Conflict));

        var result = await _sut.ProvisionFirstSuperAdminAsync(Request);

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Message.Should().Be("A super admin user already exists with conflicting attributes.");
    }

    [Fact]
    public async Task Queries_UseTheSuperAdminRoleAndSystemTenant()
    {
        var firstId = Guid.NewGuid();
        _repository.AnyUserHasRoleAsync(AuthRoleNames.SuperAdmin, Arg.Any<CancellationToken>()).Returns(true);
        _repository.GetFirstUserIdWithRoleAsync(AuthRoleNames.SuperAdmin, Arg.Any<CancellationToken>()).Returns(firstId);
        _repository.TenantExistsAsync(AuthRoleNames.SystemTenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repository.RoleExistsAsync(AuthRoleNames.SuperAdmin, Arg.Any<CancellationToken>()).Returns(true);

        (await _sut.HasSuperAdminAsync()).Should().BeTrue();
        (await _sut.GetSuperAdminUserIdAsync()).Should().Be(firstId);
        (await _sut.HasSystemTenantAsync()).Should().BeTrue();
        (await _sut.HasSuperAdminRoleAsync()).Should().BeTrue();
    }
}
