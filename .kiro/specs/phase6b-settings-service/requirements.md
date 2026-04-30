# Requirements Document

## Introduction

Phase 6B builds the service layer for the GroundUp cascading settings infrastructure established in Phase 6A. Where Phase 6A delivered the data model (6 entities, DTOs, EF configurations), Phase 6B delivers the runtime behavior: cascading resolution that walks a caller-supplied scope chain to find the effective value, type-safe deserialization from string storage to CLR types, transparent encryption/decryption of sensitive values, validation of incoming values against definition rules, and domain event publishing when settings change.

The central abstraction is `ISettingsService`, which consuming applications depend on for all settings operations. The caller provides a `SettingScopeEntry` list representing the current context (e.g., User → Team → Tenant → System), and the service resolves the effective value by checking each scope entry in order, returning the first match or falling back to the definition's default value.

Phase 6B does NOT include caching (Phase 6C), API controllers (Phase 6C), data seeders (Phase 6C), or integration tests against real Postgres (Phase 6D). The service uses EF Core directly (not through BaseRepository) since settings have their own query patterns that don't fit the generic CRUD model.

## Glossary

- **ISettingsService**: The primary service interface that consuming applications use for all settings operations — resolving effective values, setting values at specific levels, retrieving all settings for a scope, and deleting overrides.
- **SettingScopeEntry**: A record representing one entry in the scope chain, containing a `LevelId` (which cascade level) and an optional `ScopeId` (the specific entity at that level). The consuming application builds the scope chain based on the current context.
- **Scope_Chain**: An ordered list of `SettingScopeEntry` records, arranged from most specific to least specific (e.g., User → Team → Tenant → System). The resolution service checks each entry in order and returns the first match.
- **Cascade_Resolution**: The algorithm that walks the scope chain from most specific to least specific, looking for a `SettingValue` matching the setting definition, level, and scope. The first match wins. If no match is found, the `SettingDefinition.DefaultValue` is returned.
- **SettingsService**: The concrete implementation of `ISettingsService` that performs cascade resolution, type conversion, encryption/decryption, validation, and event publishing. Registered as scoped in the DI container.
- **ResolvedSettingDto**: A DTO representing a fully resolved setting, including the definition metadata, the effective value, which level the value came from, and whether the value is inherited or directly overridden at the current scope.
- **SettingChangedEvent**: A domain event published via `IEventBus` when a setting value is created, updated, or deleted. Carries the setting key, level, scope, old value, and new value.
- **SettingValueConverter**: An internal helper responsible for deserializing string values into typed CLR objects based on the `SettingDataType` enum, and for handling `AllowMultiple` settings as JSON arrays.
- **OperationResult**: The framework's standard return type for all service methods. Business logic errors return `OperationResult.Fail(...)` — exceptions are never thrown for expected failures.
- **ISettingEncryptionProvider**: The interface (defined in Phase 6A) that consuming applications implement to provide encryption/decryption of sensitive setting values. Phase 6B consumes this interface.
- **IEventBus**: The framework's event publishing interface (defined in Phase 2). Phase 6B publishes `SettingChangedEvent` through this interface.
- **GroundUpDbContext**: The abstract base DbContext (from Phase 6A) that the settings service queries against. The service accepts `GroundUpDbContext` — not a specific derived context — so it works with any consuming application.
- **AllowedLevels**: The `SettingDefinitionLevel` junction records that declare which cascade levels a setting definition can be overridden at. The service enforces this constraint on write operations.

## Requirements

### Requirement 1: SettingScopeEntry Record

**User Story:** As a consuming application developer, I want a simple record type to represent a single entry in the scope chain, so that I can build the cascade context from my application's current user, team, tenant, or other contextual information.

#### Acceptance Criteria

1. THE SettingScopeEntry record SHALL reside in `src/GroundUp.Core/Models/SettingScopeEntry.cs`.
2. THE SettingScopeEntry record SHALL have a `LevelId` property of type `Guid` identifying which cascade level this entry represents.
3. THE SettingScopeEntry record SHALL have a `ScopeId` property of type `Guid?` identifying the specific entity at that level (e.g., a UserId or TenantId). A null `ScopeId` indicates the root/system level where no specific entity scope applies.
4. THE SettingScopeEntry record SHALL be a readonly record struct for zero-allocation usage in scope chain lists.
5. THE SettingScopeEntry record SHALL have XML doc comments on the type and each property.

