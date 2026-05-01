# Implementation Plan: Phase 6C — Settings Module: API Layer, Caching, and Admin Service

## Overview

This plan implements the API layer, caching, scope chain provider, admin service, and sample app wiring for the GroundUp settings module. The implementation is broken into 6 task groups at natural boundaries to keep each reviewable (~10-15 files max):

- **Task 1**: Core interfaces and options (IScopeChainProvider, ISettingsAdminService, SettingsCacheOptions, request DTOs)
- **Task 2**: DefaultScopeChainProvider + convenience overloads on ISettingsService/SettingsService + cache layer
- **Task 3**: SettingsAdminService implementation
- **Task 4**: API controllers (SettingsController in framework, admin controllers in sample app)
- **Task 5**: Sample app wiring (DI registration updates, seeder, migration, Program.cs)
- **Task 6**: Integration tests (cascading resolution, admin CRUD, cache invalidation)

All code uses C# targeting net8.0 with nullable reference types, file-scoped namespaces, sealed classes, and XML doc comments per project conventions.

## Tasks

- [x] 1. Core interfaces, options, and request DTOs
  - [x] 1.1 Create IScopeChainProvider interface
    - Create `src/GroundUp.Core/Abstractions/IScopeChainProvider.cs`
    - Declare `GetScopeChainAsync(CancellationToken cancellationToken = default)` returning `Task<IReadOnlyList<SettingScopeEntry>>`
    - XML doc comments on interface and method describing contract and expected ordering (most specific to least specific)
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 1.2 Create SettingsCacheOptions class
    - Create `src/GroundUp.Core/Models/SettingsCacheOptions.cs`
    - Sealed class with `CacheDuration` property of type `TimeSpan`, default 15 minutes
    - XML doc comments
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 1.3 Create ISettingsAdminService interface
    - Create `src/GroundUp.Core/Abstractions/ISettingsAdminService.cs`
    - Declare CRUD methods for SettingLevel: `GetAllLevelsAsync`, `CreateLevelAsync`, `UpdateLevelAsync`, `DeleteLevelAsync`
    - Declare CRUD methods for SettingGroup: `GetAllGroupsAsync`, `CreateGroupAsync`, `UpdateGroupAsync`, `DeleteGroupAsync`
    - Declare CRUD methods for SettingDefinition: `GetAllDefinitionsAsync`, `GetDefinitionByIdAsync`, `CreateDefinitionAsync`, `UpdateDefinitionAsync`, `DeleteDefinitionAsync`
    - All methods return `OperationResult<T>` or `OperationResult` and accept `CancellationToken cancellationToken = default`
    - XML doc comments on interface and each method
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

  - [x] 1.4 Create request DTOs for write operations
    - Create `src/GroundUp.Core/Dtos/Settings/CreateSettingLevelDto.cs` — record with Name, Description, ParentId, DisplayOrder
    - Create `src/GroundUp.Core/Dtos/Settings/UpdateSettingLevelDto.cs` — record with Name, Description, ParentId, DisplayOrder
    - Create `src/GroundUp.Core/Dtos/Settings/CreateSettingGroupDto.cs` — record with Key, DisplayName, Description, Icon, DisplayOrder
    - Create `src/GroundUp.Core/Dtos/Settings/UpdateSettingGroupDto.cs` — record with Key, DisplayName, Description, Icon, DisplayOrder
    - Create `src/GroundUp.Core/Dtos/Settings/CreateSettingDefinitionDto.cs` — record with all definition fields plus Options and AllowedLevelIds collections
    - Create `src/GroundUp.Core/Dtos/Settings/CreateSettingOptionDto.cs` — record with Value, Label, DisplayOrder, IsDefault, ParentOptionValue
    - Create `src/GroundUp.Core/Dtos/Settings/UpdateSettingDefinitionDto.cs` — record with all definition fields plus Options and AllowedLevelIds collections
    - Create `src/GroundUp.Core/Dtos/Settings/SetSettingValueDto.cs` — record with Value, LevelId, ScopeId
    - All DTOs are record types with XML doc comments
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5_

  - [x] 1.5 Add convenience overloads to ISettingsService interface
    - Add `GetAsync<T>(string key, CancellationToken cancellationToken = default)` returning `Task<OperationResult<T>>`
    - Add `GetAllForScopeAsync(CancellationToken cancellationToken = default)` returning `Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>>`
    - Add `GetGroupAsync(string groupKey, CancellationToken cancellationToken = default)` returning `Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>>`
    - Existing explicit scope chain overloads remain unchanged
    - _Requirements: 3.1, 3.2, 3.3, 3.6_

  - [x] 1.6 Verify solution builds
    - Run `dotnet build groundup.sln` and confirm zero errors
    - _Requirements: 14.1_

