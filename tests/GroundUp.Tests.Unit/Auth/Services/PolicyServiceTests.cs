using System.Reflection;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Validators;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Policies;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Attributes;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using CoreErrorCodes = GroundUp.Core.ErrorCodes;

namespace GroundUp.Tests.Unit.Auth.Services;

/// <summary>
/// GU-96: the policy service's rules (catalogue, keys, names, the escalation guard, usage), cache
/// version bumps and events. Permissions are enforced by the authorization proxy (attributes below).
/// </summary>
public sealed class PolicyServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    private static readonly PermissionDto DocsRead = new(Guid.NewGuid(), "docs.read", "Read docs", null, "docs");
    private static readonly PermissionDto DocsWrite = new(Guid.NewGuid(), "docs.write", "Write docs", null, "docs");
    private static readonly PermissionDto SettingsRead = new(Guid.NewGuid(), "settings.read", "Read settings", null, "settings");
    private static readonly PermissionDto AssignSystem = new(Guid.NewGuid(), PermissionKeysAuth.RolesAssignSystem, "Assign system roles", null, "auth");
    private static readonly PermissionDto[] Catalogue = [DocsWrite, SettingsRead, AssignSystem, DocsRead];

    private static readonly PolicyDetailDto Editors = new(Guid.NewGuid(), "Editors", null, [DocsRead], []);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IPolicyRepository _policies = Substitute.For<IPolicyRepository>();
    private readonly IPermissionRepository _permissionCatalogue = Substitute.For<IPermissionRepository>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly PermissionCacheVersions _versions = new();
    private readonly PolicyService _sut;

    public PolicyServiceTests()
    {
        _currentUser.UserId.Returns(CallerId);
        _tenantContext.TenantId.Returns(TenantId);
        _permissionCatalogue.ListAllAsync(Arg.Any<CancellationToken>()).Returns(OperationResult<List<PermissionDto>>.Ok(Catalogue.ToList()));
        _permissionCatalogue.GetByKeysAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => OperationResult<List<PermissionDto>>.Ok(
                Catalogue.Where(p => call.Arg<IReadOnlyCollection<string>>().Contains(p.Key)).ToList()));
        _policies.GetDetailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OperationResult<PolicyDetailDto>.NotFound());
        GivenPolicy(Editors);
        GivenCallerHolds("docs.read", "docs.write", "settings.read");
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => RunAsync(call.Arg<Func<CancellationToken, Task>>()));

        _sut = CreateSut(withEventBus: true);
    }

    // ── Current tenant ────────────────────────────────────────────

    public static TheoryData<string> AllMethods => new()
    {
        "Catalog", "List", "Get", "Create", "Update", "SetPermissions", "Delete"
    };

    [Theory]
    [MemberData(nameof(AllMethods))]
    public async Task AnyMethod_NoTenantSelected_TenantRequired(string method)
    {
        _tenantContext.TenantId.Returns(Guid.Empty);

        var (status, code) = await CallAsync(method);

        status.Should().Be(403);
        code.Should().Be(CoreErrorCodes.TenantRequired);
    }

    [Theory]
    [InlineData("Get", 404)]
    [InlineData("Update", 404)]
    [InlineData("SetPermissions", 404)]
    [InlineData("Delete", 404)]
    [InlineData("Create", 403)]
    public async Task SystemTenant_NotReachable(string method, int expected)
    {
        _tenantContext.TenantId.Returns(AuthRoleNames.SystemTenantId);

        (await CallAsync(method)).Status.Should().Be(expected);
    }

    [Fact]
    public async Task ListAsync_SystemTenant_Empty()
    {
        _tenantContext.TenantId.Returns(AuthRoleNames.SystemTenantId);

        (await _sut.ListAsync(new FilterParams())).Data!.Items.Should().BeEmpty();
        await _policies.DidNotReceiveWithAnyArgs().ListAsync(default!, default);
    }

    // ── Catalogue ─────────────────────────────────────────────────

    [Fact]
    public async Task ListCatalogAsync_PassesTheRepositoryPageThrough()
    {
        var filters = new FilterParams { SearchTerm = "docs", Filters = new() { ["module"] = "docs" } };
        var page = new PaginatedData<PermissionDto> { Items = Catalogue.Take(1).ToList(), PageNumber = 1, PageSize = 25, TotalRecords = 1 };
        _permissionCatalogue.ListCatalogAsync(filters, Arg.Any<CancellationToken>()).Returns(OperationResult<PaginatedData<PermissionDto>>.Ok(page));

        var result = await _sut.ListCatalogAsync(filters);

        result.Data.Should().BeSameAs(page);
    }

    [Fact]
    public async Task GetModulesAsync_ReadsTheRepository()
    {
        _permissionCatalogue.ListModulesAsync(Arg.Any<CancellationToken>()).Returns(OperationResult<List<string>>.Ok(["docs", "settings"]));

        (await _sut.GetModulesAsync()).Data.Should().Equal("docs", "settings");
    }

    [Fact]
    public void SystemOnly_IsTheAgreedKeys()
        => PermissionKeysAuth.SystemOnly.Should().BeEquivalentTo(
            PermissionKeysAuth.RolesAssignSystem, PermissionKeysAuth.TenantsWrite, PermissionKeysAuth.TenantsDelete);

    // ── Create ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NameOnly_CreatedWithoutPermissionsOrBump()
    {
        var created = GivenAddReturns("Auditors");

        var result = await _sut.CreateAsync(new CreatePolicyDto("  Auditors ", " "));

        result.StatusCode.Should().Be(201);
        await _policies.Received(1).AddAsync(
            Arg.Is<PolicyDto>(p => p.Name == "Auditors" && p.Description == null && p.TenantId == TenantId), Arg.Any<CancellationToken>());
        await _policies.DidNotReceiveWithAnyArgs().ReplacePermissionsAsync(default, default!, default);
        _versions.Current(TenantId).Should().Be(0);
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<EntityCreatedEvent<PolicyDto>>(e => e.Entity == created && e.TenantId == TenantId && e.UserId == CallerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithKeys_LinksThemInTheTransactionAndPublishesEachLink()
    {
        var created = GivenAddReturns("Writers");
        _policies.ReplacePermissionsAsync(created.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PolicyPermissionChangesDto>.Ok(new PolicyPermissionChangesDto(
                [new PolicyPermissionDto(Guid.NewGuid(), created.Id, DocsRead.Id), new PolicyPermissionDto(Guid.NewGuid(), created.Id, DocsWrite.Id)], [])));

        var result = await _sut.CreateAsync(new CreatePolicyDto("Writers", null, [" docs.write", "docs.read", "docs.read"]));

        result.StatusCode.Should().Be(201);
        await _policies.Received(1).ReplacePermissionsAsync(
            created.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(DocsRead.Id) && ids.Contains(DocsWrite.Id)),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
        await _eventBus.Received(2).PublishAsync(Arg.Any<EntityCreatedEvent<PolicyPermissionDto>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_UnknownAndSystemOnlyKeys_BadRequestListingThem()
    {
        var result = await _sut.CreateAsync(new CreatePolicyDto("Bad", null, ["docs.read", "nope.nope", PermissionKeysAuth.RolesAssignSystem]));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be(CoreErrorCodes.ValidationFailed);
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.Contains("nope.nope"));
        result.Errors.Should().Contain(e => e.Contains(PermissionKeysAuth.RolesAssignSystem));
        await _policies.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task CreateAsync_KeyTheCallerLacks_PermissionNotHeld()
    {
        GivenCallerHolds("docs.read");

        var result = await _sut.CreateAsync(new CreatePolicyDto("Writers", null, ["docs.read", "docs.write"]));

        result.ErrorCode.Should().Be(AuthErrorCodes.PermissionNotHeld);
        result.Errors.Should().Equal("docs.write");
        await _policies.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task CreateAsync_NameTaken_PolicyNameTaken()
    {
        _policies.AddAsync(Arg.Any<PolicyDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PolicyDto>.Fail("conflict", 409, CoreErrorCodes.Conflict));

        var result = await _sut.CreateAsync(new CreatePolicyDto("editors", null));

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(AuthErrorCodes.PolicyNameTaken);
        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync<EntityCreatedEvent<PolicyDto>>(default!, default);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ValidationFailed()
        => (await _sut.CreateAsync(new CreatePolicyDto(" ", null))).ErrorCode.Should().Be(CoreErrorCodes.ValidationFailed);

    // ── Update ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_Rename_KeepsThePermissionsAndPublishes()
    {
        _policies.UpdateAsync(Editors.Id, Arg.Any<PolicyDto>(), Arg.Any<CancellationToken>())
            .Returns(call => OperationResult<PolicyDto>.Ok(call.Arg<PolicyDto>()));

        var result = await _sut.UpdateAsync(Editors.Id, new UpdatePolicyDto(" Writers ", "Write"));

        result.Data!.Name.Should().Be("Writers");
        result.Data.Description.Should().Be("Write");
        result.Data.Permissions.Should().Equal(Editors.Permissions);
        await _eventBus.Received(1).PublishAsync(Arg.Is<EntityUpdatedEvent<PolicyDto>>(e => e.Entity.Name == "Writers"), Arg.Any<CancellationToken>());
        _versions.Current(TenantId).Should().Be(0, "a rename changes no permission");
    }

    [Fact]
    public async Task UpdateAsync_NameTaken_PolicyNameTaken()
    {
        _policies.UpdateAsync(Editors.Id, Arg.Any<PolicyDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PolicyDto>.Fail("conflict", 409, CoreErrorCodes.Conflict));

        (await _sut.UpdateAsync(Editors.Id, new UpdatePolicyDto("Auditors", null))).ErrorCode.Should().Be(AuthErrorCodes.PolicyNameTaken);
    }

    [Fact]
    public async Task UpdateAsync_UnknownPolicy_NotFound()
        => (await _sut.UpdateAsync(Guid.NewGuid(), new UpdatePolicyDto("X", null))).StatusCode.Should().Be(404);

    // ── Set permissions ───────────────────────────────────────────

    [Fact]
    public async Task SetPermissionsAsync_OnlyAddedKeysAreGuarded()
    {
        // Editors already grants docs.read, which the caller no longer holds; keeping it is allowed
        GivenCallerHolds("docs.write");
        GivenReplaceAdds(DocsWrite);

        var result = await _sut.SetPermissionsAsync(Editors.Id, new SetPolicyPermissionsDto(["docs.read", "docs.write"]));

        result.Success.Should().BeTrue(result.Message);
        result.Data!.Permissions.Select(p => p.Key).Should().Equal("docs.read", "docs.write");
    }

    [Fact]
    public async Task SetPermissionsAsync_AddingAKeyTheCallerLacks_PermissionNotHeld()
    {
        GivenCallerHolds("docs.read");

        var result = await _sut.SetPermissionsAsync(Editors.Id, new SetPolicyPermissionsDto(["docs.read", "settings.read"]));

        result.ErrorCode.Should().Be(AuthErrorCodes.PermissionNotHeld);
        result.Errors.Should().Equal("settings.read");
        await _policies.DidNotReceiveWithAnyArgs().ReplacePermissionsAsync(default, default!, default);
    }

    [Fact]
    public async Task SetPermissionsAsync_RemovingAKeyTheCallerLacks_Allowed()
    {
        GivenCallerHolds();
        _policies.ReplacePermissionsAsync(Editors.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PolicyPermissionChangesDto>.Ok(new PolicyPermissionChangesDto([], [new PolicyPermissionDto(Guid.NewGuid(), Editors.Id, DocsRead.Id)])));

        var result = await _sut.SetPermissionsAsync(Editors.Id, new SetPolicyPermissionsDto([]));

        result.Data!.Permissions.Should().BeEmpty();
        _versions.Current(TenantId).Should().Be(1);
        await _eventBus.Received(1).PublishAsync(Arg.Any<EntityDeletedEvent<PolicyPermissionDto>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPermissionsAsync_Changed_BumpsAndPublishes()
    {
        GivenReplaceAdds(DocsWrite);

        await _sut.SetPermissionsAsync(Editors.Id, new SetPolicyPermissionsDto(["docs.read", "docs.write"]));

        _versions.Current(TenantId).Should().Be(1);
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<EntityCreatedEvent<PolicyPermissionDto>>(e => e.Entity.PermissionId == DocsWrite.Id && e.TenantId == TenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPermissionsAsync_Unchanged_NoBumpOrEvent()
    {
        _policies.ReplacePermissionsAsync(Editors.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PolicyPermissionChangesDto>.Ok(new PolicyPermissionChangesDto([], [])));

        (await _sut.SetPermissionsAsync(Editors.Id, new SetPolicyPermissionsDto(["docs.read"]))).Success.Should().BeTrue();

        _versions.Current(TenantId).Should().Be(0);
        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync<EntityCreatedEvent<PolicyPermissionDto>>(default!, default);
    }

    [Fact]
    public async Task SetPermissionsAsync_ConcurrentChange_ConflictPassedThrough()
    {
        _policies.ReplacePermissionsAsync(Editors.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PolicyPermissionChangesDto>.Fail("Try again.", 409, CoreErrorCodes.Conflict));

        var result = await _sut.SetPermissionsAsync(Editors.Id, new SetPolicyPermissionsDto(["docs.read", "docs.write"]));

        result.StatusCode.Should().Be(409);
        _versions.Current(TenantId).Should().Be(0);
    }

    [Fact]
    public async Task SetPermissionsAsync_SystemOnlyKey_BadRequest()
    {
        var result = await _sut.SetPermissionsAsync(Editors.Id, new SetPolicyPermissionsDto([PermissionKeysAuth.RolesAssignSystem]));

        result.StatusCode.Should().Be(400);
        await _policies.DidNotReceiveWithAnyArgs().ReplacePermissionsAsync(default, default!, default);
    }

    [Fact]
    public async Task SetPermissionsAsync_TenantAdminCaller_AddsAnything()
    {
        _permissions.GetPermissionSummaryAsync(CallerId, Arg.Any<CancellationToken>()).Returns(new PermissionSummaryDto(true, []));
        GivenReplaceAdds(SettingsRead);

        (await _sut.SetPermissionsAsync(Editors.Id, new SetPolicyPermissionsDto(["docs.read", "settings.read"]))).Success.Should().BeTrue();
    }

    // ── Delete ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_UsedByRoles_PolicyInUseNamingThem()
    {
        GivenPolicy(Editors with { Roles = [new EntityRefDto(Guid.NewGuid(), "Editor"), new EntityRefDto(Guid.NewGuid(), "Writer")] });

        var result = await _sut.DeleteAsync(Editors.Id);

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(AuthErrorCodes.PolicyInUse);
        result.Errors.Should().Equal("Used by role 'Editor'.", "Used by role 'Writer'.");
        await _policies.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Fact]
    public async Task DeleteAsync_Unused_DeletesBumpsAndPublishes()
    {
        _policies.DeleteAsync(Editors.Id, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());

        (await _sut.DeleteAsync(Editors.Id)).Success.Should().BeTrue();

        _versions.Current(TenantId).Should().Be(1);
        await _eventBus.Received(1).PublishAsync(Arg.Is<EntityDeletedEvent<PolicyDto>>(e => e.EntityId == Editors.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_RoleAttachedMeanwhile_PolicyInUse()
    {
        _policies.DeleteAsync(Editors.Id, Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("in use", 409, CoreErrorCodes.Conflict));

        var result = await _sut.DeleteAsync(Editors.Id);

        result.ErrorCode.Should().Be(AuthErrorCodes.PolicyInUse);
        _versions.Current(TenantId).Should().Be(0);
    }

    [Fact]
    public async Task NoEventBus_ChangesStillSucceedAndBump()
    {
        var sut = CreateSut(withEventBus: false);
        GivenReplaceAdds(DocsWrite);

        (await sut.SetPermissionsAsync(Editors.Id, new SetPolicyPermissionsDto(["docs.read", "docs.write"]))).Success.Should().BeTrue();

        _versions.Current(TenantId).Should().Be(1);
    }

    // ── Permissions on the interface ──────────────────────────────

    [Theory]
    [InlineData(nameof(IPolicyService.ListCatalogAsync), PermissionKeysAuth.PermissionsRead)]
    [InlineData(nameof(IPolicyService.GetModulesAsync), PermissionKeysAuth.PermissionsRead)]
    [InlineData(nameof(IPolicyService.ListAsync), PermissionKeysAuth.PermissionsRead)]
    [InlineData(nameof(IPolicyService.GetAsync), PermissionKeysAuth.PermissionsRead)]
    [InlineData(nameof(IPolicyService.CreateAsync), PermissionKeysAuth.PermissionsWrite)]
    [InlineData(nameof(IPolicyService.UpdateAsync), PermissionKeysAuth.PermissionsWrite)]
    [InlineData(nameof(IPolicyService.SetPermissionsAsync), PermissionKeysAuth.PermissionsWrite)]
    [InlineData(nameof(IPolicyService.DeleteAsync), PermissionKeysAuth.PermissionsWrite)]
    public void Interface_MethodRequiresPermission(string method, string permission)
    {
        var attribute = typeof(IPolicyService).GetMethod(method)!.GetCustomAttribute<RequiresPermissionAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Permissions.Should().Equal(permission);
    }

    [Fact]
    public void Interface_EveryMethodHasAPermission()
        => typeof(IPolicyService).GetMethods()
            .Should().OnlyContain(m => m.GetCustomAttribute<RequiresPermissionAttribute>() != null);

    // ── Helpers ───────────────────────────────────────────────────

    private PolicyService CreateSut(bool withEventBus)
        => new(
            _currentUser, _tenantContext, _policies, _permissionCatalogue,
            new PermissionHoldGuard(_permissions), _versions, _unitOfWork,
            new CreatePolicyDtoValidator(), new UpdatePolicyDtoValidator(), new SetPolicyPermissionsDtoValidator(),
            NullLogger<PolicyService>.Instance, withEventBus ? _eventBus : null);

    private async Task<(int Status, string? Code)> CallAsync(string method)
    {
        switch (method)
        {
            case "Catalog": { var r = await _sut.ListCatalogAsync(new FilterParams()); return (r.StatusCode, r.ErrorCode); }
            case "List": { var r = await _sut.ListAsync(new FilterParams()); return (r.StatusCode, r.ErrorCode); }
            case "Get": { var r = await _sut.GetAsync(Editors.Id); return (r.StatusCode, r.ErrorCode); }
            case "Create": { var r = await _sut.CreateAsync(new CreatePolicyDto("New", null)); return (r.StatusCode, r.ErrorCode); }
            case "Update": { var r = await _sut.UpdateAsync(Editors.Id, new UpdatePolicyDto("Renamed", null)); return (r.StatusCode, r.ErrorCode); }
            case "SetPermissions": { var r = await _sut.SetPermissionsAsync(Editors.Id, new SetPolicyPermissionsDto([])); return (r.StatusCode, r.ErrorCode); }
            case "Delete": { var r = await _sut.DeleteAsync(Editors.Id); return (r.StatusCode, r.ErrorCode); }
            default: throw new ArgumentOutOfRangeException(nameof(method), method, null);
        }
    }

    private static async Task<OperationResult> RunAsync(Func<CancellationToken, Task> operation)
    {
        await operation(CancellationToken.None);
        return OperationResult.Ok();
    }

    private PolicyDto GivenAddReturns(string name)
    {
        var created = new PolicyDto(Guid.NewGuid(), name, null, TenantId);
        _policies.AddAsync(Arg.Any<PolicyDto>(), Arg.Any<CancellationToken>()).Returns(OperationResult<PolicyDto>.Ok(created, "Created", 201));
        GivenPolicy(new PolicyDetailDto(created.Id, name, null, [], []));
        return created;
    }

    private void GivenPolicy(PolicyDetailDto policy)
        => _policies.GetDetailAsync(policy.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<PolicyDetailDto>.Ok(policy));

    private void GivenReplaceAdds(params PermissionDto[] added)
        => _policies.ReplacePermissionsAsync(Editors.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PolicyPermissionChangesDto>.Ok(new PolicyPermissionChangesDto(
                added.Select(p => new PolicyPermissionDto(Guid.NewGuid(), Editors.Id, p.Id)).ToList(), [])));

    private void GivenCallerHolds(params string[] keys)
        => _permissions.GetPermissionSummaryAsync(CallerId, Arg.Any<CancellationToken>()).Returns(new PermissionSummaryDto(false, keys.ToList()));
}