### Requirement 2: ISettingsService Interface

**User Story:** As a consuming application developer, I want a single service interface for all settings operations, so that I can resolve effective values, set overrides, retrieve all settings for a scope, and delete overrides through a consistent API.

#### Acceptance Criteria

1. THE ISettingsService interface SHALL reside in `src/GroundUp.Core/Abstractions/ISettingsService.cs`.
2. THE ISettingsService interface SHALL declare a `GetAsync<T>` method accepting a `string key` and an `IReadOnlyList<SettingScopeEntry> scopeChain`, returning `Task<OperationResult<T>>`.
3. THE ISettingsService interface SHALL declare a `SetAsync` method accepting a `string key`, a `string value`, a `Guid levelId`, and a `Guid? scopeId`, returning `Task<OperationResult<SettingValueDto>>`.
4. THE ISettingsService interface SHALL declare a `GetAllForScopeAsync` method accepting an `IReadOnlyList<SettingScopeEntry> scopeChain`, returning `Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>>`.
5. THE ISettingsService interface SHALL declare a `GetGroupAsync` method accepting a `string groupKey` and an `IReadOnlyList<SettingScopeEntry> scopeChain`, returning `Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>>`.
6. THE ISettingsService interface SHALL declare a `DeleteValueAsync` method accepting a `Guid settingValueId`, returning `Task<OperationResult>`.
7. ALL methods on ISettingsService SHALL accept a `CancellationToken cancellationToken = default` parameter.
8. THE ISettingsService interface SHALL have XML doc comments on the interface and each method describing the contract, parameters, and return semantics.

### Requirement 3: ResolvedSettingDto

**User Story:** As a consuming application developer, I want a DTO that represents a fully resolved setting with its effective value and provenance information, so that I can display settings in a UI showing where each value came from and whether it is inherited or overridden.

#### Acceptance Criteria

1. THE ResolvedSettingDto record SHALL reside in `src/GroundUp.Core/Dtos/Settings/ResolvedSettingDto.cs`.
2. THE ResolvedSettingDto record SHALL have a `Definition` property of type `SettingDefinitionDto` containing the full setting definition metadata.
3. THE ResolvedSettingDto record SHALL have an `EffectiveValue` property of type `string?` containing the resolved value after cascade resolution (decrypted if the setting is encrypted, or masked if the setting is secret).
4. THE ResolvedSettingDto record SHALL have a `SourceLevelId` property of type `Guid?` identifying which level the effective value came from. A null value indicates the value is the definition's default.
5. THE ResolvedSettingDto record SHALL have a `SourceScopeId` property of type `Guid?` identifying the specific scope entity the value came from.
6. THE ResolvedSettingDto record SHALL have an `IsInherited` property of type `bool` indicating whether the effective value was inherited from a higher level in the scope chain (true) or directly set at the most specific scope entry (false). A value from the definition default is also considered inherited.
7. THE ResolvedSettingDto record SHALL have XML doc comments on the type and each property.

### Requirement 4: Cascade Resolution Logic

**User Story:** As a consuming application developer, I want the settings service to walk the scope chain from most specific to least specific and return the first matching value, so that more specific overrides take precedence over broader defaults.

#### Acceptance Criteria

1. WHEN `GetAsync<T>` is called with a setting key and scope chain, THE SettingsService SHALL look up the `SettingDefinition` by key, including its `AllowedLevels` collection.
2. WHEN the setting definition is not found for the given key, THE SettingsService SHALL return `OperationResult<T>.NotFound` with a message identifying the missing key.
3. WHEN resolving the effective value, THE SettingsService SHALL iterate through the scope chain entries in order (index 0 is most specific) and for each entry, query for a `SettingValue` matching the `SettingDefinitionId`, `LevelId`, and `ScopeId`.
4. WHEN a matching `SettingValue` is found at a scope chain entry, THE SettingsService SHALL return that value as the effective value and stop searching further entries.
5. WHEN no matching `SettingValue` is found in any scope chain entry, THE SettingsService SHALL return the `SettingDefinition.DefaultValue` as the effective value.
6. WHEN a matching `SettingValue` is found at a level that is NOT in the definition's `AllowedLevels`, THE SettingsService SHALL skip that value and continue searching the next scope chain entry.
7. WHEN the scope chain is empty or null, THE SettingsService SHALL return the `SettingDefinition.DefaultValue`.

### Requirement 5: Type-Safe Value Deserialization

