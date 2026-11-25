using System.Globalization;
using System.Text.RegularExpressions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

// ReSharper disable ConvertToPrimaryConstructor

namespace GroundUp.Services.Settings;

/// <summary>
/// Sealed, scoped implementation of <see cref="ISettingsService"/> that performs
/// cascade resolution, type-safe deserialization, transparent encryption/decryption,
/// validation, and domain event publishing. Data access goes through
/// <see cref="ISettingsRepository"/>, whose query shapes (lookups by key, scope chain walks,
/// batch value reads) fit the resolution instead of generic CRUD.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly ISettingsRepository _repository;
    private readonly IEventBus _eventBus;
    private readonly ISettingEncryptionProvider? _encryptionProvider;
    private readonly ISecretResolver? _secretResolver;
    private readonly IScopeChainProvider _scopeChainProvider;
    private readonly IMemoryCache _cache;
    private readonly SettingsCacheOptions _cacheOptions;
    private readonly SettingsCacheKeyTracker _cacheKeyTracker;
    private readonly List<ISettingWriteValidator> _writeValidators;
    private readonly ISettingsAuthorizer _authorizer;

    private const string SecretMask = "***REDACTED***";

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsService"/>.
    /// </summary>
    /// <param name="repository">Data access for setting levels, definitions and values.</param>
    /// <param name="eventBus">The event bus for publishing <see cref="SettingChangedEvent"/>.</param>
    /// <param name="scopeChainProvider">The scope chain provider for convenience overloads.</param>
    /// <param name="cache">The in-memory cache for caching resolved settings.</param>
    /// <param name="cacheOptions">Configuration options for cache TTL.</param>
    /// <param name="cacheKeyTracker">Shared tracker for active cache keys.</param>
    /// <param name="authorizer">Decides whether the caller may write or delete a value.</param>
    /// <param name="encryptionProvider">
    /// Optional encryption provider. When null, the service works for non-encrypted settings
    /// and fails with a clear error only when an encrypted setting is actually accessed.
    /// </param>
    /// <param name="secretResolver">
    /// Optional secret resolver. When null, secretref:// values are returned verbatim.
    /// When registered, secretref:// values are resolved via a single-pass call.
    /// </param>
    /// <param name="writeValidators">
    /// Optional cross-value validators run by <c>SetAsync</c> after the built-in checks.
    /// </param>
    public SettingsService(
        ISettingsRepository repository,
        IEventBus eventBus,
        IScopeChainProvider scopeChainProvider,
        IMemoryCache cache,
        IOptions<SettingsCacheOptions> cacheOptions,
        SettingsCacheKeyTracker cacheKeyTracker,
        ISettingsAuthorizer authorizer,
        ISettingEncryptionProvider? encryptionProvider = null,
        ISecretResolver? secretResolver = null,
        IEnumerable<ISettingWriteValidator>? writeValidators = null)
    {
        _writeValidators = writeValidators?.ToList() ?? [];
        _repository = repository;
        _eventBus = eventBus;
        _scopeChainProvider = scopeChainProvider;
        _cache = cache;
        _cacheOptions = cacheOptions.Value;
        _cacheKeyTracker = cacheKeyTracker;
        _authorizer = authorizer;
        _encryptionProvider = encryptionProvider;
        _secretResolver = secretResolver;
    }

    /// <inheritdoc />
    public async Task<OperationResult<T>> GetAsync<T>(
        string key,
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"settings:get:{key}:{ComputeScopeChainHash(scopeChain)}";

        try
        {
            if (_cache.TryGetValue(cacheKey, out OperationResult<T>? cached) && cached is not null)
            {
                return cached;
            }
        }
        catch
        {
            // Cache exceptions are swallowed — fall through to DB resolution
        }

        var result = await ResolveFromDatabaseAsync<T>(key, scopeChain, cancellationToken);

        try
        {
            _cache.Set(cacheKey, result, _cacheOptions.CacheDuration);
            _cacheKeyTracker.Track(cacheKey);
        }
        catch
        {
            // Cache exceptions are swallowed — result is still returned
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingValueDto>> SetAsync(
        string key,
        string value,
        Guid levelId,
        Guid? scopeId,
        CancellationToken cancellationToken = default)
    {
        // Authorization first, so a caller without rights learns nothing about the key or its rules.
        var denied = await AuthorizeAsync<SettingValueDto>(write: true, key, levelId, scopeId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var definition = await _repository.GetDefinitionByKeyAsync(key, cancellationToken);

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
        if (!definition.AllowedLevelIds.Contains(levelId))
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

        // Validation: registered cross-value validators (for example "tenant value <= System value")
        if (_writeValidators.Count > 0)
        {
            var levelName = await _repository.GetLevelNameByIdAsync(levelId, cancellationToken);

            var writeContext = new SettingWriteContext(key, value, levelId, levelName, scopeId);

            foreach (var validator in _writeValidators)
            {
                var error = await validator.ValidateAsync(writeContext, cancellationToken);
                if (error is not null)
                {
                    return OperationResult<SettingValueDto>.BadRequest(error);
                }
            }
        }

        // Encryption: short-circuit null/empty/whitespace (Req 3.2)
        var valueToStore = value;
        if (string.IsNullOrWhiteSpace(value))
        {
            // Persist null without calling Encrypt — the provider's contract
            // throws on null/empty/whitespace, so we short-circuit here.
            valueToStore = null;
        }
        else if (definition.IsEncrypted)
        {
            if (_encryptionProvider is null)
            {
                return OperationResult<SettingValueDto>.Fail(
                    $"Encryption provider required to write encrypted setting '{key}'", 500);
            }

            valueToStore = _encryptionProvider.Encrypt(value);
        }

        // Upsert: insert or update the value at this level and scope
        var change = await _repository.UpsertValueAsync(
            definition.Id, levelId, scopeId, valueToStore, cancellationToken);
        var oldValue = change.OldValue;

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

        return OperationResult<SettingValueDto>.Ok(change.Value);
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteValueAsync(
        Guid settingValueId,
        CancellationToken cancellationToken = default)
    {
        var found = await _repository.FindValueAsync(settingValueId, cancellationToken);

        if (found is null)
        {
            return OperationResult.NotFound($"Setting value '{settingValueId}' not found");
        }

        var settingKey = found.DefinitionKey;
        var levelId = found.Value.LevelId;
        var scopeId = found.Value.ScopeId;

        var deniedDelete = await AuthorizeAsync<object>(write: false, settingKey, levelId, scopeId, cancellationToken);
        if (deniedDelete is not null)
        {
            return deniedDelete.StatusCode == 401
                ? OperationResult.Unauthorized(deniedDelete.Message)
                : OperationResult.Forbidden(deniedDelete.Message);
        }
        var oldValue = found.Value.Value;

        if (!await _repository.DeleteValueByIdAsync(settingValueId, cancellationToken))
        {
            // Removed by someone else between the read and the delete
            return OperationResult.NotFound($"Setting value '{settingValueId}' not found");
        }

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

    /// <summary>
    /// Runs the registered <see cref="ISettingsAuthorizer"/>. Returns <c>null</c> when the operation is allowed,
    /// otherwise a Forbidden or Unauthorized result to return to the caller. Nothing is written or published on denial.
    /// </summary>
    private async Task<OperationResult<T>?> AuthorizeAsync<T>(
        bool write, string key, Guid levelId, Guid? scopeId, CancellationToken cancellationToken)
    {
        var levelName = await _repository.GetLevelNameByIdAsync(levelId, cancellationToken);

        var decision = write
            ? await _authorizer.AuthorizeWriteAsync(key, levelId, levelName, scopeId, cancellationToken)
            : await _authorizer.AuthorizeDeleteAsync(key, levelId, levelName, scopeId, cancellationToken);

        return decision switch
        {
            SettingsAuthorizationResult.Allowed => null,
            SettingsAuthorizationResult.Unauthenticated => OperationResult<T>.Unauthorized(),
            _ => OperationResult<T>.Forbidden()
        };
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> GetAllForScopeAsync(
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"settings:all:{ComputeScopeChainHash(scopeChain)}";

        try
        {
            if (_cache.TryGetValue(cacheKey, out OperationResult<IReadOnlyList<ResolvedSettingDto>>? cached) && cached is not null)
            {
                return cached;
            }
        }
        catch
        {
            // Cache exceptions are swallowed — fall through to DB resolution
        }

        var result = await ResolveAllFromDatabaseAsync(scopeChain, cancellationToken);

        try
        {
            _cache.Set(cacheKey, result, _cacheOptions.CacheDuration);
            _cacheKeyTracker.Track(cacheKey);
        }
        catch
        {
            // Cache exceptions are swallowed — result is still returned
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> GetGroupAsync(
        string groupKey,
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"settings:group:{groupKey}:{ComputeScopeChainHash(scopeChain)}";

        try
        {
            if (_cache.TryGetValue(cacheKey, out OperationResult<IReadOnlyList<ResolvedSettingDto>>? cached) && cached is not null)
            {
                return cached;
            }
        }
        catch
        {
            // Cache exceptions are swallowed — fall through to DB resolution
        }

        var result = await ResolveGroupFromDatabaseAsync(groupKey, scopeChain, cancellationToken);

        try
        {
            _cache.Set(cacheKey, result, _cacheOptions.CacheDuration);
            _cacheKeyTracker.Track(cacheKey);
        }
        catch
        {
            // Cache exceptions are swallowed — result is still returned
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<OperationResult<T>> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
    {
        var scopeChain = await _scopeChainProvider.GetScopeChainAsync(cancellationToken);
        return await GetAsync<T>(key, scopeChain, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> GetAllForScopeAsync(
        CancellationToken cancellationToken = default)
    {
        var scopeChain = await _scopeChainProvider.GetScopeChainAsync(cancellationToken);
        return await GetAllForScopeAsync(scopeChain, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> GetGroupAsync(
        string groupKey,
        CancellationToken cancellationToken = default)
    {
        var scopeChain = await _scopeChainProvider.GetScopeChainAsync(cancellationToken);
        return await GetGroupAsync(groupKey, scopeChain, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<object?>> GetTypedValueAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        // Look up the definition to determine the DataType
        var definition = await _repository.GetDefinitionByKeyAsync(key, cancellationToken);

        if (definition is null)
        {
            return OperationResult<object?>.NotFound($"Setting '{key}' not found");
        }

        // Dispatch to the correct typed GetAsync based on DataType
        return definition.DataType switch
        {
            SettingDataType.String => BoxResult(await GetAsync<string>(key, cancellationToken)),
            SettingDataType.Int => BoxResult(await GetAsync<int>(key, cancellationToken)),
            SettingDataType.Long => BoxResult(await GetAsync<long>(key, cancellationToken)),
            SettingDataType.Decimal => BoxResult(await GetAsync<decimal>(key, cancellationToken)),
            SettingDataType.Bool => BoxResult(await GetAsync<bool>(key, cancellationToken)),
            SettingDataType.DateTime => BoxResult(await GetAsync<DateTime>(key, cancellationToken)),
            SettingDataType.Date => BoxResult(await GetAsync<DateOnly>(key, cancellationToken)),
            SettingDataType.Json => BoxResult(await GetAsync<string>(key, cancellationToken)),
            _ => OperationResult<object?>.Fail($"Unsupported data type '{definition.DataType}' for setting '{key}'", 400)
        };
    }

    /// <summary>
    /// Boxes a typed OperationResult into OperationResult&lt;object?&gt;.
    /// </summary>
    private static OperationResult<object?> BoxResult<T>(OperationResult<T> result)
    {
        if (!result.Success)
        {
            return new OperationResult<object?>
            {
                Success = false,
                StatusCode = result.StatusCode,
                Message = result.Message,
                Errors = result.Errors,
                ErrorCode = result.ErrorCode
            };
        }

        return OperationResult<object?>.Ok(result.Data);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingDefinitionDto>> EnsureDefinitionAsync(
        EnsureSettingDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        // The repository creates group, definition and level links in one transaction and handles
        // a concurrent create of the same key by returning the winner.
        return await _repository.EnsureDefinitionAsync(request, cancellationToken);
    }

    #region Private Helpers

    /// <summary>
    /// Resolves a single setting from the database (bypassing cache).
    /// </summary>
    private async Task<OperationResult<T>> ResolveFromDatabaseAsync<T>(
        string key,
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken)
    {
        var definition = await _repository.GetDefinitionByKeyAsync(key, cancellationToken);

        if (definition is null)
        {
            return OperationResult<T>.NotFound($"Setting '{key}' not found");
        }

        var allowedLevelIds = definition.AllowedLevelIds.ToHashSet();

        string? effectiveValue = null;

        if (scopeChain is { Count: > 0 })
        {
            foreach (var entry in scopeChain)
            {
                if (!allowedLevelIds.Contains(entry.LevelId))
                {
                    continue;
                }

                var settingValue = await _repository.GetValueAsync(definition.Id, entry.LevelId, entry.ScopeId, cancellationToken);

                if (settingValue is not null)
                {
                    effectiveValue = settingValue.Value;
                    break;
                }
            }
        }

        effectiveValue ??= definition.DefaultValue;

        // Req 3.4: short-circuit null/empty/whitespace before calling Decrypt
        if (definition.IsEncrypted && !string.IsNullOrWhiteSpace(effectiveValue))
        {
            if (_encryptionProvider is null)
            {
                return OperationResult<T>.Fail(
                    $"Encryption provider required to read encrypted setting '{key}'", 500);
            }

            effectiveValue = _encryptionProvider.Decrypt(effectiveValue);
        }

        // Req 4.2–4.8: single-pass secret reference resolution on read path
        if (effectiveValue is not null && effectiveValue.StartsWith("secretref://", StringComparison.Ordinal))
        {
            if (_secretResolver is not null)
            {
                var resolved = await _secretResolver.ResolveAsync(effectiveValue, cancellationToken);
                if (resolved is null)
                {
                    return OperationResult<T>.Fail(
                        $"Secret reference could not be resolved for setting '{key}'", 500);
                }

                // Return resolved verbatim — even if it itself begins with "secretref://" (single-pass, Req 4.8)
                effectiveValue = resolved;
            }
            // No resolver registered → literal pass-through (Req 4.3)
        }

        return SettingValueConverter.Convert<T>(effectiveValue, definition.DataType, definition.AllowMultiple, key);
    }

    /// <summary>
    /// Resolves all settings from the database (bypassing cache).
    /// </summary>
    private async Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> ResolveAllFromDatabaseAsync(
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken)
    {
        var definitions = await _repository.GetAllDefinitionsAsync(cancellationToken);

        var definitionIds = definitions.Select(d => d.Id).ToList();

        var allValues = await _repository.GetValuesForDefinitionsAsync(definitionIds, cancellationToken);

        var results = new List<ResolvedSettingDto>(definitions.Count);

        foreach (var definition in definitions)
        {
            var resolved = ResolveEffectiveValue(definition, allValues, scopeChain);
            var effectiveValue = resolved.Value;

            // Req 3.4: short-circuit null/empty/whitespace before calling Decrypt
            if (definition.IsEncrypted && !string.IsNullOrWhiteSpace(effectiveValue))
            {
                if (_encryptionProvider is null)
                {
                    // Skip this setting — cannot decrypt without provider
                    effectiveValue = null;
                }
                else
                {
                    effectiveValue = _encryptionProvider.Decrypt(effectiveValue);
                }
            }

            // Req 4.2–4.8: single-pass secret reference resolution on read path
            if (effectiveValue is not null && effectiveValue.StartsWith("secretref://", StringComparison.Ordinal))
            {
                if (_secretResolver is not null)
                {
                    var resolvedSecret = await _secretResolver.ResolveAsync(effectiveValue, cancellationToken);
                    if (resolvedSecret is not null)
                    {
                        effectiveValue = resolvedSecret;
                    }
                    else
                    {
                        effectiveValue = null;
                    }
                }
                // No resolver registered → literal pass-through (Req 4.3)
            }

            if (definition.IsSecret && effectiveValue is not null)
            {
                effectiveValue = SecretMask;
            }

            results.Add(new ResolvedSettingDto(
                definition,
                effectiveValue,
                resolved.SourceLevelId,
                resolved.SourceScopeId,
                resolved.IsInherited));
        }

        var ordered = results.OrderBy(r => r.Definition.DisplayOrder).ToList();
        return OperationResult<IReadOnlyList<ResolvedSettingDto>>.Ok(ordered);
    }

    /// <summary>
    /// Resolves all settings in a group from the database (bypassing cache).
    /// </summary>
    private async Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>> ResolveGroupFromDatabaseAsync(
        string groupKey,
        IReadOnlyList<SettingScopeEntry> scopeChain,
        CancellationToken cancellationToken)
    {
        var group = await _repository.GetGroupByKeyAsync(groupKey, cancellationToken);

        if (group is null)
        {
            return OperationResult<IReadOnlyList<ResolvedSettingDto>>.NotFound(
                $"Setting group '{groupKey}' not found");
        }

        var definitions = await _repository.GetDefinitionsByGroupAsync(group.Id, cancellationToken);

        var definitionIds = definitions.Select(d => d.Id).ToList();

        var allValues = await _repository.GetValuesForDefinitionsAsync(definitionIds, cancellationToken);

        var results = new List<ResolvedSettingDto>(definitions.Count);

        foreach (var definition in definitions)
        {
            var resolved = ResolveEffectiveValue(definition, allValues, scopeChain);
            var effectiveValue = resolved.Value;

            // Req 3.4: short-circuit null/empty/whitespace before calling Decrypt
            if (definition.IsEncrypted && !string.IsNullOrWhiteSpace(effectiveValue))
            {
                if (_encryptionProvider is null)
                {
                    // Skip this setting — cannot decrypt without provider
                    effectiveValue = null;
                }
                else
                {
                    effectiveValue = _encryptionProvider.Decrypt(effectiveValue);
                }
            }

            // Req 4.2–4.8: single-pass secret reference resolution on read path
            if (effectiveValue is not null && effectiveValue.StartsWith("secretref://", StringComparison.Ordinal))
            {
                if (_secretResolver is not null)
                {
                    var resolvedSecret = await _secretResolver.ResolveAsync(effectiveValue, cancellationToken);
                    if (resolvedSecret is not null)
                    {
                        effectiveValue = resolvedSecret;
                    }
                    else
                    {
                        effectiveValue = null;
                    }
                }
                // No resolver registered → literal pass-through (Req 4.3)
            }

            if (definition.IsSecret && effectiveValue is not null)
            {
                effectiveValue = SecretMask;
            }

            results.Add(new ResolvedSettingDto(
                definition,
                effectiveValue,
                resolved.SourceLevelId,
                resolved.SourceScopeId,
                resolved.IsInherited));
        }

        var ordered = results.OrderBy(r => r.Definition.DisplayOrder).ToList();
        return OperationResult<IReadOnlyList<ResolvedSettingDto>>.Ok(ordered);
    }

    /// <summary>
    /// Computes a deterministic hash from the scope chain entries for use in cache keys.
    /// Uses <see cref="HashCode.Combine"/> on ordered (LevelId, ScopeId) pairs.
    /// </summary>
    internal static int ComputeScopeChainHash(IReadOnlyList<SettingScopeEntry> scopeChain)
    {
        if (scopeChain is not { Count: > 0 })
        {
            return 0;
        }

        var hash = new HashCode();
        foreach (var entry in scopeChain)
        {
            hash.Add(entry.LevelId);
            hash.Add(entry.ScopeId);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Resolves the effective value for a definition by walking the scope chain.
    /// </summary>
    private static ResolvedValue ResolveEffectiveValue(
        SettingDefinitionDto definition,
        IReadOnlyList<SettingValueDto> allValues,
        IReadOnlyList<SettingScopeEntry> scopeChain)
    {
        var allowedLevelIds = definition.AllowedLevelIds.ToHashSet();

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
        SettingDefinitionDto definition,
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
    /// Internal record for holding resolved value data during bulk resolution.
    /// </summary>
    private sealed record ResolvedValue(
        string? Value,
        Guid? SourceLevelId,
        Guid? SourceScopeId,
        bool IsInherited);

    #endregion
}
