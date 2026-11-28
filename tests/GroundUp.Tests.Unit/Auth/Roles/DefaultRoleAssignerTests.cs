using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core.Results;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Roles;

/// <summary>
/// GU-68 Requirement 5.7 and GU-95 Requirement 5: the tenant's default role, given when joining the
/// default tenant.
/// </summary>
public sealed class DefaultRoleAssignerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly RoleDto Member = new(Guid.NewGuid(), "Member", null, RoleType.Application, TenantId, false);

    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IRoleGrantWriter _writer = Substitute.For<IRoleGrantWriter>();
    private readonly DefaultRoleAssigner _sut;

    public DefaultRoleAssignerTests()
    {
        _roles.FindByIdAnyTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.NotFound());
        _roles.FindByIdAnyTenantAsync(Member.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(Member));
        _writer.GrantAsync(default, default, default, default, default, default)
            .ReturnsForAnyArgs(OperationResult<RoleGrantResult>.Ok(
                new RoleGrantResult(new UserRoleDto(Guid.NewGuid(), UserId, Member.Id, TenantId), Created: true)));
        _sut = new DefaultRoleAssigner(_tenants, _roles, _writer, NullLogger<DefaultRoleAssigner>.Instance);
    }

    [Fact]
    public async Task AssignAsync_TenantHasADefaultRole_GrantsItWithSourceDefaultRole()
    {
        GivenDefault(Member.Id);

        await _sut.AssignAsync(UserId, TenantId);

        await _writer.Received(1).GrantAsync(
            UserId, Member.Id, TenantId, RoleAssignmentSource.DefaultRole, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignAsync_NoDefaultRole_GrantsNothing()
    {
        _tenants.GetDefaultRoleIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(OperationResult<Guid?>.Ok(null));

        await _sut.AssignAsync(UserId, TenantId);

        await _writer.DidNotReceiveWithAnyArgs().GrantAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task AssignAsync_UnknownTenant_GrantsNothing()
    {
        _tenants.GetDefaultRoleIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(OperationResult<Guid?>.NotFound());

        await _sut.AssignAsync(UserId, TenantId);

        await _writer.DidNotReceiveWithAnyArgs().GrantAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task AssignAsync_DefaultRoleOfAnotherTenant_Skipped()
    {
        var foreign = Member with { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        _roles.FindByIdAnyTenantAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(foreign));
        GivenDefault(foreign.Id);

        await _sut.AssignAsync(UserId, TenantId);

        await _writer.DidNotReceiveWithAnyArgs().GrantAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task AssignAsync_DefaultRoleMissing_Skipped()
    {
        GivenDefault(Guid.NewGuid());

        await _sut.AssignAsync(UserId, TenantId);

        await _writer.DidNotReceiveWithAnyArgs().GrantAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task AssignAsync_ReadFails_GrantsNothingAndDoesNotThrow()
    {
        _tenants.GetDefaultRoleIdAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns<Task<OperationResult<Guid?>>>(_ => throw new InvalidOperationException("db down"));

        var act = () => _sut.AssignAsync(UserId, TenantId);

        await act.Should().NotThrowAsync();
        await _writer.DidNotReceiveWithAnyArgs().GrantAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task AssignAsync_GrantFails_DoesNotThrow()
    {
        GivenDefault(Member.Id);
        _writer.GrantAsync(default, default, default, default, default, default)
            .ReturnsForAnyArgs(OperationResult<RoleGrantResult>.Fail("nope", 409));

        var act = () => _sut.AssignAsync(UserId, TenantId);

        await act.Should().NotThrowAsync();
    }

    private void GivenDefault(Guid roleId)
        => _tenants.GetDefaultRoleIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(OperationResult<Guid?>.Ok(roleId));
}