**User Story:** As a consuming application developer, I want `GetAsync<T>` to return a properly typed value based on the setting's data type, so that I can use settings as `int`, `bool`, `decimal`, or other CLR types without manual parsing.

#### Acceptance Criteria

1. WHEN the effective value is resolved and the `SettingDataType` is `String`, THE SettingsService SHALL return the value as `string`.
2. WHEN the effective value is resolved and the `SettingDataType` is `Int`, THE SettingsService SHALL parse the value to `int` using invariant culture.
3. WHEN the effective value is resolved and the `SettingDataType` is `Long`, THE SettingsService SHALL parse the value to `long` using invariant culture.
4. WHEN the effective value is resolved and the `SettingDataType` is `Decimal`, THE SettingsService SHALL parse the value to `decimal` using invariant culture.
5. WHEN the effective value is resolved and the `SettingDataType` is `Bool`, THE SettingsService SHALL parse the value to `bool` (case-insensitive "true"/"false").
6. WHEN the effective value is resolved and the `SettingDataType` is `DateTime`, THE SettingsService SHALL parse the value to `DateTime` using ISO 8601 round-trip format ("O").
7. WHEN the effective value is resolved and the `SettingDataType` is `Date`, THE SettingsService SHALL parse the value to `DateOnly` using ISO 8601 date format ("yyyy-MM-dd").
8. WHEN the effective value is resolved and the `SettingDataType` is `Json`, THE SettingsService SHALL deserialize the value to `T` using `System.Text.Json.JsonSerializer`.
9. WHEN the `SettingDefinition.AllowMultiple` flag is true, THE SettingsService SHALL deserialize the value as a JSON array into `List<T>` where the element type matches the `SettingDataType`.
10. IF the value cannot be parsed or deserialized to the requested type `T`, THEN THE SettingsService SHALL return `OperationResult<T>.Fail` with a message describing the conversion failure, the setting key, the expected type, and the actual value.
11. WHEN the effective value is null (no stored value and no default), THE SettingsService SHALL return `OperationResult<T>.Ok` with `default(T)` as the data.

### Requirement 6: Encryption and Decryption

**User Story:** As a consuming application developer, I want sensitive setting values to be transparently encrypted on write and decrypted on read, so that secrets like API keys and connection strings are protected at rest without requiring manual encryption logic.

#### Acceptance Criteria

1. WHEN reading a setting value where the `SettingDefinition.IsEncrypted` flag is true and an `ISettingEncryptionProvider` is registered, THE SettingsService SHALL decrypt the stored value via `ISettingEncryptionProvider.Decrypt` before returning it.
2. WHEN writing a setting value where the `SettingDefinition.IsEncrypted` flag is true and an `ISettingEncryptionProvider` is registered, THE SettingsService SHALL encrypt the value via `ISettingEncryptionProvider.Encrypt` before persisting it.
3. IF a setting with `IsEncrypted=true` is accessed and no `ISettingEncryptionProvider` is registered in the DI container, THEN THE SettingsService SHALL return `OperationResult.Fail` with a clear error message indicating that an encryption provider is required but not registered.
4. WHEN reading a setting value where the `SettingDefinition.IsSecret` flag is true via `GetAllForScopeAsync` or `GetGroupAsync`, THE SettingsService SHALL mask the `EffectiveValue` in the `ResolvedSettingDto` (e.g., replace with "••••••••") rather than returning the plaintext value.
5. WHEN reading a single setting value via `GetAsync<T>` where `IsSecret` is true, THE SettingsService SHALL return the actual decrypted value (not masked), because the caller explicitly requested a specific setting by key and needs the real value for programmatic use.

### Requirement 7: Validation on Set

**User Story:** As a consuming application developer, I want the settings service to validate incoming values against the definition's rules before persisting, so that invalid values are rejected with clear error messages rather than stored and causing runtime failures later.

#### Acceptance Criteria

