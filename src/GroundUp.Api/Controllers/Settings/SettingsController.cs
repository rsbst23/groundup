using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Api.Controllers.Settings;

/// <summary>
/// Consumer-facing controller for resolving effective settings and setting overrides.
/// Uses <see cref="ISettingsService"/> convenience overloads that resolve the scope chain
/// from <see cref="IScopeChainProvider"/> automatically.
/// <para>
/// This controller does NOT extend <see cref="BaseController{TDto}"/> because settings
/// endpoints don't follow the standard CRUD pattern — routes are custom, DTOs vary per
/// endpoint, and there is no single entity type.
/// </para>
/// </summary>
[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsController"/>.
    /// </summary>
    /// <param name="settingsService">The settings resolution service.</param>
    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Resolves the effective value for a single setting using the current scope chain.
    /// </summary>
    /// <param name="key">The setting definition key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The effective value as a string.</returns>
    [HttpGet("{key}")]
    public async Task<ActionResult<OperationResult<string>>> GetByKey(
        string key, CancellationToken cancellationToken = default)
    {
        var result = await _settingsService.GetAsync<string>(key, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Resolves all settings with their effective values for the current scope chain.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of resolved settings.</returns>
    [HttpGet]
    public async Task<ActionResult<OperationResult<IReadOnlyList<ResolvedSettingDto>>>> GetAll(
        CancellationToken cancellationToken = default)
    {
        var result = await _settingsService.GetAllForScopeAsync(cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Resolves all settings in a specific group with their effective values
    /// for the current scope chain.
    /// </summary>
    /// <param name="groupKey">The setting group key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of resolved settings for the group.</returns>
    [HttpGet("groups/{groupKey}")]
    public async Task<ActionResult<OperationResult<IReadOnlyList<ResolvedSettingDto>>>> GetGroup(
        string groupKey, CancellationToken cancellationToken = default)
    {
        var result = await _settingsService.GetGroupAsync(groupKey, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Sets a value at a specific level and scope for the given setting key.
    /// </summary>
    /// <param name="key">The setting definition key.</param>
    /// <param name="dto">The value, level, and scope to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created or updated setting value.</returns>
    [HttpPut("{key}")]
    public async Task<ActionResult<OperationResult<SettingValueDto>>> SetValue(
        string key, [FromBody] SetSettingValueDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _settingsService.SetAsync(key, dto.Value, dto.LevelId, dto.ScopeId, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Deletes a setting value override, causing the setting to revert to the
    /// next value in the cascade chain or the definition default.
    /// </summary>
    /// <param name="id">The unique identifier of the setting value to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    [HttpDelete("values/{id}")]
    public async Task<ActionResult<OperationResult>> DeleteValue(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _settingsService.DeleteValueAsync(id, cancellationToken);
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
