# Tasks: Core Services Layering (GU-82)

Execute one top-level task at a time. After each one: build, run the tests against the baselines, commit, and stop for review.

Baselines: unit 1698 pass / 12 known failures (plus 1 flaky property test, GU-73); integration 365 pass / 48 known failures; sample 22/22.

## PR 1: Part A (infrastructure, bootstrap, setup)

- [x] 1. Pinning tests and foundations
  - [x] 1.1 `EnsureDefinitionConcurrencyTests` (integration, own container), green against today's code: 8 parallel callers on one key all get the same id, with one definition, group and level link; an existing key is returned unchanged. The rotation logic is private to `SetupWizardService` today, so its integration test is written against the repository in 2.3
  - [x] 1.2 Add `Microsoft.EntityFrameworkCore.Relational` 8.0.* to `GroundUp.Repositories`
  - [x] 1.3 `IDatabaseMigrator` (Abstractions) and `PostgresDatabaseMigrator` (Data.Postgres). `MigrationStartupHostedService` calls the migrator, with order and logs unchanged. Update its unit test
  - [x] 1.4 `AddGroundUpPostgres` registers the migrator and `IUniqueConstraintDetector` (TryAdd). `GroundUpWebApplicationFactory` needs no change: it swaps only the context options, and the app's `AddGroundUpPostgres` does the rest
  - [x] 1.5 Build, run the baselines, commit. The `Services` → EF Relational allowlist pair no longer occurs (`MigrateAsync` was its only use) and is removed. Unit 1700 / 12 known; integration 367 / 48; sample 22/22

- [x] 2. Bootstrap state and setup log repositories
  - [x] 2.1 `IBootstrapStateRepository`, `BootstrapCompletionOutcome` and `BootstrapStateRepository`. `BootstrapStateService` uses them, with the same results, messages, log and cache behavior
  - [x] 2.2 `ISetupTransactionLogRepository`, `CreateSetupTransactionLogDto` and `SetupTransactionLogRepository` (rotation with the same warning, stage updates, pending queries, recent rows, by id). `SetupWizardService` uses them for every `SetupTransactionLogs` access
  - [x] 2.3 Register both repositories. Write repository unit tests: completion outcomes including a concurrent update, rotation boundaries (limit 0, pending rows protected), and partial stage updates. Add an integration test on Postgres: rotation keeps pending rows and removes the oldest finished rows. The existing rotation property tests copy the algorithm instead of calling it, so they move to the real repository
  - [x] 2.4 Update the existing bootstrap and setup unit tests (constructor and fixture changes only). `BootstrapConcurrencyTests` and the 2.3 rotation test stay green. Commit
  - Results: unit 1712 / 12 known; integration 369 / 48 known; sample 22/22. The bootstrap outcomes (missing, already complete, concurrent update) are covered by the existing service tests, which now run through the real repository

- [x] 3. Level lookups
  - [x] 3.1 `ISettingsRepository` with `GetLevelIdByNameAsync`, `GetLevelNameByIdAsync` and `EnsureLevelAsync`, and `SettingsRepository` (level part). Register it
  - [x] 3.2 Switch `DefaultScopeChainProvider`, `DefaultSettingLevelSeeder` and `SetupWizardService.GetSystemLevelIdAsync` to the repository, keeping the same "System setting level not found." exception
  - [x] 3.3 Repository unit tests. Update `DefaultScopeChainProviderTests`, `DefaultSettingLevelSeederTests` and the integration tests that construct the seeder (`SessionPolicyIntegrationTests`, `SettingsAuthorizationIntegrationTests`)
  - [x] 3.4 Build, run the baselines, commit. **PR 1 boundary:** I give you the compare URL, title and description
  - Results: unit 1718 / 12 known; integration 369 / 48 known; sample 22/22. Finding: `SettingLevels.Name` has no unique index, so two instances seeding at once could both create "System"; out of scope here (needs a migration), raised as a follow-up

## PR 2: Part B (settings)

