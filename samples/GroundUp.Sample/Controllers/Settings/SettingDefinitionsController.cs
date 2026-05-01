using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers.Settings;

/// <summary>
/// Admin CRUD controller for setting definitions. Demonstrates the settings admin
/// service pattern — consuming applications copy and customize this controller
/// with their own authorization and routing requirements.
/// </summary>
[ApiController]
[Route("api/settings/definitions")]
public sealed class SettingDefinitionsController : ControllerBase
{
    private readonly ISettingsAdminService _adminService;

    /// <summary>
    /// Initializes a new instance of <see cref="SettingDefinitionsController"/>.
    /// </summary>
    /// <param name="adminService">The settings admin service.</param>
    public SettingDefinitionsController(ISettingsAdminService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>
    /// Gets all setting definitions with their associated options.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of all setting definitions.</returns>
    [HttpGet]
    public async Task<ActionResult<OperationResult<IReadOnlyList<SettingDefinitionDto>>>> GetAll(
        CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetAllDefinitionsAsync(cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Gets a single setting definition by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The setting definition.</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<OperationResult<SettingDefinitionDto>>> GetById(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetDefinitionByIdAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Creates a new setting definition with its associated options and allowed levels.
    /// </summary>
    /// <param name="dto">The definition creation data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created setting definition.</returns>
    [HttpPost]
    public async Task<ActionResult<OperationResult<SettingDefinitionDto>>> Create(
        [FromBody] CreateSettingDefinitionDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _adminService.CreateDefinitionAsync(dto, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Updates an existing setting definition, replacing its options and allowed levels.
    /// </summary>
    /// <param name="id">The unique identifier of the definition to update.</param>
    /// <param name="dto">The definition update data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated setting definition.</returns>
    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult<SettingDefinitionDto>>> Update(
        Guid id, [FromBody] UpdateSettingDefinitionDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _adminService.UpdateDefinitionAsync(id, dto, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Deletes a setting definition. Cascades to associated options, values,
    /// and allowed level records.
    /// </summary>
    /// <param name="id">The unique identifier of the definition to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    [HttpDelete("{id}")]
    public async Task<ActionResult<OperationResult>> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _adminService.DeleteDefinitionAsync(id, cancellationToken);
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
