using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers.Settings;

/// <summary>
/// Admin CRUD controller for setting levels. Demonstrates the settings admin
/// service pattern — consuming applications copy and customize this controller
/// with their own authorization and routing requirements.
/// </summary>
[ApiController]
[Route("api/settings/levels")]
public sealed class SettingLevelsController : ControllerBase
{
    private readonly ISettingsAdminService _adminService;

    /// <summary>
    /// Initializes a new instance of <see cref="SettingLevelsController"/>.
    /// </summary>
    /// <param name="adminService">The settings admin service.</param>
    public SettingLevelsController(ISettingsAdminService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>
    /// Gets all setting levels ordered by display order.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of all setting levels.</returns>
    [HttpGet]
    public async Task<ActionResult<OperationResult<IReadOnlyList<SettingLevelDto>>>> GetAll(
        CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetAllLevelsAsync(cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Creates a new setting level.
    /// </summary>
    /// <param name="dto">The level creation data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created setting level.</returns>
    [HttpPost]
    public async Task<ActionResult<OperationResult<SettingLevelDto>>> Create(
        [FromBody] CreateSettingLevelDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _adminService.CreateLevelAsync(dto, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Updates an existing setting level.
    /// </summary>
    /// <param name="id">The unique identifier of the level to update.</param>
    /// <param name="dto">The level update data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated setting level.</returns>
    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult<SettingLevelDto>>> Update(
        Guid id, [FromBody] UpdateSettingLevelDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _adminService.UpdateLevelAsync(id, dto, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Deletes a setting level.
    /// </summary>
    /// <param name="id">The unique identifier of the level to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    [HttpDelete("{id}")]
    public async Task<ActionResult<OperationResult>> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _adminService.DeleteLevelAsync(id, cancellationToken);
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
