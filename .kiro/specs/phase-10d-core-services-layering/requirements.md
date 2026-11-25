# Requirements: Core Services Layering (GU-82)

## Context

`GroundUp.Services` references `GroundUp.Data.Postgres`. Seven files use `GroundUpDbContext` and EF Core directly: the settings services (`SettingsService`, `SettingsAdminService`, `DefaultScopeChainProvider`, `DefaultSettingLevelSeeder`), `BootstrapStateService`, `MigrationStartupHostedService` and `SetupWizardService`. GU-81 allowlisted this in the architecture tests.

The Phase 6B design chose `DbContext.Set<T>()` over `IBaseRepository<T>` because the settings queries (scope chain walks, batch resolution, composite key lookups) don't fit generic CRUD. That argues against the generic repository, not against dedicated ones. GU-81 established the pattern: a dedicated repository taking `DbContext`, registered with a factory lambda by the provider.

The benefit is structural. Services become free of EF and Postgres, a future `Data.SqlServer` provider becomes possible, and the layer rules hold with no exceptions. SDK callers are not affected.

## Scope

**In scope:** the 7 files above, the repositories and DTO changes they need, DI registration, test fixtures, and the `AuthDbContextUnitOfWork` unique-violation check.

**Out of scope:** new settings features. The API gains two read-only fields (Requirement 5) and no other changes.

**Delivery:** two PRs. Part A (Requirements 1-3) covers infrastructure, bootstrap and setup. Part B (Requirements 4-7) covers settings and the reference removal.

## Requirements

### Requirement 1: Repository conventions

1. Repository interfaces SHALL live in `GroundUp.Data.Abstractions`. Implementations SHALL live in `GroundUp.Repositories`, which MAY reference `Microsoft.EntityFrameworkCore.Relational` (for transactions).
2. Repositories SHALL take and return DTOs or primitives, never entities. Entity ↔ DTO mapping SHALL happen in the repositories using a Mapperly mapper (decided).
3. `AddGroundUpPostgres<TContext>` SHALL register every repository in this story with a factory lambda over `GroundUpDbContext`. It SHALL register `IUniqueConstraintDetector` with TryAdd, so it no longer depends on the auth module being added.
4. Integration tests built on `GroundUpWebApplicationFactory` SHALL get the same registrations as production. They already do: the factory swaps only the context options, and the app's `AddGroundUpPostgres` registers the rest.
5. Unique-constraint violations SHALL be detected only through `IUniqueConstraintDetector`. After a violation the repository SHALL clear the change tracker before any further query.

### Requirement 2: Migrations

1. A new `IDatabaseMigrator` interface (in `Data.Abstractions`) SHALL apply pending migrations. Its implementation in `Data.Postgres` SHALL call `Database.MigrateAsync` on `GroundUpDbContext`.
2. `MigrationStartupHostedService` SHALL stay in `GroundUp.Services` and call `IDatabaseMigrator`. Its registration in `AddGroundUpBootstrap` and its order (before `BootstrapTokenStartupValidator`) SHALL not change.

### Requirement 3: Bootstrap state and setup log

1. `BootstrapStateService` SHALL use an `IBootstrapStateRepository`. Behavior SHALL not change:
   - the 60-second cache
   - a missing sentinel row throws `InvalidOperationException` from `IsCompleteAsync`
   - `CompleteSetupAsync` returns 503 when the row is missing and 409 `CONFLICT` when the setup is already complete
   - a concurrent completion (optimistic concurrency on the row) returns 409 `CONFLICT` and logs a warning
   - the cache is invalidated after completion
2. `SetupWizardService` SHALL use an `ISetupTransactionLogRepository` for:
   - insert with rotation in one transaction, where pending rows (`keycloak-pending`, `db-pending`) are never rotated out and a warning is logged when fewer rows than needed could be removed
   - stage updates by id (stage, external user id, error message), replacing the change-tracked edits
   - the pending first-admin check used by `CompleteSetupAsync`
   - the latest pending row used by `GetStatusAsync`
   - the 20 most recent rows used by `GetTransactionLogAsync`
   - find by id used by `RecoverAsync`
