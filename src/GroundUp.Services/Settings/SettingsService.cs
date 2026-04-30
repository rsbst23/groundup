using System.Globalization;
using System.Text.RegularExpressions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Postgres;
using GroundUp.Events;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Services.Settings;

/// <summary>
/// Sealed, scoped implementation of <see cref="ISettingsService"/> that performs
/// cascade resolution, type-safe deserialization, transparent encryption/decryption,
/// validation, and domain event publishing. Uses EF Core directly via
/// <c>DbContext.Set&lt;T&gt;()</c> for optimized query patterns that don't fit
/// the generic BaseRepository model.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly GroundUpDbContext _dbContext;
    private readonly IEventBus _eventBus;
    private readonly ISettingEncryptionProvider? _encryptionProvider;

    private const string SecretMask = "••••••••";

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsService"/>.
    /// </summary>
    /// <param name="dbContext">The EF Core database context for querying settings entities.</param>
    /// <param name="eventBus">The event bus for publishing <see cref="SettingChangedEvent"/>.</param>
    /// <param name="encryptionProvider">
    /// Optional encryption provider. When null, the service works for non-encrypted settings
    /// and fails with a clear error only when an encrypted setting is actually accessed.
    /// </param>
    public SettingsService(
        GroundUpDbContext dbContext,
        IEventBus eventBus,
        ISettingEncryptionProvider? encryptionProvider = null)
    {
        _dbContext = dbContext;
        _eventBus = eventBus;
        _encryptionProvider = encryptionProvider;
    }

    /// <inheritdoc />
    public async Task<OperationResult<T>> GetAsync<T>(
        string key,
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken = default)
    {
        var definition = await _dbContext.Set<SettingDefinition>()
            .AsNoTracking()
            .Include(d => d.AllowedLevels)
            .FirstOrDefaultAsync(d => d.Key == key, cancellationToken);

        if (definition is null)
        {
            return OperationResult<T>.NotFound($"Setting '{key}' not found");
        }

        var allowedLevelIds = definition.AllowedLevels
            .Select(al => al.SettingLevelId)
            .ToHashSet();

        string? effectiveValue = null;

        if (scopeChain is { Count: > 0 })
        {
            foreach (var entry in scopeChain)
            {
                if (!allowedLevelIds.Contains(entry.LevelId))
                {
                    continue;
                }

                var settingValue = await _dbContext.Set<SettingValue>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v =>
                        v.SettingDefinitionId == definition.Id &&
                        v.LevelId == entry.LevelId &&
                        v.ScopeId == entry.ScopeId,
                        cancellationToken);

                if (settingValue is not null)
                {
                    effectiveValue = settingValue.Value;
                    break;
                }
            }
        }

        effectiveValue ??= definition.DefaultValue;

        if (definition.IsEncrypted && effectiveValue is not null)
        {
            if (_encryptionProvider is null)
            {
                return OperationResult<T>.Fail(
                    $"Encryption provider required to read encrypted setting '{key}'", 500);
            }

            effectiveValue = _encryptionProvider.Decrypt(effectiveValue);
        }

        return SettingValueConverter.Convert<T>(effectiveValue, definition.DataType, definition.AllowMultiple, key);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingValueDto>> SetAsync(
        string key,
        string value,
        Guid levelId,
        Guid? scopeId,
        CancellationToken cancellationToken = default)
    {
        var definition = await _dbContext.Set<SettingDefinition>()
            .Include(d => d.AllowedLevels)
            .FirstOrDefaultAsync(d => d.Key == key, cancellationToken);

        if (definition is null)
        {
            return OperationResult<SettingValueDto>.NotFound($"Setting '{key}' not found");
        }

        // Validation: IsReadOnly
        if (definition.IsReadOnly)
        {
            return OperationResult<SettingValueDto>.BadRequest($"Setting '{key}' is read-only");
        }

        // Validation: AllowedLevels
        var allowedLevelIds = definition.AllowedLevels
            .Select(al => al.SettingLevelId)
            .ToHashSet();

        if (!allowedLevelIds.Contains(levelId))
        {
            return OperationResult<SettingValueDto>.BadRequest(
                $"Level '{levelId}' is not allowed for setting '{key}'");
        }

        // Validation: IsRequired
        if (definition.IsRequired && string.IsNullOrEmpty(value))
        {
            return OperationResult<SettingValueDto>.BadRequest(
                $"Setting '{key}' requires a value");
        }

        // Validation: MinValue / MaxValue (numeric types, only when value is not empty)
        var numericValidation = ValidateNumericRange(definition, value);
        if (numericValidation is not null)
        {
            return numericValidation;
        }

        // Validation: MinLength / MaxLength (only when value is not empty)
        if (!string.IsNullOrEmpty(value))
        {
            if (definition.MinLength.HasValue && value.Length < definition.MinLength.Value)
            {
                return OperationResult<SettingValueDto>.BadRequest(
                    $"Value must be at least {definition.MinLength.Value} characters for setting '{key}'");
            }

            if (definition.MaxLength.HasValue && value.Length > definition.MaxLength.Value)
            {
                return OperationResult<SettingValueDto>.BadRequest(
                    $"Value must be at most {definition.MaxLength.Value} characters for setting '{key}'");
            }
        }

        // Validation: RegexPattern (only when value is not empty)
        if (!string.IsNullOrEmpty(definition.RegexPattern) && !string.IsNullOrEmpty(value))
        {
            if (!Regex.IsMatch(value, definition.RegexPattern))
            {
                var message = definition.ValidationMessage
                    ?? $"Value does not match the required pattern for setting '{key}'";
                return OperationResult<SettingValueDto>.BadRequest(message);
            }
        }

        // Encryption
        var valueToStore = value;
        if (definition.IsEncrypted)
        {
            if (_encryptionProvider is null)
            {
                return OperationResult<SettingValueDto>.Fail(
                    $"Encryption provider required to write encrypted setting '{key}'", 500);
            }

            valueToStore = _encryptionProvider.Encrypt(value);
        }

        // Upsert: find existing or create new
        var existing = await _dbContext.Set<SettingValue>()
            .FirstOrDefaultAsync(v =>
                v.SettingDefinitionId == definition.Id &&
                v.LevelId == levelId &&
                v.ScopeId == scopeId,
                cancellationToken);

        string? oldValue = null;

        if (existing is not null)
        {
            oldValue = existing.Value;
            existing.Value = valueToStore;
        }
        else
        {
            var newEntity = new SettingValue
            {
                SettingDefinitionId = definition.Id,
                LevelId = levelId,
                ScopeId = scopeId,
                Value = valueToStore
            };
            _dbContext.Set<SettingValue>().Add(newEntity);
            existing = newEntity;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Publish event (fire-and-forget)
        try
        {
            await _eventBus.PublishAsync(new SettingChangedEvent
            {
                SettingKey = key,
                LevelId = levelId,
                ScopeId = scopeId,
                OldValue = oldValue,
                NewValue = value
            }, cancellationToken);
        }
        catch
        {
            // Fire-and-forget: event publishing failures do not affect the operation result
        }

        var dto = new SettingValueDto(
            existing.Id,
            existing.SettingDefinitionId,
            existing.LevelId,
            existing.ScopeId,
            existing.Value);

        return OperationResult<SettingValueDto>.Ok(dto);
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteValueAsync(
        Guid settingValueId,
        CancellationToken cancellationToken = default)
    {
        var settingValue = await _dbContext.Set<SettingValue>()
            .Include(v => v.SettingDefinition)
            .FirstOrDefaultAsync(v => v.Id == settingValueId, cancellationToken);

        if (settingValue is null)
        {
            return OperationResult.NotFound($"Setting value '{settingValueId}' not found");
        }

        var settingKey = settingValue.SettingDefinition.Key;
        var levelId = settingValue.LevelId;
        var scopeId = settingValue.ScopeId;
        var oldValue = settingValue.Value;

        _dbContext.Set<SettingValue>().Remove(settingValue);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Publish event (fire-and-forget)
        try
        {
            await _eventBus.PublishAsync(new SettingChangedEvent
            {
                SettingKey = settingKey,
                LevelId = levelId,
                ScopeId = scopeId,
                OldValue = oldValue,
                NewValue = null
            }, cancellationToken);
        }
        catch
        {
            // Fire-and-forget: event publishing failures do not affect the operation result
        }

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> GetAllForScopeAsync(
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken = default)
    {
        var definitions = await _dbContext.Set<SettingDefinition>()
            .AsNoTracking()
            .Include(d => d.AllowedLevels)
            .ToListAsync(cancellationToken);

        var definitionIds = definitions.Select(d => d.Id).ToList();

        var allValues = await _dbContext.Set<SettingValue>()
            .AsNoTracking()
            .Where(v => definitionIds.Contains(v.SettingDefinitionId))
            .ToListAsync(cancellationToken);

        var results = new List<ResolvedSettingDto>(definitions.Count);

        foreach (var definition in definitions)
        {
            var resolved = ResolveEffectiveValue(definition, allValues, scopeChain);
            var effectiveValue = resolved.Value;

            // Decrypt if encrypted and provider available
            if (definition.IsEncrypted && effectiveValue is not null && _encryptionProvider is not null)
            {
                effectiveValue = _encryptionProvider.Decrypt(effectiveValue);
            }

            // Mask secret values
            if (definition.IsSecret && effectiveValue is not null)
            {
                effectiveValue = SecretMask;
            }

            results.Add(new ResolvedSettingDto(
                MapToDto(definition),
                effectiveValue,
                resolved.SourceLevelId,
                resolved.SourceScopeId,
                resolved.IsInherited));
        }

        var ordered = results.OrderBy(r => r.Definition.DisplayOrder).ToList();
        return OperationResult<IReadOnlyList<ResolvedSettingDto>>.Ok(ordered);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> GetGroupAsync(
        string groupKey,
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.Set<SettingGroup>()
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Key == groupKey, cancellationToken);

        if (group is null)
        {
            return OperationResult<IReadOnlyList<ResolvedSettingDto>>.NotFound(
                $"Setting group '{groupKey}' not found");
        }

        var definitions = await _dbContext.Set<SettingDefinition>()
            .AsNoTracking()
            .Include(d => d.AllowedLevels)
            .Where(d => d.GroupId == group.Id)
            .ToListAsync(cancellationToken);

        var definitionIds = definitions.Select(d => d.Id).ToList();

        var allValues = await _dbContext.Set<SettingValue>()
            .AsNoTracking()
            .Where(v => definitionIds.Contains(v.SettingDefinitionId))
            .ToListAsync(cancellationToken);

        var results = new List<ResolvedSettingDto>(definitions.Count);

        foreach (var definition in definitions)
        {
            var resolved = ResolveEffectiveValue(definition, allValues, scopeChain);
            var effectiveValue = resolved.Value;

            // Decrypt if encrypted and provider available
            if (definition.IsEncrypted && effectiveValue is not null && _encryptionProvider is not null)
            {
                effectiveValue = _encryptionProvider.Decrypt(effectiveValue);
            }

            // Mask secret values
            if (definition.IsSecret && effectiveValue is not null)
            {
                effectiveValue = SecretMask;
            }

            results.Add(new ResolvedSettingDto(
                MapToDto(definition),
                effectiveValue,
                resolved.SourceLevelId,
                resolved.SourceScopeId,
                resolved.IsInherited));
        }

        var ordered = results.OrderBy(r => r.Definition.DisplayOrder).ToList();
        return OperationResult<IReadOnlyList<ResolvedSettingDto>>.Ok(ordered);
    }

    #region Private Helpers

    /// <summary>
    /// Resolves the effective value for a definition by walking the scope chain.
    /// </summary>
    private static ResolvedValue ResolveEffectiveValue(
        SettingDefinition definition,
        List<SettingValue> allValues,
        IReadOnlyList<SettingScopeEntry> scopeChain)
    {
        var allowedLevelIds = definition.AllowedLevels
            .Select(al => al.SettingLevelId)
            .ToHashSet();

        if (scopeChain is { Count: > 0 })
        {
            for (var i = 0; i < scopeChain.Count; i++)
            {
                var entry = scopeChain[i];

                if (!allowedLevelIds.Contains(entry.LevelId))
                {
                    continue;
                }

                var match = allValues.FirstOrDefault(v =>
                    v.SettingDefinitionId == definition.Id &&
                    v.LevelId == entry.LevelId &&
                    v.ScopeId == entry.ScopeId);

                if (match is not null)
                {
                    return new ResolvedValue(
                        match.Value,
                        match.LevelId,
                        match.ScopeId,
                        IsInherited: i > 0);
                }
            }
        }

        // Fall back to default
        return new ResolvedValue(
            definition.DefaultValue,
            SourceLevelId: null,
            SourceScopeId: null,
            IsInherited: true);
    }

    /// <summary>
    /// Validates numeric range constraints (MinValue/MaxValue) for numeric data types.
    /// Returns null if validation passes, or a BadRequest result if it fails.
    /// </summary>
    private static OperationResult<SettingValueDto>? ValidateNumericRange(
        SettingDefinition definition,
        string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (definition.MinValue is null && definition.MaxValue is null)
        {
            return null;
        }

        if (definition.DataType is not (SettingDataType.Int or SettingDataType.Long or SettingDataType.Decimal))
        {
            return null;
        }

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var numericValue))
        {
            return null; // Let type conversion handle the parse failure later
        }

        if (definition.MinValue is not null)
        {
            if (decimal.TryParse(definition.MinValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var min))
            {
                if (numericValue < min)
                {
                    return OperationResult<SettingValueDto>.BadRequest(
                        $"Value must be at least {definition.MinValue} for setting '{definition.Key}'");
                }
            }
        }

        if (definition.MaxValue is not null)
        {
            if (decimal.TryParse(definition.MaxValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var max))
            {
                if (numericValue > max)
                {
                    return OperationResult<SettingValueDto>.BadRequest(
                        $"Value must be at most {definition.MaxValue} for setting '{definition.Key}'");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a <see cref="SettingDefinition"/> entity to a <see cref="SettingDefinitionDto"/>.
    /// </summary>
    private static SettingDefinitionDto MapToDto(SettingDefinition def) => new(
        def.Id,
        def.Key,
        def.DataType,
        def.DefaultValue,
        def.GroupId,
        def.DisplayName,
        def.Description,
        def.Category,
        def.DisplayOrder,
        def.IsVisible,
        def.IsReadOnly,
        def.AllowMultiple,
        def.IsEncrypted,
        def.IsSecret,
        def.IsRequired,
        def.MinValue,
        def.MaxValue,
        def.MinLength,
        def.MaxLength,
        def.RegexPattern,
        def.ValidationMessage,
        def.DependsOnKey,
        def.DependsOnOperator,
        def.DependsOnValue,
        def.CustomValidatorType);

    /// <summary>
    /// Internal record for holding resolved value data during bulk resolution.
    /// </summary>
    private sealed record ResolvedValue(
        string? Value,
        Guid? SourceLevelId,
        Guid? SourceScopeId,
        bool IsInherited);

    #endregion
}
