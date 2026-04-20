# Implementation Plan: Phase 3C — EF Core & Postgres

## Overview

Build the EF Core infrastructure in GroundUp.Data.Postgres: abstract `GroundUpDbContext`, `UuidV7ValueGenerator`, `AuditableInterceptor`, `SoftDeleteInterceptor`, `PostgresErrorHelper`, `DataSeederRunner`, and `AddGroundUpPostgres<TContext>` extension method. Implementation follows: feature branch → NuGet packages → value generator → DbContext → interceptors → error helper → seeder runner → DI extension → test helpers → unit tests (21 tests across 5 classes) → final verification. Each task is a small compilable increment with a commit.

All code is C# targeting .NET 8, matching the design document. No property-based tests for this phase — interceptors, helpers, and orchestration logic are not suitable for PBT (see design rationale).

## Tasks

- [-] 1. Create feature branch and add NuGet packages
  - [-] 1.1 Create and checkout branch `phase-3c/ef-core-postgres` from `main`
    - Run `dotnet build groundup.sln` to verify clean starting point
    - _Requirements: 14.1_
  - [ ] 1.2 Add NuGet package references to `src/GroundUp.Data.Postgres/GroundUp.Data.Postgres.csproj`
    - Add `UUIDNext` package reference
    - Add `Microsoft.Extensions.Hosting.Abstractions` Version `8.*` package reference
    - Retain existing `Microsoft.EntityFrameworkCore` and `Npgsql.EntityFrameworkCore.PostgreSQL` references
    - Retain existing project references to Core, Data.Abstractions, and Repositories
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "Add UUIDNext and Hosting.Abstractions NuGet packages to Data.Postgres"
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

- [ ] 2. Implement UuidV7ValueGenerator
  - [ ] 2.1 Create `UuidV7ValueGenerator` in `src/GroundUp.Data.Postgres/UuidV7ValueGenerator.cs`
    - Define in `GroundUp.Data.Postgres` namespace with file-scoped namespace
    - Sealed class extending `Microsoft.EntityFrameworkCore.ChangeTracking.ValueGenerator<Guid>`
    - Override `GeneratesTemporaryValues` to return `false`
    - Override `Next(EntityEntry entry)` to return `UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql)`
    - Add XML documentation comments on the class and all public members
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "Add UuidV7ValueGenerator using UUIDNext package"
    - _Requirements: 4.1, 4.2, 15.1, 15.2, 15.5, 15.6, 15.7_

- [ ] 3. Implement GroundUpDbContext
  - [ ] 3.1 Create `GroundUpDbContext` in `src/GroundUp.Data.Postgres/GroundUpDbContext.cs`
    - Define in `GroundUp.Data.Postgres` namespace with file-scoped namespace
    - Abstract class extending `Microsoft.EntityFrameworkCore.DbContext`
    - Constructor: accept `DbContextOptions options` (not `DbContextOptions<GroundUpDbContext>`) and pass to base
    - Do NOT use the `sealed` modifier — consuming applications must inherit from it
    - Override `OnModelCreating(ModelBuilder modelBuilder)`:
      1. Call `base.OnModelCreating(modelBuilder)` first so derived context configurations register first
      2. Scan `modelBuilder.Model.GetEntityTypes()` for types assignable to `BaseEntity`, configure `Id` property with `HasValueGenerator<UuidV7ValueGenerator>()`
      3. Scan `modelBuilder.Model.GetEntityTypes()` for types assignable to `ISoftDeletable`, call private `ApplySoftDeleteFilter` for each
    - Implement private static `ApplySoftDeleteFilter(ModelBuilder modelBuilder, Type entityType)`:
      - Build expression `(TEntity e) => !e.IsDeleted` dynamically using `Expression.Parameter`, `Expression.Property`, `Expression.Not`, `Expression.Lambda`
      - Apply via `modelBuilder.Entity(entityType).HasQueryFilter(lambda)`
    - Add XML documentation comments on the class, constructor, and all public/protected members
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "Add abstract GroundUpDbContext with UUID v7 generation and soft delete query filters"
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 4.1, 4.3, 4.4, 5.1, 5.2, 15.1, 15.2, 15.4, 15.6, 15.7_