3. `SetupWizardService.GetSystemLevelIdAsync` SHALL get the System level through the settings repository (Requirement 4). A missing level SHALL still throw `InvalidOperationException("System setting level not found.")`.
4. All setup wizard responses, stages and log messages SHALL stay the same.

### Requirement 4: Settings repositories

1. `SettingsService`, `DefaultScopeChainProvider` and `DefaultSettingLevelSeeder` SHALL use an `ISettingsRepository` that provides:
   - a definition by key, with allowed levels and options
   - a level by name, and a level name by id
   - ensure a level exists (name, parent, display order) and return it
   - a value by definition, level and scope
   - all definitions with values for a set of definition ids
   - a group by key, and the definitions in a group
   - upsert a value (insert or update by definition, level and scope) and return the saved value
   - delete a value by id and return its definition key
   - ensure a definition exists (Requirement 6)
2. `SettingsAdminService` SHALL use an `ISettingsAdminRepository` for level, group and definition CRUD. The repository SHALL build the entity graph from the existing Create/Update DTOs, including options and allowed levels. The service SHALL keep the duplicate checks, the delete guards (child levels, values in use) and validation.
3. A concurrent create that hits a unique constraint SHALL return 409 `CONFLICT` instead of an unhandled exception.
4. The scope chain resolution SHALL keep its query pattern: one value lookup per scope entry for single-key resolution, one bulk value query for all-for-scope and group resolution.
5. These stay in the services: authorization, validation (including `ISettingWriteValidator`), encryption and decryption, `secretref://` resolution, caching, `SettingChangedEvent` publishing and `SettingValueConverter`.
6. When two `SetAsync` calls insert the same (definition, level, scope) at once, the second SHALL be applied as an update instead of failing.

### Requirement 5: Setting definition DTO

1. `SettingDefinitionDto` SHALL gain `AllowedLevelIds` (`IReadOnlyList<Guid>`) and `Options` (`IReadOnlyList<SettingOptionDto>`), named to match `CreateSettingDefinitionDto` (decided).
2. Every endpoint and service method that returns `SettingDefinitionDto` SHALL fill both fields. A definition with none SHALL return empty lists, never null.
3. The API change is additive. Callers that construct the record need the two new arguments. This SHALL be noted in the PR description.

### Requirement 6: Ensure definition

1. `EnsureDefinitionAsync` SHALL keep its result: an existing definition is returned as is; otherwise the group (if missing), the definition and its allowed levels are created in one transaction.
2. When the transaction hits a unique-constraint violation (a concurrent create), the repository SHALL roll back, clear the change tracker, re-read the definition and return it. If the re-read finds nothing it SHALL return 500 as today.
3. Any other database error SHALL propagate instead of triggering the re-read (decided; today every `DbUpdateException` triggers it).

### Requirement 7: Layer rule and cleanup

1. The project reference `GroundUp.Services` → `GroundUp.Data.Postgres` SHALL be removed. `GroundUp.Services` SHALL use no EF Core or Npgsql assembly.
2. The GU-82 allowlist entries SHALL be removed as each stops occurring, leaving the allowlist empty.
3. `AuthDbContextUnitOfWork` SHALL detect unique violations through `IUniqueConstraintDetector` instead of its own SQL state check (decided).
4. Projects that need `GroundUp.Data.Postgres` (sample, tests) SHALL reference it themselves.

### Requirement 8: Tests and documentation

1. Unit tests SHALL cover each new repository. They SHALL include a mapping test showing that `SettingDefinitionDto` carries the same values as the old hand-written mapping plus the two new fields.
2. Existing service tests SHALL pass with fixture and constructor changes only. Fixtures SHALL build the repository and the service in one place (`SettingsTestFixture`).
3. Integration tests against real Postgres SHALL cover:
   - setup log rotation that keeps pending rows
   - a concurrent `EnsureDefinitionAsync` for the same key, which returns the same definition
   - the bootstrap concurrent completion (the existing `BootstrapConcurrencyTests` stay green)
   - the two new `SettingDefinitionDto` fields in the admin API response
4. Baselines SHALL not get worse: unit 1698 pass / 12 known failures, integration 365 / 48 known, sample 22/22.
5. ARCHITECTURE.md SHALL drop the "Open exception" note. CLAUDE.md and the steering rules SHALL be updated if they mention the exception.