- [x] 2. Scope chain provider, convenience overloads implementation, and cache layer
  - [x] 2.1 Implement DefaultScopeChainProvider
    - Create `src/GroundUp.Services/Settings/DefaultScopeChainProvider.cs`
    - Sealed class implementing `IScopeChainProvider`
    - Constructor accepts `ITenantContext` and `GroundUpDbContext`
    - When TenantId is not Guid.Empty, query for "Tenant" level and return single-entry scope chain
    - When TenantId is Guid.Empty or "Tenant" level not found, return empty list
    - XML doc comments
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7_

  - [x] 2.2 Implement convenience overloads in SettingsService
    - Add `IScopeChainProvider` as constructor dependency to `SettingsService`
    - Implement `GetAsync<T>(string key, CancellationToken)` — calls `IScopeChainProvider.GetScopeChainAsync()` then delegates to existing `GetAsync<T>(key, scopeChain)`
    - Implement `GetAllForScopeAsync(CancellationToken)` — calls provider then delegates to existing `GetAllForScopeAsync(scopeChain)`
    - Implement `GetGroupAsync(string groupKey, CancellationToken)` — calls provider then delegates to existing `GetGroupAsync(groupKey, scopeChain)`
    - _Requirements: 3.4, 3.5_

  - [x] 2.3 Implement in-memory cache in SettingsService
    - Add `IMemoryCache` and `IOptions<SettingsCacheOptions>` as constructor dependencies
    - Implement cache key generation: `settings:get:{key}:{scopeChainHash}`, `settings:all:{scopeChainHash}`, `settings:group:{groupKey}:{scopeChainHash}`
    - Implement scope chain hash using `HashCode.Combine` on ordered (LevelId, ScopeId) pairs
    - Wrap `GetAsync<T>(key, scopeChain)` with cache check: TryGetValue → return cached; else resolve from DB → Set with TTL → return
    - Wrap `GetAllForScopeAsync(scopeChain)` and `GetGroupAsync(groupKey, scopeChain)` with same cache pattern
    - `SetAsync` and `DeleteValueAsync` bypass cache (write path unchanged)
    - Cache exceptions caught and swallowed — fall through to DB resolution
    - Track active cache keys in `ConcurrentDictionary<string, byte>` for targeted invalidation
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.9, 4.10_

  - [x] 2.4 Implement SettingsCacheInvalidationHandler
    - Create `src/GroundUp.Services/Settings/SettingsCacheInvalidationHandler.cs`
    - Sealed class implementing `IEventHandler<SettingChangedEvent>`
    - Constructor accepts `IMemoryCache` and a reference to the cache key tracking set
    - On event: remove all cache entries whose key starts with `settings:get:{settingKey}:`
    - Also remove all bulk cache entries (keys starting with `settings:all:` and `settings:group:`)
    - Catch exceptions during removal — stale data until TTL is acceptable
    - _Requirements: 4.7, 4.8_

  - [x]* 2.5 Write unit tests for DefaultScopeChainProvider
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/DefaultScopeChainProviderTests.cs`
    - Test: TenantId set and "Tenant" level exists → returns single-entry scope chain
    - Test: TenantId is Guid.Empty → returns empty list
    - Test: "Tenant" level not found → returns empty list
    - _Requirements: 2.3, 2.4, 2.5_

  - [x]* 2.6 Write unit tests for cache behavior
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsCacheTests.cs`
    - Test: cache miss → resolves from DB and stores in cache
    - Test: cache hit → returns cached value without DB query
    - Test: SetAsync does not cache
    - Test: DeleteValueAsync does not cache
    - Test: cache exception → falls through to DB
    - _Requirements: 4.3, 4.4, 4.10_

  - [x]* 2.7 Write unit tests for SettingsCacheInvalidationHandler
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsCacheInvalidationHandlerTests.cs`
    - Test: event received → clears entries for changed key
    - Test: event received → clears bulk cache entries
    - Test: exception during removal does not propagate
    - _Requirements: 4.7, 4.8_

  - [x] 2.8 Verify solution builds
    - Run `dotnet build groundup.sln` — zero errors
    - _Requirements: 14.1_

- [x] 3. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. SettingsAdminService implementation
  - [x] 4.1 Implement SettingsAdminService — Level CRUD
    - Create `src/GroundUp.Services/Settings/SettingsAdminService.cs`
    - Sealed class implementing `ISettingsAdminService`, constructor accepts `GroundUpDbContext`
    - `GetAllLevelsAsync` — query all levels with AsNoTracking, map to SettingLevelDto
    - `CreateLevelAsync` — create entity from DTO, save, return mapped DTO
    - `UpdateLevelAsync` — find by ID (NotFound if missing), update fields, save, return mapped DTO
    - `DeleteLevelAsync` — check for child levels and referencing SettingValues, return BadRequest if references exist, otherwise remove and save
    - XML doc comments
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.7, 6.9_

  - [x] 4.2 Implement SettingsAdminService — Group CRUD
    - `GetAllGroupsAsync` — query all groups with AsNoTracking, map to SettingGroupDto
    - `CreateGroupAsync` — create entity from DTO, save, return mapped DTO
    - `UpdateGroupAsync` — find by ID (NotFound if missing), update fields, save, return mapped DTO
    - `DeleteGroupAsync` — set GroupId to null on all definitions in the group, then remove group and save
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.8, 6.9_

  - [x] 4.3 Implement SettingsAdminService — Definition CRUD
    - `GetAllDefinitionsAsync` — query all definitions with Include(Options) and AsNoTracking, map to SettingDefinitionDto
    - `GetDefinitionByIdAsync` — find by ID with Include(Options, AllowedLevels), NotFound if missing, map to DTO
    - `CreateDefinitionAsync` — create definition entity, add SettingOption records, add SettingDefinitionLevel records, single SaveChangesAsync, return mapped DTO
    - `UpdateDefinitionAsync` — find by ID with Include(Options, AllowedLevels), remove existing options and allowed levels, add new ones from request, save, return mapped DTO
    - `DeleteDefinitionAsync` — find by ID, remove (cascades to options, values, allowed levels via EF config), save
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.9, 6.10_

  - [x]* 4.4 Write unit tests for SettingsAdminService — Level CRUD
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsAdminServiceLevelTests.cs`
    - Test: GetAllLevelsAsync returns all levels
    - Test: CreateLevelAsync creates and returns DTO
    - Test: UpdateLevelAsync with invalid ID returns NotFound
    - Test: DeleteLevelAsync with child levels returns BadRequest
    - Test: DeleteLevelAsync with referencing values returns BadRequest
    - Test: DeleteLevelAsync with no references succeeds
    - _Requirements: 6.7, 6.9_

  - [x]* 4.5 Write unit tests for SettingsAdminService — Group CRUD
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsAdminServiceGroupTests.cs`
    - Test: GetAllGroupsAsync returns all groups
    - Test: CreateGroupAsync creates and returns DTO
    - Test: UpdateGroupAsync with invalid ID returns NotFound
    - Test: DeleteGroupAsync orphans definitions (sets GroupId to null)
    - _Requirements: 6.8, 6.9_

  - [x]* 4.6 Write unit tests for SettingsAdminService — Definition CRUD
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsAdminServiceDefinitionTests.cs`
    - Test: GetAllDefinitionsAsync includes options
    - Test: CreateDefinitionAsync persists options and allowed levels
    - Test: UpdateDefinitionAsync replaces options and allowed levels
    - Test: DeleteDefinitionAsync removes definition
    - Test: GetDefinitionByIdAsync with invalid ID returns NotFound
    - _Requirements: 6.5, 6.6, 6.10_

  - [x] 4.7 Verify solution builds
    - Run `dotnet build groundup.sln` — zero errors
    - _Requirements: 14.1_

