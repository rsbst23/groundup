using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Roles;

/// <summary>
/// GU-68 Requirements 5.6-5.7: the default role given when joining the default tenant.
/// </summary>
public sealed class DefaultRoleAssignerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly RoleDto Member = new(Guid.NewGuid(), "Member", null, RoleType.Application, TenantId, false);

    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IRoleGrantWriter _writer = Substitute.For<IRoleGrantWriter>();
    private readonly TenantContext _tenantContext = new();
    private readonly DefaultRoleAssigner _sut;

    public DefaultRoleAssignerTests()
    {
        _roles.GetByNameAsync(TenantId, "Member", Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(Member));
        _roles.GetByNameAsync(TenantId, Arg.Is<string>(n => n != "Member"), Arg.Any<CancellationToken>())
            .Returns(OperationResult<RoleDto>.NotFound());
        _writer.GrantAsync(default, default, default, default, default, default)
            .ReturnsForAnyArgs(OperationResult<RoleGrantResult>.Ok(
                new RoleGrantResult(new UserRoleDto(Guid.NewGuid(), UserId, Member.Id, TenantId), Created: true)));
        _sut = new DefaultRoleAssigner(_settings, _roles, _writer, _tenantContext, NullLogger<DefaultRoleAssigner>.Instance);
    }

    [Fact]
    public async Task AssignAsync_ConfiguredRoleExists_GrantsItWithSourceDefaultRole()
    {
        GivenSetting(" Member ");

        await _sut.AssignAsync(UserId, TenantId);

        await _writer.Received(1).GrantAsync(
            UserId, Member.Id, TenantId, RoleAssignmentSource.DefaultRole, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignAsync_ReadsTheSettingForTheJoinedTenant_AndRestoresTheContext()
    {
        var previous = Guid.NewGuid();
        _tenantContext.TenantId = previous;
        Guid seenTenant = Guid.Empty;
        _settings.GetAsync<string>(DefaultRoleAssigner.SettingKey, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                seenTenant = _tenantContext.TenantId;
                return OperationResult<string>.Ok("Member");
            });

        await _sut.AssignAsync(UserId, TenantId);

        seenTenant.Should().Be(TenantId, "the tenant value of the setting must be read for the joined tenant");
        _tenantContext.TenantId.Should().Be(previous);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Missing")]
    public async Task AssignAsync_NoRoleOrUnknownRole_GrantsNothing(string value)
    {
        GivenSetting(value);

        await _sut.AssignAsync(UserId, TenantId);

        await _writer.DidNotReceiveWithAnyArgs().GrantAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task AssignAsync_SettingsFail_GrantsNothingAndDoesNotThrow()
    {
        _settings.GetAsync<string>(DefaultRoleAssigner.SettingKey, Arg.Any<CancellationToken>())
            .Returns<Task<OperationResult<string>>>(_ => throw new InvalidOperationException("db down"));

        var act = () => _sut.AssignAsync(UserId, TenantId);

        await act.Should().NotThrowAsync();
        await _writer.DidNotReceiveWithAnyArgs().GrantAsync(default, default, default, default, default, default);
    }

    private void GivenSetting(string value)
        => _settings.GetAsync<string>(DefaultRoleAssigner.SettingKey, Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok(value));
}
