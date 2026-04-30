# Implementation Plan: Phase 6B — Settings Module: Cascading Resolution Service

## Overview

This plan implements the cascading resolution service layer for the GroundUp settings infrastructure. The implementation is broken into 4 PRs at natural boundaries to keep each PR small and reviewable (~10-15 files max):

- **PR1**: Core types (SettingScopeEntry, ISettingsService, ResolvedSettingDto, SettingChangedEvent) + build verification
- **PR2**: SettingValueConverter + unit tests for converter
- **PR3**: SettingsService implementation + DI registration
- **PR4**: Unit tests for SettingsService

All code uses C# targeting net8.0 with nullable reference types, file-scoped namespaces, sealed classes, and XML doc comments per project conventions.

## Tasks

- [x] 1. PR1: Core types and domain event
  - [x] 1.1 Create SettingScopeEntry readonly record struct
    - Create `src/GroundUp.Core/Models/SettingScopeEntry.cs`
    - Readonly record struct with `Guid LevelId` and `Guid? ScopeId` parameters
    - XML doc comments on the type and each property
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 1.2 Create ISettingsService interface
    - Create `src/GroundUp.Core/Abstractions/ISettingsService.cs`
    - Declare `GetAsync<T>(string key, IReadOnlyList<SettingScopeEntry> scopeChain, CancellationToken cancellationToken = default)` returning `Task<OperationResult<T>>`
    - Declare `SetAsync(string key, string value, Guid levelId, Guid? scopeId, CancellationToken cancellationToken = default)` returning `Task<OperationResult<SettingValueDto>>`
    - Declare `GetAllForScopeAsync(IReadOnlyList<SettingScopeEntry> scopeChain, CancellationToken cancellationToken = default)` returning `Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>>`
    - Declare `GetGroupAsync(string groupKey, IReadOnlyList<SettingScopeEntry> scopeChain, CancellationToken cancellationToken = default)` returning `Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>>`
    - Declare `DeleteValueAsync(Guid settingValueId, CancellationToken cancellationToken = default)` returning `Task<OperationResult>`
    - XML doc comments on the interface and each method
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8_

  - [x] 1.3 Create ResolvedSettingDto record
    - Create `src/GroundUp.Core/Dtos/Settings/ResolvedSettingDto.cs`
    - Positional record with: `SettingDefinitionDto Definition`, `string? EffectiveValue`, `Guid? SourceLevelId`, `Guid? SourceScopeId`, `bool IsInherited`
    - XML doc comments on the type and each property
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 1.4 Create SettingChangedEvent record
    - Create `src/GroundUp.Events/SettingChangedEvent.cs`
    - Sealed record extending `BaseEvent`
    - Properties: `required string SettingKey`, `required Guid LevelId`, `Guid? ScopeId`, `string? OldValue`, `string? NewValue`
    - XML doc comments on the type and each property
    - _Requirements: 10.1, 10.2, 10.6_

  - [x] 1.5 Verify solution builds
    - Run `dotnet build groundup.sln` and confirm zero errors
    - _Requirements: 15.1_

  - [x] 1.6 Commit and push PR1
    - Create branch `phase-6b/settings-service` from main
    - Commit core types with message "feat(phase-6b): add core types — SettingScopeEntry, ISettingsService, ResolvedSettingDto, SettingChangedEvent"
    - Push with `-u` flag
    - _Requirements: 15.2, 15.3_