- [x] 5. API controllers
  - [x] 5.1 Implement SettingsController in GroundUp.Api
    - Create `src/GroundUp.Api/Controllers/Settings/SettingsController.cs`
    - Custom controller inheriting from `ControllerBase` (NOT BaseController<T>)
    - Annotate with `[ApiController]` and `[Route("api/settings")]`
    - Constructor accepts `ISettingsService`
    - `GET /api/settings/{key}` → calls `GetAsync<string>(key)` convenience overload
    - `GET /api/settings` → calls `GetAllForScopeAsync()` convenience overload
    - `GET /api/settings/groups/{groupKey}` → calls `GetGroupAsync(groupKey)` convenience overload
    - `PUT /api/settings/{key}` → accepts `SetSettingValueDto`, calls `SetAsync(key, dto.Value, dto.LevelId, dto.ScopeId)`
    - `DELETE /api/settings/values/{id}` → calls `DeleteValueAsync(id)`
    - Uses `ToActionResult` helper to convert OperationResult to ActionResult
    - Zero business logic
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8, 7.9, 7.10, 7.11_

  - [x] 5.2 Implement SettingLevelsController in sample app
    - Create `samples/GroundUp.Sample/Controllers/Settings/SettingLevelsController.cs`
    - `[ApiController]`, `[Route("api/settings/levels")]`
    - Constructor accepts `ISettingsAdminService`
    - `GET` → `GetAllLevelsAsync()`
    - `POST` → `CreateLevelAsync(dto)`
    - `PUT {id}` → `UpdateLevelAsync(id, dto)`
    - `DELETE {id}` → `DeleteLevelAsync(id)`
    - Zero business logic
    - _Requirements: 8.1, 8.2, 8.5, 8.6_

  - [x] 5.3 Implement SettingGroupsController in sample app
    - Create `samples/GroundUp.Sample/Controllers/Settings/SettingGroupsController.cs`
    - `[ApiController]`, `[Route("api/settings/groups")]`
    - Constructor accepts `ISettingsAdminService`
    - `GET` → `GetAllGroupsAsync()`
    - `POST` → `CreateGroupAsync(dto)`
    - `PUT {id}` → `UpdateGroupAsync(id, dto)`
    - `DELETE {id}` → `DeleteGroupAsync(id)`
    - Zero business logic
    - _Requirements: 8.1, 8.3, 8.5, 8.6_

  - [x] 5.4 Implement SettingDefinitionsController in sample app
    - Create `samples/GroundUp.Sample/Controllers/Settings/SettingDefinitionsController.cs`
    - `[ApiController]`, `[Route("api/settings/definitions")]`
    - Constructor accepts `ISettingsAdminService`
    - `GET` → `GetAllDefinitionsAsync()`
    - `GET {id}` → `GetDefinitionByIdAsync(id)`
    - `POST` → `CreateDefinitionAsync(dto)`
    - `PUT {id}` → `UpdateDefinitionAsync(id, dto)`
    - `DELETE {id}` → `DeleteDefinitionAsync(id)`
    - Zero business logic
    - _Requirements: 8.1, 8.4, 8.5, 8.6_

  - [x] 5.5 Verify solution builds
    - Run `dotnet build groundup.sln` — zero errors
    - _Requirements: 14.1_

