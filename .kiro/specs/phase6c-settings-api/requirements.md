# Requirements Document

## Introduction

Phase 6C completes the GroundUp settings module by adding the caching layer, API controllers, scope chain provider, settings admin service, and sample app wiring. Where Phase 6A delivered the data model (entities, DTOs, EF configurations) and Phase 6B delivered the cascading resolution service (`ISettingsService` with `GetAsync`, `SetAsync`, `GetAllForScopeAsync`, `GetGroupAsync`, `DeleteValueAsync`), Phase 6C makes the settings module consumable by real applications.

Phase 6C delivers seven capabilities:

1. **IScopeChainProvider** — an abstraction that builds a scope chain from the current request context, so controllers and convenience overloads don't need to manually construct scope chains.
2. **Convenience overloads on ISettingsService** — parameterless versions of `GetAsync<T>`, `GetAllForScopeAsync`, and `GetGroupAsync` that use `IScopeChainProvider` internally, keeping the explicit scope chain overloads for admin scenarios.
3. **In-memory settings cache** — wraps resolved settings with `IMemoryCache`, keyed by setting key + scope chain hash, invalidated via `SettingChangedEvent` subscription through `IEventBus`, with configurable TTL (default 15 minutes).
4. **ISettingsAdminService** — a separate service for CRUD operations on `SettingLevel`, `SettingGroup`, and `SettingDefinition` (including managing options and allowed levels), keeping admin logic out of the resolution-focused `ISettingsService`.
5. **Settings API controllers** — four custom controllers (`SettingLevelsController`, `SettingGroupsController`, `SettingDefinitionsController`, `SettingsController`) that are thin HTTP adapters over the service layer, using custom routes under `/api/settings/`.
6. **Sample app wiring** — `AddGroundUpSettings()` registration in `Program.cs`, a `DefaultSettingsSeeder` that seeds example levels, groups, and definitions, an EF migration for settings tables, and Swagger verification.
7. **Integration tests** — end-to-end cascading resolution, admin CRUD, and cache invalidation tests.

Phase 6C does NOT include integration tests against real Postgres with Testcontainers (Phase 6D), advanced caching strategies (distributed cache), or FluentValidation validators for admin DTOs (can be added later).

## Glossary

- **IScopeChainProvider**: An interface in `GroundUp.Core/Abstractions` that builds a scope chain (`IReadOnlyList<SettingScopeEntry>`) from the current request context. Consuming applications override the default implementation for complex hierarchies.
- **DefaultScopeChainProvider**: The default implementation of `IScopeChainProvider` in `GroundUp.Services` that builds a single-entry scope chain from `ITenantContext` (tenant level + tenant ID).
- **ISettingsAdminService**: A service interface in `GroundUp.Core/Abstractions` for CRUD operations on settings metadata entities (`SettingLevel`, `SettingGroup`, `SettingDefinition`). Separate from `ISettingsService` which focuses on resolution.
- **SettingsAdminService**: The concrete implementation of `ISettingsAdminService` in `GroundUp.Services/Settings` that uses EF Core directly via `GroundUpDbContext`.
- **SettingsCacheInvalidationHandler**: An `IEventHandler<SettingChangedEvent>` that clears relevant cache entries when settings change.
- **SettingLevelsController**: API controller for CRUD operations on cascade levels, routed at `/api/settings/levels`.
- **SettingGroupsController**: API controller for CRUD operations on setting groups, routed at `/api/settings/groups`.
- **SettingDefinitionsController**: API controller for CRUD operations on setting definitions, routed at `/api/settings/definitions`.
- **SettingsController**: Consumer-facing API controller for resolving and setting values, routed at `/api/settings`.
- **DefaultSettingsSeeder**: An `IDataSeeder` implementation in the sample app that seeds example setting levels, groups, and definitions on startup.
- **IMemoryCache**: The `Microsoft.Extensions.Caching.Memory.IMemoryCache` interface used for in-memory caching of resolved settings.
- **SettingChangedEvent**: The domain event (from Phase 6B) published when a setting value is created, updated, or deleted. Phase 6C subscribes to this event for cache invalidation.
- **Scope_Chain_Hash**: A deterministic hash computed from the ordered list of `SettingScopeEntry` records in a scope chain, used as part of the cache key to distinguish cached values for different scope contexts.

## Requirements

### Requirement 1: IScopeChainProvider Interface

**User Story:** As a consuming application developer, I want an abstraction that builds a scope chain from the current request context, so that controllers and convenience methods can resolve settings without manually constructing scope chains.

#### Acceptance Criteria

