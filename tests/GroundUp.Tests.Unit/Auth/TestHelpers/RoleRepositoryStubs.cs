using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.TestHelpers;

/// <summary>
/// Role repository setups shared by tests that build <c>RoleGrantPolicy</c> directly.
/// </summary>
internal static class RoleRepositoryStubs
{
    /// <summary>
    /// Makes every role grant no permissions (no policies), so the GU-96 escalation guard always
    /// passes. Returns the same substitute, or a new one when none is given.
    /// </summary>
    public static IRoleRepository WithoutGrants(IRoleRepository? roles = null)
    {
        roles ??= Substitute.For<IRoleRepository>();
        roles.GetPermissionGrantsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<RoleGrantDto>>.Ok([]));
        return roles;
    }
}