1. WHEN `SetAsync` is called, THE SettingsService SHALL verify that a `SettingDefinition` exists for the given key. IF the definition does not exist, THEN THE SettingsService SHALL return `OperationResult.NotFound`.
2. WHEN `SetAsync` is called, THE SettingsService SHALL verify that the specified `levelId` is in the definition's `AllowedLevels`. IF the level is not allowed, THEN THE SettingsService SHALL return `OperationResult.BadRequest` with a message identifying the disallowed level.
3. WHEN the `SettingDefinition.IsRequired` flag is true and the provided value is null or empty, THE SettingsService SHALL return `OperationResult.BadRequest` with a message indicating the value is required.
4. WHEN the `SettingDefinition.MinValue` is set and the provided value (parsed as the appropriate numeric type) is less than `MinValue`, THE SettingsService SHALL return `OperationResult.BadRequest` with a message indicating the minimum allowed value.
5. WHEN the `SettingDefinition.MaxValue` is set and the provided value (parsed as the appropriate numeric type) is greater than `MaxValue`, THE SettingsService SHALL return `OperationResult.BadRequest` with a message indicating the maximum allowed value.
6. WHEN the `SettingDefinition.MinLength` is set and the provided value's string length is less than `MinLength`, THE SettingsService SHALL return `OperationResult.BadRequest` with a message indicating the minimum required length.
7. WHEN the `SettingDefinition.MaxLength` is set and the provided value's string length is greater than `MaxLength`, THE SettingsService SHALL return `OperationResult.BadRequest` with a message indicating the maximum allowed length.
8. WHEN the `SettingDefinition.RegexPattern` is set and the provided value does not match the pattern, THE SettingsService SHALL return `OperationResult.BadRequest` with the `ValidationMessage` from the definition (or a default message if `ValidationMessage` is null).
9. WHEN the `SettingDefinition.IsReadOnly` flag is true, THE SettingsService SHALL return `OperationResult.BadRequest` with a message indicating the setting is read-only and cannot be modified.

### Requirement 8: Set Value Persistence

**User Story:** As a consuming application developer, I want `SetAsync` to create or update a setting value at a specific level and scope, so that I can override settings at any allowed level in the cascade hierarchy.

#### Acceptance Criteria

1. WHEN `SetAsync` is called with a key, value, levelId, and scopeId that does not match an existing `SettingValue`, THE SettingsService SHALL create a new `SettingValue` entity with the provided values and save it to the database.
2. WHEN `SetAsync` is called with a key, value, levelId, and scopeId that matches an existing `SettingValue` (same SettingDefinitionId, LevelId, ScopeId), THE SettingsService SHALL update the existing `SettingValue.Value` and save the change.
3. WHEN `SetAsync` succeeds, THE SettingsService SHALL return `OperationResult<SettingValueDto>.Ok` with the created or updated `SettingValueDto`.
4. WHEN `SetAsync` succeeds, THE SettingsService SHALL capture the old value (if updating) before persisting the new value, for inclusion in the `SettingChangedEvent`.

### Requirement 9: Delete Value

**User Story:** As a consuming application developer, I want to delete a setting override at a specific level, so that the setting reverts to the next value in the cascade chain or the definition default.

#### Acceptance Criteria

1. WHEN `DeleteValueAsync` is called with a `settingValueId`, THE SettingsService SHALL look up the `SettingValue` by ID, including its `SettingDefinition`.
2. WHEN the `SettingValue` is not found, THE SettingsService SHALL return `OperationResult.NotFound`.
3. WHEN the `SettingValue` is found, THE SettingsService SHALL remove it from the database and save the change.
4. WHEN `DeleteValueAsync` succeeds, THE SettingsService SHALL return `OperationResult.Ok`.

### Requirement 10: Event Publishing

**User Story:** As a framework consumer, I want the settings service to publish domain events when setting values change, so that other parts of the application can react to configuration changes (e.g., invalidate caches, update runtime behavior).

#### Acceptance Criteria

1. THE SettingChangedEvent record SHALL extend `BaseEvent` and reside in `src/GroundUp.Events/SettingChangedEvent.cs`.
2. THE SettingChangedEvent record SHALL have properties: `SettingKey` (string), `LevelId` (Guid), `ScopeId` (Guid?), `OldValue` (string?), `NewValue` (string?).
3. WHEN `SetAsync` succeeds (create or update), THE SettingsService SHALL publish a `SettingChangedEvent` via `IEventBus` with the setting key, level, scope, old value (null for new creates), and new value.
4. WHEN `DeleteValueAsync` succeeds, THE SettingsService SHALL publish a `SettingChangedEvent` via `IEventBus` with the setting key, level, scope, old value, and null as the new value.
5. IF event publishing fails, THEN THE SettingsService SHALL catch the exception and continue — event publishing failures SHALL NOT cause the settings operation to fail.
6. THE SettingChangedEvent record SHALL have XML doc comments on the type and each property.

### Requirement 11: Effective Settings View