- [x] 2. PR2: SettingValueConverter and its tests
  - [x] 2.1 Add InternalsVisibleTo on GroundUp.Services for test project
    - Add `[assembly: InternalsVisibleTo("GroundUp.Tests.Unit")]` to the Services project (via AssemblyInfo or csproj)
    - Add project reference to `GroundUp.Data.Postgres` in `GroundUp.Services.csproj` (needed for `GroundUpDbContext` dependency)
    - _Requirements: 13.3_

  - [x] 2.2 Implement SettingValueConverter
    - Create `src/GroundUp.Services/Settings/SettingValueConverter.cs`
    - Internal static class with `Convert<T>(string? value, SettingDataType dataType, bool allowMultiple, string settingKey)` returning `OperationResult<T>`
    - Handle all SettingDataType cases: String (direct return), Int (int.TryParse invariant), Long (long.TryParse invariant), Decimal (decimal.TryParse invariant), Bool (bool.TryParse case-insensitive), DateTime (DateTime.TryParseExact "O"), Date (DateOnly.TryParseExact "yyyy-MM-dd"), Json (JsonSerializer.Deserialize<T>)
    - When `allowMultiple` is true, deserialize as JSON array into `List<T>` with element type matching the SettingDataType
    - When value is null, return `OperationResult<T>.Ok(default(T))`
    - On parse failure, return `OperationResult<T>.Fail` with descriptive message including setting key, expected type, and actual value
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10, 5.11_

  - [x] 2.3 Write unit tests for SettingValueConverter
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingValueConverterTests.cs`
    - Test each data type conversion: String passthrough, Int parsing, Long parsing, Decimal parsing, Bool case-insensitive, DateTime ISO 8601, DateOnly yyyy-MM-dd, Json deserialization
    - Test AllowMultiple JSON array deserialization
    - Test null value returns default(T)
    - Test invalid values return Fail with descriptive message
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10, 5.11_

  - [ ]* 2.4 Write property test for type conversion round-trip
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingValueConverterPropertyTests.cs`
    - **Property 2: Round trip consistency** — For any value of a supported SettingDataType, formatting to string and parsing back produces the original value
    - **Validates: Requirements 5.2, 5.3, 5.4, 5.6, 5.7, 5.8, 5.9**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6b-settings-service, Property 2: Type conversion round-trip preserves values`

  - [ ]* 2.5 Write property test for invalid type conversions
    - In `tests/GroundUp.Tests.Unit/Services/Settings/SettingValueConverterPropertyTests.cs`
    - **Property 3: Invalid type conversions produce failure results** — For any non-parseable string, Convert<T> returns OperationResult.Fail with descriptive message
    - **Validates: Requirements 5.10**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6b-settings-service, Property 3: Invalid type conversions produce failure results`

  - [x] 2.6 Verify solution builds and tests pass
    - Run `dotnet build groundup.sln` — zero errors
    - Run `dotnet test` for the SettingValueConverter tests
    - _Requirements: 15.1_

  - [x] 2.7 Commit and push PR2
    - Commit with message "feat(phase-6b): add SettingValueConverter with unit tests"
    - Push to remote
    - _Requirements: 15.2_

- [x] 3. PR3: SettingsService implementation and DI registration
  - [x] 3.1 Implement SettingsService — cascade resolution (GetAsync<T>)
    - Create `src/GroundUp.Services/Settings/SettingsService.cs`
    - Sealed class implementing `ISettingsService`
    - Constructor accepts `GroundUpDbContext`, `IEventBus`, `ISettingEncryptionProvider?` (nullable)
    - Implement `GetAsync<T>`: look up SettingDefinition by key (include AllowedLevels), iterate scope chain in order, query for SettingValue matching (DefinitionId, LevelId, ScopeId), skip values at disallowed levels, return first match or fall back to DefaultValue
    - Handle empty/null scope chain → return default value
    - Handle definition not found → return NotFound
    - Handle IsEncrypted: decrypt via provider, fail if no provider registered
    - Use SettingValueConverter.Convert<T> for type-safe deserialization
    - Use AsNoTracking() for all read queries
    - XML doc comments on class and all public methods
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 5.1–5.11, 6.1, 6.3, 6.5, 13.1, 13.2, 13.3, 13.4, 13.5_

  - [x] 3.2 Implement SettingsService — SetAsync with validation
    - Implement `SetAsync`: validate definition exists, check level is in AllowedLevels, check IsReadOnly, check IsRequired, validate MinValue/MaxValue (numeric), validate MinLength/MaxLength (string), validate RegexPattern
    - Handle encryption on write (encrypt via provider if IsEncrypted, fail if no provider)
    - Create or update SettingValue entity (upsert by DefinitionId + LevelId + ScopeId)
    - Capture old value before update for event publishing
    - Return OperationResult<SettingValueDto>.Ok with the created/updated DTO
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8, 7.9, 8.1, 8.2, 8.3, 8.4, 6.2, 6.3_

  - [x] 3.3 Implement SettingsService — DeleteValueAsync
    - Implement `DeleteValueAsync`: look up SettingValue by ID (include SettingDefinition for event data), return NotFound if missing, remove and save
    - _Requirements: 9.1, 9.2, 9.3, 9.4_

  - [x] 3.4 Implement SettingsService — event publishing
    - After successful SetAsync (create or update), publish SettingChangedEvent with key, level, scope, old value, new value
    - After successful DeleteValueAsync, publish SettingChangedEvent with key, level, scope, old value, null new value
    - Wrap event publishing in try/catch — swallow exceptions (fire-and-forget pattern)
    - _Requirements: 10.3, 10.4, 10.5_

  - [x] 3.5 Implement SettingsService — GetAllForScopeAsync
    - Load all SettingDefinitions with AllowedLevels
    - Batch-load all SettingValues matching any (LevelId, ScopeId) pair in the scope chain in a single query
    - Resolve each definition's effective value in memory using cascade logic
    - Build ResolvedSettingDto for each: set IsInherited based on whether value came from first scope entry or not
    - Mask secret values (IsSecret=true) with "••••••••"
    - Order results by SettingDefinition.DisplayOrder
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

  - [x] 3.6 Implement SettingsService — GetGroupAsync
    - Look up SettingGroup by key, return NotFound if missing
    - Load SettingDefinitions belonging to that group
    - Resolve each using cascade logic (same as GetAllForScopeAsync but filtered to group)
    - Mask secret values, order by DisplayOrder
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [x] 3.7 Create DI registration extension method
    - Create `src/GroundUp.Services/Settings/SettingsServiceCollectionExtensions.cs`
    - Public static class with `AddGroundUpSettings(this IServiceCollection services)` method
    - Register `ISettingsService` as `SettingsService` with scoped lifetime
    - Do NOT require ISettingEncryptionProvider to be registered
    - XML doc comments
    - _Requirements: 14.1, 14.2, 14.3_

  - [x] 3.8 Verify solution builds
    - Run `dotnet build groundup.sln` — zero errors
    - _Requirements: 15.1_

  - [x] 3.9 Commit and push PR3
    - Commit with message "feat(phase-6b): implement SettingsService with cascade resolution, validation, and DI registration"
    - Push to remote
    - _Requirements: 15.2, 15.3_