- [x] 6. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Sample app wiring, DI registration, and seeder
  - [x] 7.1 Update AddGroundUpSettings DI registration
    - Update `src/GroundUp.Services/Settings/SettingsServiceCollectionExtensions.cs`
    - Add optional `Action<SettingsCacheOptions>?` parameter
    - Register `IScopeChainProvider` as `DefaultScopeChainProvider` with `TryAddScoped` (overridable)
    - Register `ISettingsAdminService` as `SettingsAdminService` with scoped lifetime
    - Register `IMemoryCache` via `AddMemoryCache()`
    - Configure `SettingsCacheOptions` via the optional action
    - Register `SettingsCacheInvalidationHandler` as `IEventHandler<SettingChangedEvent>` with scoped lifetime
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6_

  - [x] 7.2 Implement DefaultSettingsSeeder
    - Create `samples/GroundUp.Sample/Data/DefaultSettingsSeeder.cs`
    - Implement `IDataSeeder`
    - Seed "System" level (root, no parent) and "Tenant" level (with "System" as parent)
    - Seed "DatabaseConnection" group with settings: Host (String, default "localhost"), Port (Int, default "5432"), Database (String, default "app")
    - Seed "MaxUploadSizeMB" definition (Int, default "50", allowed at System and Tenant)
    - Seed "AppTheme" definition (String, default "light", options: "light", "dark", "auto", allowed at System and Tenant)
    - Use check-before-insert logic for idempotency
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8_

  - [x] 7.3 Wire settings into sample app Program.cs
    - Add `AddGroundUpSettings()` call in Program.cs
    - Register `DefaultSettingsSeeder` as `IDataSeeder`
    - _Requirements: 12.1, 12.2_

  - [x] 7.4 Add EF migration for settings tables
    - Create migration in sample app that creates SettingLevels, SettingGroups, SettingDefinitions, SettingOptions, SettingValues, SettingDefinitionLevels tables
    - _Requirements: 12.3_

  - [x] 7.5 Verify solution builds and settings endpoints appear in Swagger
    - Run `dotnet build groundup.sln` — zero errors
    - Verify settings endpoints appear under Settings, SettingLevels, SettingGroups, SettingDefinitions controller groups
    - _Requirements: 12.4, 14.1, 14.2, 14.3_

