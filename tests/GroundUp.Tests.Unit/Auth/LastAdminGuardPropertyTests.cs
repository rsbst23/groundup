using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Property-based tests for <see cref="LastAdminGuard"/>.
/// Feature: phase-10c-auth-dispatcher, Property 17: Last Admin Guard
/// **Validates: Requirements 10.8**
/// </summary>
[Trait("Category", "Property")]
public sealed class LastAdminGuardPropertyTests
{
    /// <summary>
    /// Creates a <see cref="LastAdminGuard"/> with a mocked repository that returns
    /// the specified number of TenantAdmin holders for any tenant query.
    /// </summary>
    private static LastAdminGuard CreateSut(int adminCount, Guid tenantId)
    {
        var userRoleRepository = Substitute.For<IUserRoleRepository>();

        var adminHolders = Enumerable.Range(0, adminCount)
            .Select(_ => new UserRoleDto(
                Id: Guid.NewGuid(),
                UserId: Guid.NewGuid(),
                RoleId: Guid.NewGuid(),
                TenantId: tenantId,
                RoleName: AuthRoleNames.TenantAdmin))
            .ToList();

        userRoleRepository.GetTenantAdminHoldersAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(adminHolders));

        return new LastAdminGuard(userRoleRepository);
    }

    // --- Property 17: Last Admin Guard ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 17: Last Admin Guard
    /// For any tenant with exactly one active TenantAdmin, attempting to remove that
    /// user's TenantAdmin assignment SHALL be rejected with error code "LAST_ADMIN" and HTTP 409.
    /// **Validates: Requirements 10.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LastAdminGuardArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 17: Single admin removal is rejected")]
    public Property SingleAdmin_RemovalRejected(TenantAdminRemovalScenario scenario)
    {
        // Only test the single-admin case
        if (scenario.ActiveAdminCount != 1)
            return true.ToProperty();

        var sut = CreateSut(scenario.ActiveAdminCount, scenario.TenantId);

        // Act
        var result = sut.CanRemoveTenantAdminAsync(scenario.UserIdToRemove, scenario.TenantId)
            .GetAwaiter().GetResult();

        // Assert — removal must be rejected with LAST_ADMIN error
        return (!result.Success
            && result.ErrorCode == "LAST_ADMIN"
            && result.StatusCode == 409)
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 17: Last Admin Guard
    /// For any tenant with two or more active TenantAdmins, removing one TenantAdmin
    /// assignment SHALL succeed (the guard allows it).
    /// **Validates: Requirements 10.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LastAdminGuardArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 17: Multiple admins removal succeeds")]
    public Property MultipleAdmins_RemovalSucceeds(TenantAdminRemovalScenario scenario)
    {
        // Only test multi-admin cases (2+)
        if (scenario.ActiveAdminCount < 2)
            return true.ToProperty();

        var sut = CreateSut(scenario.ActiveAdminCount, scenario.TenantId);

        // Act
        var result = sut.CanRemoveTenantAdminAsync(scenario.UserIdToRemove, scenario.TenantId)
            .GetAwaiter().GetResult();

        // Assert — removal must be permitted
        return (result.Success
            && result.Data == true
            && result.StatusCode == 200)
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 17: Last Admin Guard
    /// For any tenant with N TenantAdmin holders (N >= 1), removal is rejected if and only if N == 1.
    /// This is the core biconditional property: rejected ⟺ exactly one admin.
    /// **Validates: Requirements 10.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LastAdminGuardArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 17: Removal rejected iff exactly one admin")]
    public Property Removal_RejectedIffExactlyOneAdmin(TenantAdminRemovalScenario scenario)
    {
        var sut = CreateSut(scenario.ActiveAdminCount, scenario.TenantId);

        // Act
        var result = sut.CanRemoveTenantAdminAsync(scenario.UserIdToRemove, scenario.TenantId)
            .GetAwaiter().GetResult();

        // Assert — the biconditional: rejected ⟺ N == 1
        var isExactlyOne = scenario.ActiveAdminCount == 1;
        var isRejected = !result.Success && result.ErrorCode == "LAST_ADMIN";
        var isAllowed = result.Success && result.Data == true;

        // If exactly one admin → must be rejected; if 2+ admins → must be allowed
        return ((isExactlyOne && isRejected) || (!isExactlyOne && isAllowed)).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 17: Last Admin Guard
    /// The guard decision is independent of the specific user being removed — only the
    /// count of TenantAdmin holders in the tenant matters.
    /// **Validates: Requirements 10.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LastAdminGuardArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 17: Guard decision depends only on admin count")]
    public Property GuardDecision_IndependentOfUserIdentity(
        TenantAdminRemovalScenario scenario1,
        TenantAdminRemovalScenario scenario2)
    {
        // Use same admin count and tenant but different user IDs
        var tenantId = scenario1.TenantId;
        var adminCount = scenario1.ActiveAdminCount;

        var sut = CreateSut(adminCount, tenantId);

        // Act — same tenant/count, different users
        var result1 = sut.CanRemoveTenantAdminAsync(scenario1.UserIdToRemove, tenantId)
            .GetAwaiter().GetResult();
        var result2 = sut.CanRemoveTenantAdminAsync(scenario2.UserIdToRemove, tenantId)
            .GetAwaiter().GetResult();

        // Assert — both should have the same outcome
        return (result1.Success == result2.Success
            && result1.ErrorCode == result2.ErrorCode)
            .ToProperty();
    }
}

// --- Test data types ---

/// <summary>
/// Represents a scenario for testing the last-admin guard with varying admin counts.
/// </summary>
public sealed record TenantAdminRemovalScenario(
    Guid TenantId,
    Guid UserIdToRemove,
    int ActiveAdminCount)
{
    public override string ToString() =>
        $"Tenant={TenantId.ToString()[..8]}..., User={UserIdToRemove.ToString()[..8]}..., AdminCount={ActiveAdminCount}";
}

/// <summary>
/// Custom FsCheck Arbitrary generators for LastAdminGuard property tests.
/// </summary>
public static class LastAdminGuardArbitraries
{
    /// <summary>
    /// Generates TenantAdminRemovalScenario instances with admin counts ranging from 1 to 10.
    /// The count starts at 1 (minimum — you can't have 0 admins when removing one).
    /// </summary>
    public static Arbitrary<TenantAdminRemovalScenario> TenantAdminRemovalScenarioArb()
    {
        var gen = from tenantId in Gen.Fresh(() => Guid.NewGuid())
                  from userIdToRemove in Gen.Fresh(() => Guid.NewGuid())
                  from adminCount in Gen.Choose(1, 10)
                  select new TenantAdminRemovalScenario(tenantId, userIdToRemove, adminCount);

        return gen.ToArbitrary();
    }
}