**User Story:** As a consuming application developer, I want to retrieve all settings with their effective values for a given scope chain, so that I can render a complete settings UI showing each setting's current value and where it came from.

#### Acceptance Criteria

1. WHEN `GetAllForScopeAsync` is called with a scope chain, THE SettingsService SHALL load all `SettingDefinition` records with their `AllowedLevels`.
2. FOR EACH `SettingDefinition`, THE SettingsService SHALL resolve the effective value using the same cascade resolution logic as `GetAsync<T>` (walk the scope chain, first match wins, fall back to default).
3. FOR EACH resolved setting, THE SettingsService SHALL return a `ResolvedSettingDto` containing the definition metadata, effective value, source level, source scope, and whether the value is inherited.
4. WHEN a setting's effective value comes from the first entry in the scope chain (the most specific level), THE SettingsService SHALL set `IsInherited` to false on the `ResolvedSettingDto`.
5. WHEN a setting's effective value comes from any entry other than the first in the scope chain, or from the definition default, THE SettingsService SHALL set `IsInherited` to true on the `ResolvedSettingDto`.
6. WHEN a setting has `IsSecret=true`, THE SettingsService SHALL mask the `EffectiveValue` in the `ResolvedSettingDto` rather than returning the plaintext value.

### Requirement 12: Group Settings View

**User Story:** As a consuming application developer, I want to retrieve all settings in a specific group with their effective values, so that I can render a composite settings section (e.g., all database connection settings together).

#### Acceptance Criteria

1. WHEN `GetGroupAsync` is called with a group key and scope chain, THE SettingsService SHALL look up the `SettingGroup` by key.
2. WHEN the group is not found, THE SettingsService SHALL return `OperationResult.NotFound` with a message identifying the missing group key.
3. WHEN the group is found, THE SettingsService SHALL load all `SettingDefinition` records belonging to that group and resolve each one's effective value using the cascade resolution logic.
4. THE SettingsService SHALL return the resolved settings ordered by `SettingDefinition.DisplayOrder`.

### Requirement 13: SettingsService Implementation

**User Story:** As a framework developer, I want the SettingsService to be a concrete, testable implementation that uses EF Core directly for its query patterns, so that settings resolution can use optimized queries without being constrained by the generic BaseRepository pattern.

#### Acceptance Criteria

1. THE SettingsService class SHALL reside in `src/GroundUp.Services/Settings/SettingsService.cs` and implement `ISettingsService`.
2. THE SettingsService class SHALL accept `GroundUpDbContext`, `IEventBus`, and `ISettingEncryptionProvider?` (optional) as constructor dependencies.
3. THE SettingsService class SHALL use EF Core directly (via `DbContext.Set<T>()`) for querying settings entities, not through `IBaseRepository<T>`.
4. THE SettingsService class SHALL be a sealed class with XML doc comments on the class and all public methods.
5. THE SettingsService class SHALL use `AsNoTracking()` for all read-only queries.

### Requirement 14: DI Registration

**User Story:** As a consuming application developer, I want a single extension method to register the settings service in the DI container, so that I can add settings support with one line of code in my startup configuration.

#### Acceptance Criteria

1. THE DI registration SHALL provide an `AddGroundUpSettings` extension method on `IServiceCollection`, residing in `src/GroundUp.Services/Settings/SettingsServiceCollectionExtensions.cs`.
2. THE `AddGroundUpSettings` method SHALL register `ISettingsService` as `SettingsService` with scoped lifetime.
3. THE `AddGroundUpSettings` method SHALL NOT require `ISettingEncryptionProvider` to be registered — the settings service works without encryption support, and only fails when an encrypted setting is actually accessed without a provider.

### Requirement 15: Solution Compilation

**User Story:** As a framework developer, I want the entire solution to compile with zero errors after adding all Phase 6B types, so that the service layer is ready for caching, controllers, and integration tests in subsequent phases.

#### Acceptance Criteria

1. WHEN `dotnet build groundup.sln` is executed, THE build SHALL complete with zero errors.
2. THE Phase 6B types SHALL follow the existing project conventions: file-scoped namespaces, sealed classes where appropriate, XML doc comments on all public types and members, one class per file.
3. THE Phase 6B types SHALL follow the framework's dependency rules: `ISettingsService` in `GroundUp.Core`, `SettingsService` in `GroundUp.Services`, `SettingChangedEvent` in `GroundUp.Events`.