- [x] 8. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Integration tests
  - [x] 9.1 Implement cascading resolution integration tests
    - Create `tests/GroundUp.Tests.Integration/Settings/SettingsCascadingResolutionTests.cs`
    - Seed System and Tenant levels
    - Seed a setting definition allowed at both levels
    - Test: set system-level value, verify tenant without override receives system value
    - Test: set tenant-level override, verify tenant receives override value
    - Test: verify different tenant (without override) still receives system value
    - Test: delete tenant override, verify tenant reverts to system value
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5, 15.6_

  - [x] 9.2 Implement settings admin CRUD integration tests
    - Create `tests/GroundUp.Tests.Integration/Settings/SettingsAdminCrudTests.cs`
    - Test: create a setting level via POST and verify response
    - Test: create a setting definition via POST and verify response includes options and allowed levels
    - Test: set a value via PUT and resolve it via GET
    - Test: update a setting level and verify changes persist
    - Test: delete a setting definition and verify it is removed
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5_

  - [x] 9.3 Implement cache invalidation integration tests
    - Create `tests/GroundUp.Tests.Integration/Settings/SettingsCacheInvalidationTests.cs`
    - Test: set value → resolve (populates cache) → update value → resolve again → returns updated value
    - Test: delete value override → resolve → falls back to system default or definition default
    - _Requirements: 17.1, 17.2_

  - [x] 9.4 Verify all tests pass and solution builds
    - Run `dotnet build groundup.sln` — zero errors
    - Run `dotnet test` — all tests pass
    - _Requirements: 14.1_