- [ ] 4. Checkpoint — Verify DbContext and value generator compile
  - Ensure `dotnet build groundup.sln` passes with zero errors
  - Ensure `GroundUpDbContext.cs` and `UuidV7ValueGenerator.cs` exist in `src/GroundUp.Data.Postgres/`
  - Ensure all existing tests still pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 5. Implement interceptors
  - [ ] 5.1 Create `AuditableInterceptor` in `src/GroundUp.Data.Postgres/Interceptors/AuditableInterceptor.cs`
    - Define in `GroundUp.Data.Postgres.Interceptors` namespace with file-scoped namespace
    - Sealed class extending `Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor`
    - Constructor: accept `IServiceProvider serviceProvider`, store in private readonly field
    - Override `SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken)`:
      - If `eventData.Context` is null, delegate to base and return
      - Create a scope via `_serviceProvider.CreateScope()` (using statement)
      - Resolve `ICurrentUser` via `scope.ServiceProvider.GetService<ICurrentUser>()` (may be null)
      - Capture `userId = currentUser?.UserId.ToString()` and `utcNow = DateTime.UtcNow`
      - Iterate `eventData.Context.ChangeTracker.Entries<IAuditable>()`:
        - `EntityState.Added`: set `CreatedAt = utcNow`, `CreatedBy = userId`
        - `EntityState.Modified`: set `UpdatedAt = utcNow`, `UpdatedBy = userId`
      - Delegate to base
    - Add XML documentation comments on the class, constructor, and all public members
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "Add AuditableInterceptor for automatic audit field population"
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 15.1, 15.2, 15.5, 15.6, 15.7_
  - [ ] 5.2 Create `SoftDeleteInterceptor` in `src/GroundUp.Data.Postgres/Interceptors/SoftDeleteInterceptor.cs`
    - Define in `GroundUp.Data.Postgres.Interceptors` namespace with file-scoped namespace
    - Sealed class extending `SaveChangesInterceptor`
    - Constructor: accept `IServiceProvider serviceProvider`, store in private readonly field
    - Override `SavingChangesAsync`:
      - If `eventData.Context` is null, delegate to base and return
      - Create a scope, resolve `ICurrentUser` (may be null)
      - Capture `userId` and `utcNow`
      - Iterate `eventData.Context.ChangeTracker.Entries<ISoftDeletable>()`:
        - If `entry.State == EntityState.Deleted`: change state to `EntityState.Modified`, set `IsDeleted = true`, `DeletedAt = utcNow`, `DeletedBy = userId`
      - Delegate to base
    - Add XML documentation comments on the class, constructor, and all public members
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "Add SoftDeleteInterceptor for safety-net soft delete conversion"
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 15.1, 15.2, 15.5, 15.6, 15.7_

- [ ] 6. Implement PostgresErrorHelper
  - [ ] 6.1 Create `PostgresErrorHelper` in `src/GroundUp.Data.Postgres/PostgresErrorHelper.cs`
    - Define in `GroundUp.Data.Postgres` namespace with file-scoped namespace
    - Static sealed class (use `public static class`)
    - Static method `IsUniqueConstraintViolation(DbUpdateException? exception)` returning `bool`:
      - If `exception?.InnerException` is not `Npgsql.PostgresException pgEx`, return `false`
      - Return `pgEx.SqlState == "23505"`
    - Add XML documentation comments on the class and the method
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "Add PostgresErrorHelper for unique constraint violation detection"
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 15.1, 15.2, 15.5, 15.6, 15.7_

- [ ] 7. Implement DataSeederRunner
  - [ ] 7.1 Create `DataSeederRunner` in `src/GroundUp.Data.Postgres/DataSeederRunner.cs`
    - Define in `GroundUp.Data.Postgres` namespace with file-scoped namespace
    - Sealed class implementing `Microsoft.Extensions.Hosting.IHostedService`
    - Constructor: accept `IServiceProvider serviceProvider` and `ILogger<DataSeederRunner> logger`, store in private readonly fields
    - Implement `StartAsync(CancellationToken cancellationToken)`:
      - Create a scope via `_serviceProvider.CreateScope()` (using statement)
      - Resolve `IEnumerable<IDataSeeder>` via `scope.ServiceProvider.GetServices<IDataSeeder>()`
      - Order by `Order` ascending
      - For each seeder: try `await seeder.SeedAsync(cancellationToken)`, catch `Exception` → log error via `_logger.LogError`, continue to next
      - Log information before each seeder with seeder type name and order
    - Implement `StopAsync(CancellationToken)`: return `Task.CompletedTask`
    - Add XML documentation comments on the class, constructor, and all public members
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "Add DataSeederRunner hosted service for startup data seeding"
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 15.1, 15.2, 15.5, 15.6, 15.7_

