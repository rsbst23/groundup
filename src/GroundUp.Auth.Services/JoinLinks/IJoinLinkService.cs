using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Attributes;
using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.JoinLinks;

/// <summary>
/// Join links for the current tenant, for administrators (GU-68 Requirement 9). Registered by
/// <c>AddGroundUpAuth</c> through the authorization proxy; every method needs
/// <c>auth.joinlinks.manage</c>, and creating or regenerating also needs the right to grant the
/// link's role. The token is returned only when a link is created or regenerated.
/// </summary>
public interface IJoinLinkService
{
    /// <summary>Creates a join link that grants a role in the current tenant.</summary>
    /// <returns>
    /// 201 with the link and its token; 400 for invalid input; 403 when the caller may not grant the
    /// role; 404 for an unknown role; 422 <c>ENTERPRISE_NOT_SUPPORTED</c>.
    /// </returns>
    [RequiresPermission(PermissionKeysAuth.JoinLinksManage)]
    Task<OperationResult<CreatedJoinLinkDto>> CreateAsync(CreateJoinLinkDto request, CancellationToken cancellationToken = default);

    /// <summary>Lists the tenant's join links, newest first.</summary>
    [RequiresPermission(PermissionKeysAuth.JoinLinksManage)]
    Task<OperationResult<PaginatedData<JoinLinkDto>>> ListAsync(PaginationParams paging, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the link's token, so the old link stops working. The caller becomes the link's creator
    /// for the re-check on each use.
    /// </summary>
    [RequiresPermission(PermissionKeysAuth.JoinLinksManage)]
    Task<OperationResult<CreatedJoinLinkDto>> RegenerateAsync(Guid joinLinkId, CancellationToken cancellationToken = default);

    /// <summary>Revokes the link. People who already joined keep their membership.</summary>
    [RequiresPermission(PermissionKeysAuth.JoinLinksManage)]
    Task<OperationResult> RevokeAsync(Guid joinLinkId, CancellationToken cancellationToken = default);
}