- [x] 4. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [-] 5. PR4: Unit tests for SettingsService
  - [ ] 5.1 Set up test infrastructure for SettingsService tests
    - Create a test helper/fixture that sets up SQLite in-memory DbContext for EF Core query testing
    - Add `Microsoft.EntityFrameworkCore.Sqlite` package reference to `GroundUp.Tests.Unit.csproj` if not present
    - Create a concrete test DbContext deriving from `GroundUpDbContext` for use in tests
    - Set up NSubstitute mocks for `IEventBus` and `ISettingEncryptionProvider`
    - _Requirements: 13.3_

  - [ ] 5.2 Write unit tests for GetAsync<T>
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceGetAsyncTests.cs`
    - Test: key not found returns NotFound (Req 4.2)
    - Test: empty scope chain returns default value (Req 4.7)
    - Test: first match in scope chain wins (Req 4.3, 4.4)
    - Test: skips values at disallowed levels (Req 4.6)
    - Test: falls back to default when no match (Req 4.5)
    - Test: encrypted setting decrypted on read (Req 6.1)
    - Test: encrypted setting without provider returns Fail (Req 6.3)
    - Test: secret setting via GetAsync returns real value (Req 6.5)
    - Test: null value returns default(T) (Req 5.11)
    - _Requirements: 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 5.11, 6.1, 6.3, 6.5_

  - [ ] 5.3 Write unit tests for SetAsync
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceSetAsyncTests.cs`
    - Test: key not found returns NotFound (Req 7.1)
    - Test: disallowed level returns BadRequest (Req 7.2)
    - Test: required value empty returns BadRequest (Req 7.3)
    - Test: value below MinValue returns BadRequest (Req 7.4)
    - Test: value above MaxValue returns BadRequest (Req 7.5)
    - Test: value shorter than MinLength returns BadRequest (Req 7.6)
    - Test: value longer than MaxLength returns BadRequest (Req 7.7)
    - Test: regex mismatch returns BadRequest with ValidationMessage (Req 7.8)
    - Test: read-only setting returns BadRequest (Req 7.9)
    - Test: creates new SettingValue when none exists (Req 8.1)
    - Test: updates existing SettingValue when match exists (Req 8.2)
    - Test: returns Ok with SettingValueDto on success (Req 8.3)
    - Test: encrypts value on write when IsEncrypted (Req 6.2)
    - Test: encrypted write without provider returns Fail (Req 6.3)
    - _Requirements: 7.1–7.9, 8.1–8.4, 6.2, 6.3_

  - [ ] 5.4 Write unit tests for DeleteValueAsync
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceDeleteValueAsyncTests.cs`
    - Test: not found returns NotFound (Req 9.2)
    - Test: successful delete removes entity and returns Ok (Req 9.3, 9.4)
    - _Requirements: 9.2, 9.3, 9.4_

  - [ ] 5.5 Write unit tests for GetAllForScopeAsync
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceGetAllForScopeAsyncTests.cs`
    - Test: loads all definitions and resolves each (Req 11.1, 11.2)
    - Test: IsInherited false when value from first scope entry (Req 11.4)
    - Test: IsInherited true when value from higher level or default (Req 11.5)
    - Test: secret values are masked (Req 11.6)
    - _Requirements: 11.1, 11.2, 11.4, 11.5, 11.6_

  - [ ] 5.6 Write unit tests for GetGroupAsync
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceGetGroupAsyncTests.cs`
    - Test: group not found returns NotFound (Req 12.2)
    - Test: resolves settings in group with cascade logic (Req 12.3)
    - Test: results ordered by DisplayOrder (Req 12.4)
    - _Requirements: 12.2, 12.3, 12.4_

  - [ ] 5.7 Write unit tests for event publishing
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceEventTests.cs`
    - Test: SetAsync publishes SettingChangedEvent on create (Req 10.3)
    - Test: SetAsync publishes SettingChangedEvent on update with old value (Req 10.3)
    - Test: DeleteValueAsync publishes SettingChangedEvent with null new value (Req 10.4)
    - Test: event publishing failure does not fail the operation (Req 10.5)
    - _Requirements: 10.3, 10.4, 10.5_

  - [ ] 5.8 Write unit tests for DI registration
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceCollectionExtensionsTests.cs`
    - Test: AddGroundUpSettings registers ISettingsService as scoped (Req 14.2)
    - Test: works without ISettingEncryptionProvider registered (Req 14.3)
    - _Requirements: 14.2, 14.3_

  - [ ]* 5.9 Write property test for cascade resolution
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/CascadeResolutionPropertyTests.cs`
    - **Property 1: Cascade resolution returns the first match at an allowed level, or the default**
    - **Validates: Requirements 4.3, 4.4, 4.5, 4.6, 4.7**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Generate random scope chains, stored values at various positions, allowed level sets
    - Tag: `// Feature: phase6b-settings-service, Property 1: Cascade resolution returns the first match at an allowed level, or the default`

  - [ ]* 5.10 Write property test for secret masking
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SecretMaskingPropertyTests.cs`
    - **Property 4: Secret settings are masked in bulk reads**
    - **Validates: Requirements 6.4, 11.6**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6b-settings-service, Property 4: Secret settings are masked in bulk reads`

  - [ ]* 5.11 Write property test for disallowed level rejection
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SetAsyncValidationPropertyTests.cs`
    - **Property 5: Writes to disallowed levels are rejected**
    - **Validates: Requirements 7.2**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6b-settings-service, Property 5: Writes to disallowed levels are rejected`

  - [ ]* 5.12 Write property test for numeric range validation
    - In `tests/GroundUp.Tests.Unit/Services/Settings/SetAsyncValidationPropertyTests.cs`
    - **Property 6: Numeric values outside [MinValue, MaxValue] are rejected**
    - **Validates: Requirements 7.4, 7.5**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6b-settings-service, Property 6: Numeric values outside [MinValue, MaxValue] are rejected`

  - [ ]* 5.13 Write property test for string length validation
    - In `tests/GroundUp.Tests.Unit/Services/Settings/SetAsyncValidationPropertyTests.cs`
    - **Property 7: Strings outside [MinLength, MaxLength] are rejected**
    - **Validates: Requirements 7.6, 7.7**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6b-settings-service, Property 7: Strings outside [MinLength, MaxLength] are rejected`

  - [ ]* 5.14 Write property test for IsInherited flag
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/IsInheritedPropertyTests.cs`
    - **Property 8: IsInherited flag reflects value provenance**
    - **Validates: Requirements 11.4, 11.5**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6b-settings-service, Property 8: IsInherited flag reflects value provenance`

  - [ ]* 5.15 Write property test for DisplayOrder sorting
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/DisplayOrderPropertyTests.cs`
    - **Property 9: Group and bulk results are ordered by DisplayOrder**
    - **Validates: Requirements 12.4**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6b-settings-service, Property 9: Group and bulk results are ordered by DisplayOrder`

  - [ ] 5.16 Verify all tests pass and solution builds
    - Run `dotnet build groundup.sln` — zero errors
    - Run `dotnet test` — all tests pass
    - _Requirements: 15.1_

  - [ ] 5.17 Commit and push PR4
    - Commit with message "test(phase-6b): add unit tests for SettingsService"
    - Push to remote
    - _Requirements: 15.2_

- [ ] 6. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The implementation uses SQLite in-memory provider for test DbContext (not EF InMemory provider, which is banned for integration tests but acceptable here for unit-level EF query testing)
- The SettingsService does NOT inherit from BaseService<T> — it has its own query patterns and uses EF Core directly
- PR boundaries are designed to keep each PR at ~10-15 files for easy review