1. THE IScopeChainProvider interface SHALL reside in `src/GroundUp.Core/Abstractions/IScopeChainProvider.cs`.
2. THE IScopeChainProvider interface SHALL declare a `GetScopeChainAsync` method accepting a `CancellationToken`, returning `Task<IReadOnlyList<SettingScopeEntry>>`.
3. THE IScopeChainProvider interface SHALL have XML doc comments on the interface and the method describing the contract and expected ordering (most specific to least specific).

### Requirement 2: DefaultScopeChainProvider Implementation

**User Story:** As a consuming application developer, I want a default scope chain provider that builds a simple tenant-level scope chain from `ITenantContext`, so that the settings module works out of the box for single-level tenant scenarios without requiring custom implementation.

#### Acceptance Criteria

1. THE DefaultScopeChainProvider class SHALL reside in `src/GroundUp.Services/Settings/DefaultScopeChainProvider.cs` and implement `IScopeChainProvider`.
2. THE DefaultScopeChainProvider class SHALL accept `ITenantContext` and `GroundUpDbContext` as constructor dependencies.
3. WHEN `GetScopeChainAsync` is called and `ITenantContext.TenantId` is not `Guid.Empty`, THE DefaultScopeChainProvider SHALL query the database for a `SettingLevel` named "Tenant" and return a single-entry scope chain containing that level's ID and the tenant ID from `ITenantContext`.
4. WHEN `GetScopeChainAsync` is called and `ITenantContext.TenantId` is `Guid.Empty`, THE DefaultScopeChainProvider SHALL return an empty scope chain (falling back to definition defaults).
5. WHEN the "Tenant" level does not exist in the database, THE DefaultScopeChainProvider SHALL return an empty scope chain.
6. THE DefaultScopeChainProvider class SHALL be a sealed class with XML doc comments.
7. THE DefaultScopeChainProvider class SHALL be registered as scoped in the DI container.

### Requirement 3: Convenience Overloads on ISettingsService

**User Story:** As a consuming application developer, I want overloads on `ISettingsService` that resolve settings using the scope chain provider automatically, so that I can call `GetAsync<T>(key)` without manually building a scope chain for the common case.

#### Acceptance Criteria

1. THE ISettingsService interface SHALL declare a `GetAsync<T>` overload accepting only `string key` and `CancellationToken`, returning `Task<OperationResult<T>>`.
2. THE ISettingsService interface SHALL declare a `GetAllForScopeAsync` overload accepting only `CancellationToken`, returning `Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>>`.
3. THE ISettingsService interface SHALL declare a `GetGroupAsync` overload accepting `string groupKey` and `CancellationToken`, returning `Task<OperationResult<IReadOnlyList<ResolvedSettingDto>>>`.
4. THE SettingsService implementation SHALL resolve the scope chain from `IScopeChainProvider.GetScopeChainAsync` and delegate to the existing explicit scope chain overloads.
5. THE SettingsService constructor SHALL accept `IScopeChainProvider` as an additional dependency.
6. THE explicit scope chain overloads SHALL remain unchanged for admin scenarios where the caller provides a custom scope chain.

### Requirement 4: In-Memory Settings Cache

**User Story:** As a consuming application developer, I want resolved settings to be cached in memory to avoid repeated database queries, so that high-frequency settings reads perform well without hitting the database on every request.

#### Acceptance Criteria

1. THE settings cache SHALL use `IMemoryCache` from `Microsoft.Extensions.Caching.Memory`.
2. THE cache key SHALL be a combination of the setting key (or operation identifier for bulk reads) and a deterministic hash of the scope chain entries.
3. WHEN `GetAsync<T>` is called with a key and scope chain, THE SettingsService SHALL check the cache first and return the cached value if present.
4. WHEN the cache does not contain the requested value, THE SettingsService SHALL resolve the value from the database, store it in the cache with the configured TTL, and return the result.
5. THE cache TTL SHALL be configurable, with a default of 15 minutes.
6. THE cache configuration SHALL be provided via an options class (e.g., `SettingsCacheOptions`) that can be configured through `IServiceCollection`.
7. WHEN a `SettingChangedEvent` is received, THE SettingsCacheInvalidationHandler SHALL clear the cache entry for the changed setting key across all scope chain variations.
8. THE SettingsCacheInvalidationHandler SHALL implement `IEventHandler<SettingChangedEvent>` and be registered in the DI container.
9. THE cache SHALL also apply to `GetAllForScopeAsync` and `GetGroupAsync` operations, keyed by the operation type and scope chain hash.
10. THE `SetAsync` and `DeleteValueAsync` methods SHALL NOT be cached — only read operations use the cache.