- [x] 4. Setting definition DTO and mapper
  - [x] 4.1 Add `AllowedLevelIds` and `Options` to the end of `SettingDefinitionDto`, with XML docs
  - [x] 4.2 Add per-entity Mapperly mappers (`SettingDefinitionMapper` with options, `SettingGroupMapper`, `SettingValueMapper`; `SettingLevelMapper` exists) in `GroundUp.Repositories/Mappers`. RMG012 (DTO member without a source) is a build error in that project. Options are sorted by `DisplayOrder`, then `Value`
  - [x] 4.3 Mapper unit test: every field of a fully populated definition, the two new lists, and empty lists when there are none
  - [x] 4.4 Fix the 2 test files that construct the DTO. The two services' hand-written `MapToDto` methods stay until task groups 5 and 6, filling the new fields from whatever collections were loaded. Commit
  - Results: unit 1723 / 12 known; integration 369 / 48 known; sample 22/22

- [x] 5. Settings runtime repository
  - [x] 5.1 `ISettingsRepository` runtime methods, `SettingValueUpsertResult`, `SettingValueDeleteResult`
  - [x] 5.2 `SettingsRepository` runtime part:
    - reads with `AsSplitQuery`
    - upsert with the concurrent-insert retry (Requirement 4.6)
    - find and delete value
    - `EnsureDefinitionAsync` transaction with the detector, rollback and `ChangeTracker.Clear()` (Requirement 6)
  - [x] 5.3 `SettingsService` on the repository. Authorization, validation, encryption, secrets, cache and events are unchanged. Remove `MapToDto`, and change `ResolveEffectiveValue` to work on DTOs
  - [x] 5.4 `SettingsTestFixture` builds the repository. Update the other constructors (`SettingsCacheTests`, the secret resolver tests, `SettingsServiceAuthorizationTests`, `SettingsEncryptionIntegrationTests`, and the `ActivatorUtilities` callers)
  - [x] 5.5 Repository unit tests: upsert insert and update, retry on conflict, ensure-definition race (substituted detector), non-unique error propagates. The 1.1 concurrency test stays green. Commit
  - Results: unit 1735 / 12 known; integration 369 / 48 known; sample 22/22. `SettingsRepository` takes `IUniqueConstraintDetector`; unit tests use a SQLite detector (`SqliteUniqueConstraintDetector`, code 2067), integration tests the Postgres one. Delete re-checks existence after authorization (404 if removed meanwhile). Finding: the `(definition, level, scope)` unique index treats a null scope as distinct in Postgres, so concurrent System-level inserts are not caught by it (the retry covers tenant scope); needs `NULLS NOT DISTINCT` in a migration, raised as a follow-up

- [ ] 6. Settings admin repository
  - [ ] 6.1 `ISettingsAdminRepository` and `SettingsAdminRepository`: levels, groups (delete orphans definitions), and definitions (full replace of options and levels). NotFound messages are moved verbatim, and unique violations return 409 `CONFLICT`
  - [ ] 6.2 `SettingsAdminService` on the repository. It keeps the duplicate checks, the delete guards and their messages. Remove the hand-written mappers
  - [ ] 6.3 Register the repository. Update the 5 `SettingsAdminService*Tests.cs` files (constructor changes, plus assertions for the new DTO fields where definitions are returned)
  - [ ] 6.4 Repository unit tests, including the 409 on a concurrent create (substituted detector). Integration test: admin `GET` definition by id returns `allowedLevelIds` and `options`. Commit

- [ ] 7. Reference removal, cleanup and docs
  - [ ] 7.1 Remove `GroundUp.Services` → `GroundUp.Data.Postgres`. Add direct references where a project used it transitively (sample, tests). The whole solution builds
  - [ ] 7.2 Remove the remaining GU-82 allowlist entries. `LayerRulesTests` passes with an empty allowlist
  - [ ] 7.3 `AuthDbContextUnitOfWork` uses `IUniqueConstraintDetector`, with an updated unit test
  - [ ] 7.4 ARCHITECTURE.md: drop the "Open exception" note. Update CLAUDE.md and the steering rules if they mention it. Commit
  - [ ] 7.5 Sample app smoke check (settings page and setup status) via the manual guide, Parts 11 and 14.4
  - [ ] 7.6 **PR 2 boundary:** I give you the compare URL, title and description, which notes the `SettingDefinitionDto` change for SDK callers. After merge: Jira GU-82 summary, then Done
