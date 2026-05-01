using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers.Settings;

/// <summary>
/// Admin CRUD controller for setting groups. Demonstrates the settings admin
/// service pattern — consuming applications copy and customize this controller
/// with their own authorization and routing requirements.
/// </summary>
[ApiController]
[Route("api/settings/groups")]
public sealed class SettingGroupsController : ControllerBase
{
    private readonly ISettingsAdminService _adminService;

    /// <summary>
    /// Initializes a new instance of <see cref="SettingGroupsController"/>.
    /// </summary>
    /// <param name="adminService">The settings admin service.</param>
    public SettingGroupsController(ISettingsAdminService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>
    /// Gets all setting groups ordered by display order.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of all setting groups.</returns>
    [HttpGet]
    public async Task<ActionResult<OperationResult<IReadOnlyList<SettingGroupDto>>>> GetAll(
        CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetAllGroupsAsync(cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Creates a new setting group.
    /// </summary>
    /// <param name="dto">The group creation data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created setting group.</returns>
    [HttpPost]
    public async Task<ActionResult<OperationResult<SettingGroupDto>>> Create(
        [FromBody] CreateSettingGroupDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _adminService.CreateGroupAsync(dto, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Updates an existing setting group.
    /// </summary>
    /// <param name="id">The unique identifier of the group to update.</param>
    /// <param name="dto">The group update data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated setting group.</returns>
    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult<SettingGroupDto>>> Update(
        Guid id, [FromBody] UpdateSettingGroupDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _adminService.UpdateGroupAsync(id, dto, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Deletes a setting group. Definitions in the group are orphaned rather than deleted.
    /// </summary>
    /// <param name="id">The unique identifier of the group to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    [HttpDelete("{id}")]
    public async Task<ActionResult<OperationResult>> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _adminService.DeleteGroupAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    #region Private Helpers

    private ActionResult ToActionResult<T>(OperationResult<T> result)
    {
        return result.StatusCode switch
        {
            200 => Ok(result),
            201 => StatusCode(201, result),
            400 => BadRequest(result),
            401 => Unauthorized(),
            403 => StatusCode(403, result),
            404 => NotFound(result),
            _ => new ObjectResult(result) { StatusCode = result.StatusCode }
        };
    }

    private ActionResult ToActionResult(OperationResult result)
    {
        return result.StatusCode switch
        {
            200 => Ok(result),
            201 => StatusCode(201, result),
            400 => BadRequest(result),
            401 => Unauthorized(),
            403 => StatusCode(403, result),
            404 => NotFound(result),
            _ => new ObjectResult(result) { StatusCode = result.StatusCode }
        };
    }

    #endregion
}