- [x] 10. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ]* 11. Property-based tests (optional)
  - [ ]* 11.1 Write property test for convenience overload equivalence
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceConvenienceOverloadPropertyTests.cs`
    - **Property 1: Convenience overloads produce identical results to explicit overloads**
    - **Validates: Requirements 3.4**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6c-settings-api, Property 1: Convenience overloads produce identical results to explicit overloads`

  - [ ]* 11.2 Write property test for scope chain hash determinism
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/ScopeChainHashPropertyTests.cs`
    - **Property 2: Scope chain hash determinism**
    - **Validates: Requirements 4.2**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6c-settings-api, Property 2: Scope chain hash determinism`

  - [ ]* 11.3 Write property test for cache read-through
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsCachePropertyTests.cs`
    - **Property 3: Cache read-through populates on miss and returns on hit**
    - **Validates: Requirements 4.3, 4.4**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6c-settings-api, Property 3: Cache read-through populates on miss and returns on hit`

  - [ ]* 11.4 Write property test for cache invalidation
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsCacheInvalidationPropertyTests.cs`
    - **Property 4: Cache invalidation clears stale entries on setting change**
    - **Validates: Requirements 4.7, 17.1, 17.2**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6c-settings-api, Property 4: Cache invalidation clears stale entries on setting change`

  - [ ]* 11.5 Write property test for definition CRUD options/levels
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/SettingsAdminServicePropertyTests.cs`
    - **Property 5: Definition CRUD preserves associated options and allowed levels**
    - **Validates: Requirements 6.5, 6.6**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6c-settings-api, Property 5: Definition CRUD preserves associated options and allowed levels`

  - [ ]* 11.6 Write property test for delete level with references rejected
    - In `tests/GroundUp.Tests.Unit/Services/Settings/SettingsAdminServicePropertyTests.cs`
    - **Property 6: Deleting a level with references is rejected**
    - **Validates: Requirements 6.7**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6c-settings-api, Property 6: Deleting a level with references is rejected`

  - [ ]* 11.7 Write property test for delete group orphans definitions
    - In `tests/GroundUp.Tests.Unit/Services/Settings/SettingsAdminServicePropertyTests.cs`
    - **Property 7: Deleting a group orphans its definitions**
    - **Validates: Requirements 6.8**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6c-settings-api, Property 7: Deleting a group orphans its definitions`

  - [ ]* 11.8 Write property test for cascading resolution
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/CascadingResolutionPropertyTests.cs`
    - **Property 8: Cascading resolution returns the most specific value with correct fallback**
    - **Validates: Requirements 15.3, 15.4, 15.5, 15.6**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6c-settings-api, Property 8: Cascading resolution returns the most specific value with correct fallback`

  - [ ]* 11.9 Write property test for seeder idempotency
    - Create `tests/GroundUp.Tests.Unit/Services/Settings/DefaultSettingsSeederPropertyTests.cs`
    - **Property 9: Settings seeder is idempotent**
    - **Validates: Requirements 11.7**
    - Use FsCheck.Xunit with minimum 100 iterations
    - Tag: `// Feature: phase6c-settings-api, Property 9: Settings seeder is idempotent`

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The SettingsController does NOT inherit from BaseController<T> — it has custom routes and DTOs
- Admin controllers live in the sample app, NOT in the framework (GroundUp.Api)
- The cache uses `ConcurrentDictionary<string, byte>` for key tracking to enable targeted invalidation
- `TryAddScoped` is used for IScopeChainProvider so consuming apps can override it
- Integration tests use CustomWebApplicationFactory with Testcontainers Postgres
- The seeder uses check-before-insert logic (not deterministic GUIDs) for idempotency
