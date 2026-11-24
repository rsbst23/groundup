# Design: Core Services Layering (GU-82)

Requirements: [requirements.md](requirements.md).

## Overview

```
GroundUp.Services ──► GroundUp.Data.Abstractions (interfaces, outcome enums)
                                 ▲
GroundUp.Repositories ───────────┘  (implementations over DbContext; EF Core + Relational; Mapperly)
                                 ▲
GroundUp.Data.Postgres ──────────┘  (GroundUpDbContext, PostgresDatabaseMigrator, registration)
```

After Part B, `GroundUp.Services` references only Core, Events and Data.Abstractions (plus its existing non-EF packages).

All repositories follow the GU-81 pattern (`IdentityBootstrapRepository`):
- They are `sealed` and take a `DbContext` (the app's context, passed in as `GroundUpDbContext`) plus `IUniqueConstraintDetector` where they write.
- Reads use `AsNoTracking()`. Writes load tracked entities, change them and save inside the repository. No tracked entity leaves the repository.
- Reads return a DTO, `null` (not found) or a primitive. Writes that can fail return `OperationResult` / `OperationResult<T>` or an outcome enum, never throw for expected failures.
- After a unique violation: `ChangeTracker.Clear()`, then return 409 `CONFLICT` (`ErrorCodes.Conflict`).

## Registration

`AddGroundUpPostgres<TContext>` gains:

```csharp
services.TryAddSingleton<IUniqueConstraintDetector, PostgresUniqueConstraintDetector>();
services.AddScoped<IDatabaseMigrator>(sp => new PostgresDatabaseMigrator(sp.GetRequiredService<GroundUpDbContext>()));
services.AddScoped<IBootstrapStateRepository>(sp => new BootstrapStateRepository(sp.GetRequiredService<GroundUpDbContext>()));
services.AddScoped<ISetupTransactionLogRepository>(sp => new SetupTransactionLogRepository(
    sp.GetRequiredService<GroundUpDbContext>(), sp.GetRequiredService<ILogger<SetupTransactionLogRepository>>()));
services.AddScoped<ISettingsRepository>(sp => new SettingsRepository(
    sp.GetRequiredService<GroundUpDbContext>(), sp.GetRequiredService<IUniqueConstraintDetector>()));
services.AddScoped<ISettingsAdminRepository>(sp => new SettingsAdminRepository(
    sp.GetRequiredService<GroundUpDbContext>(), sp.GetRequiredService<IUniqueConstraintDetector>()));
```

`GroundUpWebApplicationFactory` needs no change. It replaces only `DbContextOptions<TContext>`, and the app's own `AddGroundUpPostgres` call still registers the `GroundUpDbContext` forwarding and everything above (found in task group 1).

`AddGroundUpAuthPostgres` keeps its own `TryAddSingleton` for the detector. It's harmless and keeps auth usable on its own.

## Part A

### IDatabaseMigrator (Requirement 2)

```csharp
public interface IDatabaseMigrator { Task MigrateAsync(CancellationToken cancellationToken = default); }
```

`PostgresDatabaseMigrator` (in `Data.Postgres`) calls `context.Database.MigrateAsync(ct)`. `MigrationStartupHostedService` creates a scope, resolves `IDatabaseMigrator` and calls it. The log lines and registration order stay the same.

`MigrationStartupHostedServiceTests` currently resolves `GroundUpDbContext` with SQLite. It changes to assert that the hosted service calls the migrator (an NSubstitute mock). A new small test checks that `PostgresDatabaseMigrator` applies migrations on SQLite. That's possible only if SQLite supports the migrations; if not, the existing integration startup path already covers it.

### IBootstrapStateRepository (Requirement 3.1)

```csharp
public enum BootstrapCompletionOutcome { Completed, Missing, AlreadyComplete, ConcurrentUpdate }

public interface IBootstrapStateRepository
{
    /// <summary>IsComplete of the sentinel row, or null when the row is missing.</summary>
    Task<bool?> GetIsCompleteAsync(CancellationToken cancellationToken = default);
    Task<BootstrapCompletionOutcome> TryCompleteAsync(Guid completedByUserId, CancellationToken cancellationToken = default);
}
```

The repository sets `IsComplete`, `CompletedAt = DateTime.UtcNow` and `CompletedBy`, and maps `DbUpdateConcurrencyException` to `ConcurrentUpdate`, followed by `ChangeTracker.Clear()`. The service keeps the cache, the `InvalidOperationException` for a missing row, the messages, status codes, warning log and cache invalidation, and maps each outcome to the current result.

### ISetupTransactionLogRepository (Requirement 3.2)

```csharp
public sealed record CreateSetupTransactionLogDto(string Operation, string Stage, string? Email, string? CorrelationId);

public interface ISetupTransactionLogRepository
{
    /// <summary>Inserts the row, rotating out the oldest non-pending rows beyond maxRowCount (0 = no limit). One transaction.</summary>
    Task<Guid> InsertWithRotationAsync(CreateSetupTransactionLogDto row, int maxRowCount, CancellationToken cancellationToken = default);
    Task UpdateStageAsync(Guid id, string stage, string? externalUserId, string? errorMessage, CancellationToken cancellationToken = default);
    Task<bool> HasUnfinishedFirstAdminAsync(CancellationToken cancellationToken = default);   // keycloak-pending, db-pending, failed
    Task<SetupTransactionLogDto?> GetLatestPendingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SetupTransactionLogDto>> GetRecentAsync(int count, CancellationToken cancellationToken = default);
    Task<SetupTransactionLogDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
```

- The stage names and the pending predicates stay exactly as they are today. They move into the repository as private constants, so the repository is the only place that builds the queries.
- The rotation warning moves into the repository along with the counts, which is why the repository takes an `ILogger`. The message template stays the same.
- `UpdateStageAsync` updates only the fields it's given: `externalUserId` and `errorMessage` change only when they're not null. That's the same as today, where each call site sets just the fields it touches. A missing id is a no-op. Today the row is always present, because the service inserted it in the same scope.
- `RecoverAsync`'s `catch (Exception) when not OperationCanceledException` still wraps the success-path stage update. The behavior doesn't change.
- `GetSystemLevelIdAsync` calls `ISettingsRepository.GetLevelIdByNameAsync("System")`. That method is added to `ISettingsRepository` in Part A. Only the level lookup methods are added there; the rest comes in Part B.

### Level lookups in Part A

`ISettingsRepository` starts with three methods:

```csharp
Task<Guid?> GetLevelIdByNameAsync(string name, CancellationToken cancellationToken = default);
Task<string?> GetLevelNameByIdAsync(Guid levelId, CancellationToken cancellationToken = default);
Task<SettingLevelDto> EnsureLevelAsync(string name, Guid? parentId, int displayOrder, CancellationToken cancellationToken = default);
```

`DefaultScopeChainProvider` and `DefaultSettingLevelSeeder` switch to these methods in Part A. That leaves only `SettingsService` and `SettingsAdminService` for Part B.

## Part B

### SettingDefinitionDto (Requirement 5)

Two parameters are appended to the end of the record, so existing positional order is kept:

```csharp
public record SettingDefinitionDto(
    /* existing 26 parameters */,
    IReadOnlyList<Guid> AllowedLevelIds,
    IReadOnlyList<SettingOptionDto> Options);
```

`SettingOptionDto` already exists. Options are ordered by `DisplayOrder`, then `Value`. Allowed level ids keep the order they are stored in. Both lists are never null.

### SettingsMapper (Mapperly)

`GroundUp.Repositories/Settings/SettingsMapper.cs` is a `[Mapper] internal static partial class` with these mappings:
- `SettingDefinition → SettingDefinitionDto`. `AllowedLevels` maps to `AllowedLevelIds` through a user method `Select(l => l.SettingLevelId)`, and `Options` maps to a sorted list.
- `SettingLevel → SettingLevelDto`
- `SettingGroup → SettingGroupDto`
- `SettingValue → SettingValueDto`
- `SettingOption → SettingOptionDto`

Every read that returns a definition loads `AllowedLevels` and `Options`. A query that includes two collections uses `AsSplitQuery()`, which is in Relational, to avoid a cartesian product. Unmapped-member diagnostics are set to errors, so a field added later can't be silently dropped.

### ISettingsRepository: runtime (Requirement 4.1)

Methods added in Part B:

```csharp
Task<SettingDefinitionDto?> GetDefinitionByKeyAsync(string key, CancellationToken ct = default);
Task<IReadOnlyList<SettingDefinitionDto>> GetAllDefinitionsAsync(CancellationToken ct = default);
Task<SettingGroupDto?> GetGroupByKeyAsync(string groupKey, CancellationToken ct = default);
Task<IReadOnlyList<SettingDefinitionDto>> GetDefinitionsByGroupAsync(Guid groupId, CancellationToken ct = default);
Task<SettingValueDto?> GetValueAsync(Guid definitionId, Guid levelId, Guid? scopeId, CancellationToken ct = default);
Task<IReadOnlyList<SettingValueDto>> GetValuesForDefinitionsAsync(IReadOnlyCollection<Guid> definitionIds, CancellationToken ct = default);
Task<SettingValueUpsertResult> UpsertValueAsync(Guid definitionId, Guid levelId, Guid? scopeId, string? value, CancellationToken ct = default);
Task<SettingValueDeleteResult?> FindValueAsync(Guid valueId, CancellationToken ct = default);   // value + definition key
Task<bool> DeleteValueByIdAsync(Guid valueId, CancellationToken ct = default);                   // false when already gone
Task<OperationResult<SettingDefinitionDto>> EnsureDefinitionAsync(EnsureSettingDefinitionRequest request, CancellationToken ct = default);
```

The two result records are defined in `Data.Abstractions`:
- `SettingValueUpsertResult(SettingValueDto Value, string? OldValue)`. The service needs `OldValue` for `SettingChangedEvent`.
- `SettingValueDeleteResult(SettingValueDto Value, string DefinitionKey)`.

**Delete.** Authorization runs between the read and the remove, so the delete takes two calls:
- `FindValueAsync(valueId)` returns `SettingValueDeleteResult?` (the value plus its definition key). Null returns today's 404.
- The service authorizes, then calls `DeleteValueByIdAsync(valueId)`. If the value was removed in between, the service returns the same 404.

The repository never calls back into authorization. The cost is one more query than today on the delete path.

**SetAsync.**
1. `GetDefinitionByKeyAsync`.
2. All validations, unchanged.
3. Level name lookup for the write validators.
4. Encryption.
5. `UpsertValueAsync`.
6. Event publish.

The upsert is a find followed by an insert or update. A concurrent insert of the same (definition, level, scope) can hit the unique index; this is new handling. The repository then clears the tracker, re-reads the row, applies the update and saves once more. Anything else propagates. Today that race throws an unhandled exception.

**Resolution.** `ResolveFromDatabaseAsync` keeps one `GetValueAsync` call per allowed scope entry (Requirement 4.4). The bulk paths use `GetValuesForDefinitionsAsync`. `ResolveEffectiveValue` and the private `ResolvedValue` change to work on DTOs, and the loop logic stays the same. `SecretMask`, decryption and the secret resolver stay in the service, unchanged.

The service methods `GetTypedValueAsync` and `AuthorizeAsync` use `GetDefinitionByKeyAsync` and `GetLevelNameByIdAsync`.

### EnsureDefinitionAsync (Requirement 6)

The whole transaction moves into `SettingsRepository.EnsureDefinitionAsync`. The group, the definition and the level links are written in the same order with the same three saves. The case-insensitive level name match is kept.

```text
existing = by key (with levels and options) → Ok(existing)
begin transaction
  try   ... commit → Ok(mapped, re-read with includes)
  catch (DbUpdateException ex) when detector.IsUniqueConstraintViolation(ex)
        rollback; ChangeTracker.Clear(); re-read by key → Ok(winner) or Fail("Failed to create setting definition '{key}'", 500)
```

There is one change: any other `DbUpdateException` propagates (decided). The service method becomes a one-line pass-through.

### ISettingsAdminRepository (Requirement 4.2)

```csharp
// Levels
Task<IReadOnlyList<SettingLevelDto>> GetAllLevelsAsync(CancellationToken ct = default);
Task<SettingLevelDto?> GetLevelByIdAsync(Guid id, CancellationToken ct = default);
Task<bool> LevelNameExistsAsync(string name, CancellationToken ct = default);
Task<bool> LevelHasChildrenAsync(Guid id, CancellationToken ct = default);
Task<bool> LevelHasValuesAsync(Guid id, CancellationToken ct = default);
Task<OperationResult<SettingLevelDto>> CreateLevelAsync(CreateSettingLevelDto dto, CancellationToken ct = default);
Task<OperationResult<SettingLevelDto>> UpdateLevelAsync(Guid id, UpdateSettingLevelDto dto, CancellationToken ct = default);
Task<OperationResult> DeleteLevelAsync(Guid id, CancellationToken ct = default);
// Groups: same shape (GetAll, GetById, KeyExists, Create, Update, Delete; delete orphans definitions in the same save)
// Definitions: GetAll, GetById, KeyExists, Create, Update (full replace of options and levels), Delete
```

- The service keeps the existence checks before create. Those return 400 with today's messages. It also keeps the child-level and values-in-use guards for level delete, which need the level name for the message.
- The repository's write methods return NotFound with today's messages (moved verbatim), or 409 `CONFLICT` on a unique violation (Requirement 4.3).
- `CreatedAt` and `UpdatedAt` are set the same way as today.

Today `GetAllDefinitionsAsync` loads `Options` and not `AllowedLevels`. After this change it loads both (Requirement 5.2).

## Error handling summary

| Case | Today | After |
|---|---|---|
| EnsureDefinition race (unique) | re-read | re-read after `ChangeTracker.Clear()` |
| EnsureDefinition other DB error | re-read, then 500 if not found | propagates (decided) |
| Admin create/update unique race | unhandled → 500 | 409 `CONFLICT` |
| SetAsync concurrent insert | unhandled → 500 | retried as update once |
| Bootstrap concurrent completion | 409 | 409 (same) |
| Auth unit of work unique check | own SQL state check | `IUniqueConstraintDetector` |

## Testing

- **Unit (SQLite, as today):**
  - Repository tests per repository.
  - A mapper test that builds a fully populated `SettingDefinition` and asserts every DTO field, including the two new ones.
  - `SettingsTestFixture.CreateService` builds `SettingsRepository` over the fixture context, so tests keep seeding and asserting through that context.
  - The admin, scope chain, seeder, bootstrap and setup tests construct the services the same way, through small helpers in the existing fixtures.
  - The SQLite unique-violation code (19 / 2067) is not 23505. Tests that need a conflict substitute `IUniqueConstraintDetector`.
- **Integration (Testcontainers Postgres):**
  - Setup log rotation that keeps pending rows.
  - Parallel `EnsureDefinitionAsync` for one key, using two scopes, asserting both return the same id.
  - Admin `GET /api/settings/definitions/{id}` returning `allowedLevelIds` and `options`.
  - The existing `BootstrapConcurrencyTests`.
- **Architecture:** after Part B, the allowlist is empty and `LayerRulesTests` passes.

## Files (estimate)

- **Part A (PR 1), about 15 source files plus tests:**
  - 4 interfaces and records in Abstractions
  - 3 repositories
  - the migrator
  - registration and the test factory
  - 4 services
  - 1 csproj (Relational in Repositories)
- **Part B (PR 2), about 12 source files plus tests:**
  - DTO
  - mapper
  - 2 repositories and interfaces
  - 2 services
  - `AuthDbContextUnitOfWork`
  - csproj reference removal
  - allowlist
  - docs