- [ ] 8. Implement AddGroundUpPostgres extension method
  - [ ] 8.1 Create `PostgresServiceCollectionExtensions` in `src/GroundUp.Data.Postgres/PostgresServiceCollectionExtensions.cs`
    - Define in `GroundUp.Data.Postgres` namespace with file-scoped namespace
    - Static class
    - Static extension method `AddGroundUpPostgres<TContext>(this IServiceCollection services, string connectionString)` where `TContext : GroundUpDbContext`:
      - Register `AuditableInterceptor` as singleton
      - Register `SoftDeleteInterceptor` as singleton
      - Call `services.AddDbContext<TContext>((sp, options) => { options.UseNpgsql(connectionString); options.AddInterceptors(sp.GetRequiredService<AuditableInterceptor>(), sp.GetRequiredService<SoftDeleteInterceptor>()); })`
      - Register `DataSeederRunner` as hosted service via `services.AddHostedService<DataSeederRunner>()`
      - Return `services` for method chaining
    - Add XML documentation comments on the class and the extension method
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "Add AddGroundUpPostgres extension method for DI registration"
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 15.1, 15.2, 15.5, 15.6, 15.7_

- [ ] 9. Checkpoint — Verify all production code compiles
  - Ensure `dotnet build groundup.sln` passes with zero errors
  - Verify all 7 source files exist in `src/GroundUp.Data.Postgres/`:
    - `GroundUpDbContext.cs`, `UuidV7ValueGenerator.cs`, `PostgresErrorHelper.cs`, `DataSeederRunner.cs`, `PostgresServiceCollectionExtensions.cs`
    - `Interceptors/AuditableInterceptor.cs`, `Interceptors/SoftDeleteInterceptor.cs`
  - Ensure all existing tests still pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 10. Add test project references
  - [ ] 10.1 Update `tests/GroundUp.Tests.Unit/GroundUp.Tests.Unit.csproj`
    - Add project reference to `..\..\src\GroundUp.Data.Postgres\GroundUp.Data.Postgres.csproj`
    - Add package reference to `Npgsql` (for constructing `PostgresException` in tests)
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "Add Data.Postgres project ref and Npgsql package ref to test project"
    - _Requirements: 14.1_

- [ ] 11. Create test helpers for Phase 3C
  - [ ] 11.1 Create `AuditableTestEntity` in `tests/GroundUp.Tests.Unit/Data/Postgres/TestHelpers/AuditableTestEntity.cs`
    - Extend `BaseEntity`, implement `IAuditable`
    - Properties: `string Name`, `DateTime CreatedAt`, `string? CreatedBy`, `DateTime? UpdatedAt`, `string? UpdatedBy`
    - _Requirements: 10.5_
  - [ ] 11.2 Create `SoftDeletableAuditableTestEntity` in `tests/GroundUp.Tests.Unit/Data/Postgres/TestHelpers/SoftDeletableAuditableTestEntity.cs`
    - Extend `BaseEntity`, implement `IAuditable` and `ISoftDeletable`
    - Properties: `string Name`, all IAuditable properties, all ISoftDeletable properties
    - _Requirements: 10.5, 11.4_
  - [ ] 11.3 Create `NonAuditableTestEntity` in `tests/GroundUp.Tests.Unit/Data/Postgres/TestHelpers/NonAuditableTestEntity.cs`
    - Extend `BaseEntity` only (does NOT implement IAuditable or ISoftDeletable)
    - Properties: `string Name`
    - _Requirements: 10.3_
  - [ ] 11.4 Create `TestGroundUpDbContext` in `tests/GroundUp.Tests.Unit/Data/Postgres/TestHelpers/TestGroundUpDbContext.cs`
    - Extend `GroundUpDbContext` (not plain `DbContext`)
    - Constructor: accept `DbContextOptions<TestGroundUpDbContext> options`, pass to base
    - DbSet properties for `AuditableTestEntity`, `SoftDeletableAuditableTestEntity`, `NonAuditableTestEntity`
    - Override `OnModelCreating`: call `base.OnModelCreating(modelBuilder)` first, then configure entity keys and Name property constraints
    - Static factory method `Create()` returning a new instance with InMemory provider and unique database name
    - _Requirements: 10.5, 11.4_
  - Run `dotnet build groundup.sln` to verify compilation
  - Commit: "Add Phase 3C test helpers and TestGroundUpDbContext"