### Requirement 5: ISettingsAdminService Interface

**User Story:** As a consuming application developer, I want a dedicated admin service for CRUD operations on settings metadata (levels, groups, definitions), so that admin operations are cleanly separated from the resolution-focused `ISettingsService`.

#### Acceptance Criteria

1. THE ISettingsAdminService interface SHALL reside in `src/GroundUp.Core/Abstractions/ISettingsAdminService.cs`.
2. THE ISettingsAdminService interface SHALL declare CRUD methods for `SettingLevel`: `GetAllLevelsAsync`, `CreateLevelAsync`, `UpdateLevelAsync`, `DeleteLevelAsync`.
3. THE ISettingsAdminService interface SHALL declare CRUD methods for `SettingGroup`: `GetAllGroupsAsync`, `CreateGroupAsync`, `UpdateGroupAsync`, `DeleteGroupAsync`.
4. THE ISettingsAdminService interface SHALL declare CRUD methods for `SettingDefinition`: `GetAllDefinitionsAsync`, `GetDefinitionByIdAsync`, `CreateDefinitionAsync`, `UpdateDefinitionAsync`, `DeleteDefinitionAsync`.
5. ALL methods on ISettingsAdminService SHALL return `OperationResult<T>` or `OperationResult` and accept a `CancellationToken cancellationToken = default` parameter.
6. THE ISettingsAdminService interface SHALL have XML doc comments on the interface and each method.

### Requirement 6: SettingsAdminService Implementation

**User Story:** As a framework developer, I want a concrete admin service that performs CRUD operations on settings metadata entities using EF Core directly, so that admin controllers have a proper service layer to delegate to.

#### Acceptance Criteria

1. THE SettingsAdminService class SHALL reside in `src/GroundUp.Services/Settings/SettingsAdminService.cs` and implement `ISettingsAdminService`.
2. THE SettingsAdminService class SHALL accept `GroundUpDbContext` as a constructor dependency.
3. THE SettingsAdminService class SHALL use EF Core directly (via `DbContext.Set<T>()`) for querying and persisting settings metadata entities.
4. THE SettingsAdminService class SHALL be a sealed class with XML doc comments.
5. WHEN creating a `SettingDefinition`, THE SettingsAdminService SHALL also persist the associated `SettingOption` records and `SettingDefinitionLevel` records provided in the request.
6. WHEN updating a `SettingDefinition`, THE SettingsAdminService SHALL replace the associated `SettingOption` and `SettingDefinitionLevel` collections with the values provided in the request.
7. WHEN deleting a `SettingLevel` that has child levels or is referenced by `SettingValue` records, THE SettingsAdminService SHALL return `OperationResult.BadRequest` with a descriptive message instead of allowing a database constraint violation.
8. WHEN deleting a `SettingGroup`, THE SettingsAdminService SHALL orphan the definitions (set `GroupId` to null) rather than deleting them, matching the `SetNull` delete behavior configured in EF.
9. ALL read queries in SettingsAdminService SHALL use `AsNoTracking()`.
10. WHEN `GetAllDefinitionsAsync` is called, THE SettingsAdminService SHALL include the associated `SettingOption` records in the response.

### Requirement 7: SettingsController (Consumer-Facing, in GroundUp.Api)

**User Story:** As a consuming application developer, I want API endpoints for resolving effective settings and setting overrides, so that front-end applications can read and write settings through a REST API.

#### Acceptance Criteria

1. THE SettingsController SHALL reside in `src/GroundUp.Api/Controllers/Settings/SettingsController.cs`.
2. THE SettingsController SHALL be annotated with `[ApiController]` and `[Route("api/settings")]`.
3. THE SettingsController SHALL NOT extend `BaseController<T>` — it is a custom controller inheriting from `ControllerBase`.
4. THE SettingsController SHALL accept `ISettingsService` as a constructor dependency.
5. THE SettingsController SHALL expose `GET /api/settings/{key}` to resolve the effective value for a single setting using the scope chain from `IScopeChainProvider` (via the convenience overload).
6. THE SettingsController SHALL expose `GET /api/settings` to get all effective settings using the scope chain from `IScopeChainProvider` (via the convenience overload).
7. THE SettingsController SHALL expose `GET /api/settings/groups/{groupKey}` to get group settings using the scope chain from `IScopeChainProvider` (via the convenience overload).
8. THE SettingsController SHALL expose `PUT /api/settings/{key}` to set a value at a specific level and scope, with `levelId` and `scopeId` provided in the request body.
9. THE SettingsController SHALL expose `DELETE /api/settings/values/{id}` to delete a value override.
10. THE SettingsController SHALL contain zero business logic.
11. WHEN `GET /api/settings/{key}` resolves a value, THE SettingsController SHALL return the effective value as a string in the response.

