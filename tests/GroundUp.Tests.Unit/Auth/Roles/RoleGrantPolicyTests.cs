using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Roles;

/// <summary>
/// GU-68 Requirement 3.2: who may grant or remove each kind of role, and the application rule hook.
/// </summary>
public sealed class RoleGrantPolicyTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();

    private static readonly RoleDto SuperAdminRole =
        new(Guid.NewGuid(), AuthRoleNames.SuperAdmin, null, RoleType.System, AuthRoleNames.SystemTenantId, true);
    private static readonly RoleDto TenantAdminRole =
        new(Guid.NewGuid(), AuthRoleNames.TenantAdmin, null, RoleType.Application, TenantA, true);
    private static readonly RoleDto EditorRole =
        new(Guid.NewGuid(), "Editor", null, RoleType.Application, TenantA, false);

    private readonly IUserRoleRepository _userRoles = Substitute.For<IUserRoleRepository>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly List<IRoleAssignmentRule> _rules = [];

    public RoleGrantPolicyTests()
    {
        _tenantContext.TenantId.Returns(TenantA);
        _userRoles.GetSystemRolesForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([]));
        _userRoles.GetByUserIdForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([]));
    }

    public enum Caller { SuperAdmin, TenantAdmin, TenantAdminOfOtherTenant, AssignPermission, AssignPermissionInOtherContext, Nobody }

    public enum Kind { SuperAdmin, TenantAdmin, Other }

    /// <summary>The whole matrix, for both assign and remove (the rules are the same).</summary>
    public static IEnumerable<object[]> Matrix()
    {
        var allowed = new Dictionary<Kind, Caller[]>
        {
            [Kind.SuperAdmin] = [Caller.SuperAdmin],
            [Kind.TenantAdmin] = [Caller.SuperAdmin, Caller.TenantAdmin],
            [Kind.Other] = [Caller.SuperAdmin, Caller.TenantAdmin, Caller.AssignPermission]
        };

        foreach (var change in new[] { RoleChange.Assign, RoleChange.Remove })
        foreach (var kind in Enum.GetValues<Kind>())
        foreach (var caller in Enum.GetValues<Caller>())
        {
            yield return [caller, kind, change, allowed[kind].Contains(caller)];
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task CheckAsync_Matrix(Caller caller, Kind kind, RoleChange change, bool expectedAllowed)
    {
        GivenCaller(caller);

        var result = await CreateSut().CheckAsync(CallerId, TargetId, RoleFor(kind), change, RoleAssignmentSource.Manual);

        result.Success.Should().Be(expectedAllowed);
        if (!expectedAllowed)
        {
            result.StatusCode.Should().Be(403);
            result.ErrorCode.Should().Be(GroundUp.Core.ErrorCodes.Forbidden);
        }
    }

    [Fact]
    public async Task CheckAsync_TenantAdminWithFullPermissions_CannotGrantSuperAdmin()
    {
        // A TenantAdmin passes every permission check in their tenant; the SuperAdmin rule must not rely on it
        GivenCaller(Caller.TenantAdmin);
        _permissions.HasPermissionAsync(CallerId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateSut().CheckAsync(CallerId, TargetId, SuperAdminRole, RoleChange.Assign, RoleAssignmentSource.Manual);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task CheckAsync_AssignPermissionHolder_CannotGrantTenantAdmin()
    {
        GivenCaller(Caller.AssignPermission);

        var result = await CreateSut().CheckAsync(CallerId, TargetId, TenantAdminRole, RoleChange.Assign, RoleAssignmentSource.Manual);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task CheckAsync_NoCaller_Refused()
    {
        var result = await CreateSut().CheckAsync(Guid.Empty, TargetId, EditorRole, RoleChange.Assign, RoleAssignmentSource.Manual);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task CheckAsync_AppRuleRefuses_ReturnsItsFailure()
    {
        GivenCaller(Caller.TenantAdmin);
        var rule = Substitute.For<IRoleAssignmentRule>();
        rule.EvaluateAsync(Arg.Any<RoleAssignmentContext>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Forbidden("No self-assignment."));
        _rules.Add(rule);

        var result = await CreateSut().CheckAsync(CallerId, TargetId, EditorRole, RoleChange.Assign, RoleAssignmentSource.Invitation);

        result.Message.Should().Be("No self-assignment.");
        await rule.Received(1).EvaluateAsync(
            new RoleAssignmentContext(CallerId, TargetId, EditorRole, TenantA, RoleChange.Assign, RoleAssignmentSource.Invitation),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAsync_RulesRunInOrder_FirstRefusalWins()
    {
        GivenCaller(Caller.TenantAdmin);
        var first = Substitute.For<IRoleAssignmentRule>();
        var second = Substitute.For<IRoleAssignmentRule>();
        first.EvaluateAsync(default!, default).ReturnsForAnyArgs(OperationResult.Forbidden("first"));
        second.EvaluateAsync(default!, default).ReturnsForAnyArgs(OperationResult.Ok());
        _rules.Add(first);
        _rules.Add(second);

        var result = await CreateSut().CheckAsync(CallerId, TargetId, EditorRole, RoleChange.Remove, RoleAssignmentSource.Manual);

        result.Message.Should().Be("first");
        await second.DidNotReceiveWithAnyArgs().EvaluateAsync(default!, default);
    }

    [Fact]
    public async Task CheckAsync_BuiltInRuleRefuses_AppRulesDoNotRun()
    {
        GivenCaller(Caller.Nobody);
        var rule = Substitute.For<IRoleAssignmentRule>();
        _rules.Add(rule);

        await CreateSut().CheckAsync(CallerId, TargetId, EditorRole, RoleChange.Assign, RoleAssignmentSource.Manual);

        await rule.DidNotReceiveWithAnyArgs().EvaluateAsync(default!, default);
    }

    private RoleGrantPolicy CreateSut() => new(_userRoles, _permissions, _tenantContext, _rules);

    private static RoleDto RoleFor(Kind kind) => kind switch
    {
        Kind.SuperAdmin => SuperAdminRole,
        Kind.TenantAdmin => TenantAdminRole,
        _ => EditorRole
    };

    private void GivenCaller(Caller caller)
    {
        switch (caller)
        {
            case Caller.SuperAdmin:
                _userRoles.GetSystemRolesForUserAsync(CallerId, Arg.Any<CancellationToken>())
                    .Returns(OperationResult<List<UserRoleDto>>.Ok(
                        [new UserRoleDto(Guid.NewGuid(), CallerId, SuperAdminRole.Id, AuthRoleNames.SystemTenantId, AuthRoleNames.SuperAdmin)]));
                break;
            case Caller.TenantAdmin:
                GivenTenantAdminOf(TenantA);
                break;
            case Caller.TenantAdminOfOtherTenant:
                GivenTenantAdminOf(TenantB);
                break;
            case Caller.AssignPermission:
                _permissions.HasPermissionAsync(CallerId, PermissionKeysAuth.RolesAssign, Arg.Any<CancellationToken>()).Returns(true);
                break;
            case Caller.AssignPermissionInOtherContext:
                // Holds the permission, but the session is in another tenant than the role's
                _tenantContext.TenantId.Returns(TenantB);
                _permissions.HasPermissionAsync(CallerId, PermissionKeysAuth.RolesAssign, Arg.Any<CancellationToken>()).Returns(true);
                break;
        }
    }

    private void GivenTenantAdminOf(Guid tenantId)
        => _userRoles.GetByUserIdForTenantAsync(CallerId, tenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(
                [new UserRoleDto(Guid.NewGuid(), CallerId, Guid.NewGuid(), tenantId, AuthRoleNames.TenantAdmin)]));
}