- [ ] 12. Checkpoint — Verify test helpers compile
  - Ensure `dotnet build groundup.sln` passes with zero errors
  - Ensure all 4 test helper files exist in `tests/GroundUp.Tests.Unit/Data/Postgres/TestHelpers/`
  - Ensure all existing tests still pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 13. Write unit tests for AuditableInterceptor
  - [ ]* 13.1 Create `tests/GroundUp.Tests.Unit/Data/Postgres/AuditableInterceptorTests.cs`
    - Write test: `SavingChangesAsync_AddedAuditableEntity_SetsCreatedAtAndCreatedBy`
      - Create `TestGroundUpDbContext` with InMemory provider, add `AuditableTestEntity` in Added state
      - Mock `IServiceProvider` to return scope with `ICurrentUser` (NSubstitute)
      - Invoke `SavingChangesAsync` with `DbContextEventData` pointing to the context
      - Assert `CreatedAt` is within 1 second of `DateTime.UtcNow` and `CreatedBy` equals `ICurrentUser.UserId.ToString()`
    - Write test: `SavingChangesAsync_ModifiedAuditableEntity_SetsUpdatedAtAndUpdatedBy`
      - Add entity, save, then modify it, invoke interceptor
      - Assert `UpdatedAt` is within 1 second of `DateTime.UtcNow` and `UpdatedBy` equals `ICurrentUser.UserId.ToString()`
    - Write test: `SavingChangesAsync_NonAuditableEntity_DoesNotModify`
      - Add `NonAuditableTestEntity` in Added state, invoke interceptor
      - Assert entity has no audit fields set (entity type doesn't have them)
    - Write test: `SavingChangesAsync_NoCurrentUser_SetsTimestampsLeavesUserFieldsNull`
      - Mock `IServiceProvider` to return scope where `GetService<ICurrentUser>()` returns null
      - Add `AuditableTestEntity` in Added state, invoke interceptor
      - Assert `CreatedAt` is set, `CreatedBy` is null
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_
  - Run `dotnet test` to verify all tests pass
  - Commit: "Add AuditableInterceptor unit tests (4 tests)"

- [ ] 14. Write unit tests for SoftDeleteInterceptor
  - [ ]* 14.1 Create `tests/GroundUp.Tests.Unit/Data/Postgres/SoftDeleteInterceptorTests.cs`
    - Write test: `SavingChangesAsync_DeletedSoftDeletableEntity_ConvertsToModifiedAndSetsFields`
      - Add `SoftDeletableAuditableTestEntity`, save, then mark as Deleted, invoke interceptor
      - Assert state changed to Modified, `IsDeleted` is true, `DeletedAt` is within 1 second of `DateTime.UtcNow`
    - Write test: `SavingChangesAsync_DeletedNonSoftDeletableEntity_DoesNotModifyState`
      - Add `NonAuditableTestEntity`, save, then mark as Deleted, invoke interceptor
      - Assert state remains Deleted (not converted)
    - Write test: `SavingChangesAsync_WithCurrentUser_SetsDeletedBy`
      - Mock `ICurrentUser` with a known UserId, invoke interceptor on Deleted `SoftDeletableAuditableTestEntity`
      - Assert `DeletedBy` equals `ICurrentUser.UserId.ToString()`
    - Write test: `SavingChangesAsync_NoCurrentUser_SetsIsDeletedAndDeletedAtLeavesDeletedByNull`
      - Mock `IServiceProvider` to return scope where `GetService<ICurrentUser>()` returns null
      - Invoke interceptor on Deleted `SoftDeletableAuditableTestEntity`
      - Assert `IsDeleted` is true, `DeletedAt` is set, `DeletedBy` is null
    - _Requirements: 11.1, 11.2, 11.3, 11.4_
  - Run `dotnet test` to verify all tests pass
  - Commit: "Add SoftDeleteInterceptor unit tests (4 tests)"

- [ ] 15. Write unit tests for PostgresErrorHelper
  - [ ]* 15.1 Create `tests/GroundUp.Tests.Unit/Data/Postgres/PostgresErrorHelperTests.cs`
    - Write test: `IsUniqueConstraintViolation_PostgresException23505_ReturnsTrue`
      - Construct `PostgresException` with SqlState "23505" (via Npgsql package — use reflection or internal constructor)
      - Wrap in `DbUpdateException`, call `IsUniqueConstraintViolation`, assert true
    - Write test: `IsUniqueConstraintViolation_PostgresExceptionDifferentCode_ReturnsFalse`
      - Construct `PostgresException` with SqlState "23503" (foreign key violation)
      - Wrap in `DbUpdateException`, call `IsUniqueConstraintViolation`, assert false
    - Write test: `IsUniqueConstraintViolation_NoInnerException_ReturnsFalse`
      - Create `DbUpdateException` with no inner exception, assert false
    - Write test: `IsUniqueConstraintViolation_Null_ReturnsFalse`
      - Call `IsUniqueConstraintViolation(null)`, assert false
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_
  - Run `dotnet test` to verify all tests pass
  - Commit: "Add PostgresErrorHelper unit tests (4 tests)"

- [ ] 16. Write unit tests for DataSeederRunner
  - [ ]* 16.1 Create `tests/GroundUp.Tests.Unit/Data/Postgres/DataSeederRunnerTests.cs`
    - Write test: `StartAsync_MultipleSeeders_ExecutesInOrderAscending`
      - Create mock `IDataSeeder` implementations with Order values 3, 1, 2
      - Register in DI, create `DataSeederRunner`, call `StartAsync`
      - Assert `SeedAsync` called in order: Order 1, Order 2, Order 3
    - Write test: `StartAsync_SeederThrows_LogsErrorAndContinues`
      - Create 3 mock seeders, middle one throws `InvalidOperationException`
      - Call `StartAsync`, assert first and third seeders' `SeedAsync` were called
      - Assert error was logged (verify via `ILogger` mock)
    - Write test: `StartAsync_NoSeeders_CompletesWithoutError`
      - Register no `IDataSeeder` implementations, call `StartAsync`
      - Assert completes without exception
    - Write test: `StopAsync_CompletesImmediately`
      - Call `StopAsync`, assert it completes (returns `Task.CompletedTask`)
    - _Requirements: 13.1, 13.2, 13.3, 13.4_
  - Run `dotnet test` to verify all tests pass
  - Commit: "Add DataSeederRunner unit tests (4 tests)"

- [ ] 17. Write unit tests for GroundUpDbContext
  - [ ]* 17.1 Create `tests/GroundUp.Tests.Unit/Data/Postgres/GroundUpDbContextTests.cs`
    - Write test: `OnModelCreating_BaseEntity_ConfiguresUuidV7ValueGenerator`
      - Create `TestGroundUpDbContext`, inspect model for `AuditableTestEntity.Id` property
      - Assert value generator type is `UuidV7ValueGenerator`
    - Write test: `OnModelCreating_SoftDeletableEntity_AppliesQueryFilter`
      - Create `TestGroundUpDbContext`, inspect model for `SoftDeletableAuditableTestEntity`
      - Assert query filter is configured on the entity type
    - Write test: `OnModelCreating_NonSoftDeletableEntity_NoQueryFilter`
      - Create `TestGroundUpDbContext`, inspect model for `NonAuditableTestEntity`
      - Assert no query filter is configured
    - Write test: `OnModelCreating_QueryExcludesSoftDeletedEntities`
      - Add `SoftDeletableAuditableTestEntity` with `IsDeleted = true` and one with `IsDeleted = false`
      - Query the DbSet, assert only the non-deleted entity is returned
    - Write test: `OnModelCreating_IgnoreQueryFilters_ReturnsSoftDeletedEntities`
      - Add entities as above, query with `IgnoreQueryFilters()`, assert both returned
    - _Requirements: 1.3, 1.4, 1.5, 4.1, 4.3, 5.1, 5.2, 5.3, 5.4_
  - Run `dotnet test` to verify all tests pass
  - Commit: "Add GroundUpDbContext unit tests (5 tests)"

- [ ] 18. Final checkpoint — Full solution build and test verification
  - Run `dotnet build groundup.sln` and verify zero errors
  - Run `dotnet test` and verify all tests pass (existing tests + 21 new Phase 3C tests)
  - Verify file-scoped namespaces, nullable reference types, XML documentation, sealed modifiers, one-class-per-file across all new files
  - Ensure all tests pass, ask the user if questions arise.
  - Commit: "Phase 3C complete — all tests green"
  - _Requirements: 14.1, 14.2, 15.1, 15.2, 15.3, 15.4, 15.5, 15.6, 15.7_

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each major component
- No property-based tests for this phase — interceptors, helpers, and orchestration logic are not suitable for PBT (design rationale explains why)
- Unit tests (21 total): AuditableInterceptor (4), SoftDeleteInterceptor (4), PostgresErrorHelper (4), DataSeederRunner (4), GroundUpDbContext (5)
- All tests use EF Core InMemory for unit test isolation — integration tests with real Postgres come in later phases
- ICurrentUser is resolved from DI scope in interceptors — mocked via NSubstitute in tests
- PostgresException construction requires the Npgsql package reference in the test project
- TestGroundUpDbContext inherits from GroundUpDbContext (not plain DbContext) to test framework conventions
- Git workflow: commit after each compilable step, small commits with clear messages