### Requirement 8: Settings Admin Controllers (in Sample App, NOT in Framework)

**User Story:** As a sample app developer, I want admin CRUD endpoints for managing settings levels, groups, and definitions, so that I can test and demonstrate the settings admin service through Swagger during development.

#### Acceptance Criteria

1. THE admin CRUD controllers for levels, groups, and definitions SHALL reside in the sample app (`samples/GroundUp.Sample/Controllers/Settings/`), NOT in the framework (`GroundUp.Api`).
2. THE sample app SHALL include `SettingLevelsController` at `api/settings/levels` with GET (list), POST (create), PUT (update), DELETE endpoints.
3. THE sample app SHALL include `SettingGroupsController` at `api/settings/groups` with GET (list), POST (create), PUT (update), DELETE endpoints.
4. THE sample app SHALL include `SettingDefinitionsController` at `api/settings/definitions` with GET (list), GET (by id), POST (create), PUT (update), DELETE endpoints.
5. ALL admin controllers SHALL accept `ISettingsAdminService` as a constructor dependency and contain zero business logic.
6. THE framework (`GroundUp.Api`) SHALL NOT include admin CRUD controllers — consuming applications decide whether to expose admin endpoints based on their needs.

### Requirement 9: Settings DI Registration Updates

**User Story:** As a consuming application developer, I want the `AddGroundUpSettings` extension method to register all Phase 6C services (scope chain provider, admin service, cache, event handler), so that a single registration call enables the complete settings module.

#### Acceptance Criteria

1. THE `AddGroundUpSettings` method SHALL register `IScopeChainProvider` as `DefaultScopeChainProvider` with scoped lifetime.
2. THE `AddGroundUpSettings` method SHALL register `ISettingsAdminService` as `SettingsAdminService` with scoped lifetime.
3. THE `AddGroundUpSettings` method SHALL register `IMemoryCache` via `AddMemoryCache()` if not already registered.
4. THE `AddGroundUpSettings` method SHALL register `SettingsCacheInvalidationHandler` as `IEventHandler<SettingChangedEvent>` with scoped lifetime.
5. THE `AddGroundUpSettings` method SHALL accept an optional `Action<SettingsCacheOptions>` parameter for configuring cache TTL.
6. THE `AddGroundUpSettings` method SHALL allow consuming applications to override `IScopeChainProvider` by registering their own implementation after calling `AddGroundUpSettings`.

### Requirement 10: SettingsCacheOptions

**User Story:** As a consuming application developer, I want to configure the settings cache TTL, so that I can tune cache behavior for my application's needs.

#### Acceptance Criteria

1. THE SettingsCacheOptions class SHALL reside in `src/GroundUp.Core/Models/SettingsCacheOptions.cs`.
2. THE SettingsCacheOptions class SHALL have a `CacheDuration` property of type `TimeSpan` with a default value of 15 minutes.
3. THE SettingsCacheOptions class SHALL have XML doc comments.

### Requirement 11: DefaultSettingsSeeder

**User Story:** As a sample app developer, I want example settings seeded on startup, so that I can immediately test the settings API endpoints without manual data setup.

#### Acceptance Criteria

1. THE DefaultSettingsSeeder class SHALL reside in `samples/GroundUp.Sample/Data/DefaultSettingsSeeder.cs` and implement `IDataSeeder`.
2. THE DefaultSettingsSeeder SHALL seed two `SettingLevel` records: "System" (root, no parent) and "Tenant" (with "System" as parent).
3. THE DefaultSettingsSeeder SHALL seed a `SettingGroup` with key "DatabaseConnection" containing settings for Host, Port, and Database.
4. THE DefaultSettingsSeeder SHALL seed a `SettingDefinition` with key "MaxUploadSizeMB" of type `Int` with default value "50", allowed at both System and Tenant levels.
5. THE DefaultSettingsSeeder SHALL seed a `SettingDefinition` with key "AppTheme" of type `String` with default value "light" and options "light", "dark", "auto", allowed at both System and Tenant levels.
6. THE DefaultSettingsSeeder SHALL seed `SettingDefinition` records for the "DatabaseConnection" group: "DatabaseConnection.Host" (String, default "localhost"), "DatabaseConnection.Port" (Int, default "5432"), "DatabaseConnection.Database" (String, default "app").
7. THE DefaultSettingsSeeder SHALL be idempotent — running multiple times produces the same result.
8. THE DefaultSettingsSeeder SHALL use deterministic GUIDs or check-before-insert logic to ensure idempotency.

### Requirement 12: Sample App Wiring

**User Story:** As a sample app developer, I want the settings module fully wired into the sample application, so that I can test all settings endpoints through Swagger.

#### Acceptance Criteria

1. THE sample app `Program.cs` SHALL call `AddGroundUpSettings()` to register the settings module.
2. THE sample app SHALL register `DefaultSettingsSeeder` as an `IDataSeeder` implementation.
3. THE sample app SHALL have an EF migration that creates the settings tables (SettingLevels, SettingGroups, SettingDefinitions, SettingOptions, SettingValues, SettingDefinitionLevels).
4. WHEN the sample app starts and Swagger is opened, THE settings endpoints SHALL appear under the Settings, SettingLevels, SettingGroups, and SettingDefinitions controller groups.

### Requirement 13: Request DTOs for Write Operations

**User Story:** As an API consumer, I want well-defined request DTOs for creating and updating settings entities, so that the API contract is clear and separate from the response DTOs.

#### Acceptance Criteria

1. THE settings module SHALL define request DTOs for create and update operations: `CreateSettingLevelDto`, `UpdateSettingLevelDto`, `CreateSettingGroupDto`, `UpdateSettingGroupDto`, `CreateSettingDefinitionDto`, `UpdateSettingDefinitionDto`, `SetSettingValueDto`.
2. THE request DTOs SHALL reside in `src/GroundUp.Core/Dtos/Settings/`.
3. THE `SetSettingValueDto` SHALL contain `string Value`, `Guid LevelId`, and `Guid? ScopeId` properties for the `PUT /api/settings/{key}` endpoint.
4. THE `CreateSettingDefinitionDto` SHALL include collections for options and allowed level IDs, so that a definition can be created with its full configuration in a single request.
5. ALL request DTOs SHALL be record types with XML doc comments.

### Requirement 14: Solution Compilation

**User Story:** As a framework developer, I want the entire solution to compile with zero errors after adding all Phase 6C types, so that the settings module is fully functional end-to-end.

#### Acceptance Criteria

1. WHEN `dotnet build groundup.sln` is executed, THE build SHALL complete with zero errors.
2. THE Phase 6C types SHALL follow the existing project conventions: file-scoped namespaces, sealed classes where appropriate, XML doc comments on all public types and members, one class per file.
3. THE Phase 6C types SHALL follow the framework's dependency rules: interfaces in `GroundUp.Core`, implementations in `GroundUp.Services`, controllers in `GroundUp.Api`, sample-specific code in `GroundUp.Sample`.

### Requirement 15: Integration Tests — Cascading Resolution End-to-End

**User Story:** As a framework developer, I want integration tests that verify cascading resolution works end-to-end through the API, so that I can confirm the complete settings pipeline (controller → service → cache → database) functions correctly.

#### Acceptance Criteria

1. THE integration tests SHALL seed a System level and a Tenant level (with System as parent).
2. THE integration tests SHALL seed a setting definition allowed at both levels.
3. THE integration tests SHALL set a system-level value, then verify that a tenant without an override receives the system value.
4. THE integration tests SHALL set a tenant-level override, then verify that the tenant receives the override value.
5. THE integration tests SHALL verify that a different tenant (without an override) still receives the system-level value.
6. THE integration tests SHALL delete the tenant override and verify the tenant reverts to the system value.

### Requirement 16: Integration Tests — Settings Admin CRUD

**User Story:** As a framework developer, I want integration tests that verify the admin CRUD operations work end-to-end, so that I can confirm levels, groups, and definitions can be managed through the API.

#### Acceptance Criteria

1. THE integration tests SHALL create a setting level via `POST /api/settings/levels` and verify the response.
2. THE integration tests SHALL create a setting definition via `POST /api/settings/definitions` and verify the response includes options and allowed levels.
3. THE integration tests SHALL set a value via `PUT /api/settings/{key}` and resolve it via `GET /api/settings/{key}`.
4. THE integration tests SHALL update a setting level and verify the changes persist.
5. THE integration tests SHALL delete a setting definition and verify it is removed.

### Requirement 17: Integration Tests — Cache Invalidation

**User Story:** As a framework developer, I want integration tests that verify cache invalidation works correctly, so that I can confirm that updating a setting value causes subsequent reads to return the new value rather than a stale cached value.

#### Acceptance Criteria

1. THE integration tests SHALL set a value, resolve it (populating the cache), update the value, and resolve again — verifying the second resolution returns the updated value.
2. THE integration tests SHALL delete a value override, resolve the setting, and verify the resolution falls back to the system default or definition default.
