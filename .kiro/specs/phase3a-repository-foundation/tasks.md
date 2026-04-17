# Implementation Plan: Phase 3A — Data Abstractions & Repository Foundation

## Overview

Build the repository interfaces in GroundUp.Data.Abstractions and the base repository implementation (including ExpressionHelper) in GroundUp.Repositories. Implementation follows the design document's architecture: interfaces first, then ExpressionHelper (pure functions, ideal for property-based testing), then BaseRepository (CRUD with filtering, sorting, paging, soft delete awareness), then comprehensive tests.

All code is C# targeting .NET 8. The implementation language matches the design document exactly.

## Tasks

- [x] 1. Create feature branch and set up project dependencies
  - Create and checkout branch `phase-3a/repository-foundation` from `main`
  - Add `Microsoft.EntityFrameworkCore` (Version 8.*) NuGet package to `GroundUp.Repositories.csproj`
  - Add `Riok.Mapperly` (Version 4.*) NuGet package to `GroundUp.Repositories.csproj`
  - Retain existing project references to Core, Data.Abstractions, and Events
  - Verify no provider-specific packages (no Npgsql, no SqlServer) in Repositories
  - Run `dotnet build groundup.sln` to verify the solution compiles
  - Commit: "Add EF Core and Mapperly NuGet dependencies to Repositories project"
  - _Requirements: 9.1, 9.2, 9.3, 9.4_

- [x] 2. Define IBaseRepository, IUnitOfWork, and IDataSeeder interfaces
  - [x] 2.1 Create `IBaseRepository<TDto>` interface in `src/GroundUp.Data.Abstractions/IBaseRepository.cs`
    - Define in `GroundUp.Data.Abstractions` namespace with file-scoped namespace
    - Generic constraint: `where TDto : class`
    - Methods: `GetAllAsync(FilterParams, CancellationToken)`, `GetByIdAsync(Guid, CancellationToken)`, `AddAsync(TDto, CancellationToken)`, `UpdateAsync(Guid, TDto, CancellationToken)`, `DeleteAsync(Guid, CancellationToken)`
    - Return types: `Task<OperationResult<PaginatedData<TDto>>>`, `Task<OperationResult<TDto>>`, `Task<OperationResult>`
    - Add XML documentation comments on the interface and all methods
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8_
  - [x] 2.2 Create `IUnitOfWork` interface in `src/GroundUp.Data.Abstractions/IUnitOfWork.cs`
    - Define in `GroundUp.Data.Abstractions` namespace with file-scoped namespace
    - Method: `ExecuteInTransactionAsync(Func<CancellationToken, Task>, CancellationToken)`
    - Return type: `Task<OperationResult>` (non-generic)
    - Add XML documentation comments on the interface and method
    - _Requirements: 2.1, 2.2, 2.3, 2.4_
  - [x] 2.3 Create `IDataSeeder` interface in `src/GroundUp.Data.Abstractions/IDataSeeder.cs`
    - Define in `GroundUp.Data.Abstractions` namespace with file-scoped namespace
    - Property: `int Order { get; }` (read-only, lower values execute first)
    - Method: `SeedAsync(CancellationToken)`
    - Add XML documentation comments on the interface, property, and method
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_
  - Run `dotnet build groundup.sln` to verify compilation
  - Commit: "Add IBaseRepository, IUnitOfWork, and IDataSeeder interfaces"
  - _Requirements: 19.1, 19.2, 19.3, 19.6_

- [x] 3. Checkpoint — Verify interfaces compile and review
  - Ensure `dotnet build groundup.sln` passes with zero errors
  - Ensure all three interfaces exist in `src/GroundUp.Data.Abstractions/`
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement ExpressionHelper — all predicate and sorting methods
  - [x] 4.1 Create `ExpressionHelper` static class in `src/GroundUp.Repositories/ExpressionHelper.cs`
    - Define in `GroundUp.Repositories` namespace with file-scoped namespace
    - Use `public static class ExpressionHelper` (static modifier)
    - Implement `BuildPredicate<T>(string propertyName, string value)` — case-insensitive string comparison for string properties, parse-and-compare for Guid/int/DateTime/bool/enum, always-true predicate for invalid property names
    - Implement `BuildContainsPredicate<T>(string propertyName, string value)` — case-insensitive substring match for string properties, always-true for non-string or invalid properties
    - Implement `BuildRangePredicate<T>(string propertyName, string? minValue, string? maxValue)` — >= min AND/OR <= max for comparable types, always-true for invalid properties
    - Implement `BuildDateRangePredicate<T>(string propertyName, string? minDate, string? maxDate)` — DateTime parsing with >= min AND/OR <= max, always-true for invalid properties or unparseable dates
    - Implement `ApplySorting<T>(IQueryable<T> query, string? sortExpression)` — ascending by default, "PropertyName desc" for descending, unchanged queryable for invalid properties or null/empty expression
    - Add XML documentation comments on the class and all public methods
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 5.1, 5.2, 5.3, 6.1, 6.2, 6.3, 6.4, 6.5, 7.1, 7.2, 7.3, 7.4, 8.1, 8.2, 8.3, 8.4, 8.5, 19.4_
  - Run `dotnet build groundup.sln` to verify compilation
  - Commit: "Implement ExpressionHelper with all predicate and sorting methods"

- [-] 5. Implement BaseRepository — constructor, GetAllAsync, GetByIdAsync, AddAsync, UpdateAsync, DeleteAsync
  - [-] 5.1 Create `BaseRepository<TEntity, TDto>` abstract class in `src/GroundUp.Repositories/BaseRepository.cs`
    - Define in `GroundUp.Repositories` namespace with file-scoped namespace
    - Generic constraints: `where TEntity : BaseEntity` and `where TDto : class`
    - Implement `IBaseRepository<TDto>`
    - Constructor: accept `DbContext context`, `Func<TEntity, TDto> mapToDto`, `Func<TDto, TEntity> mapToEntity`
    - Expose `protected DbContext Context` and `protected DbSet<TEntity> DbSet` properties
    - Implement `GetAllAsync` with pipeline: AsNoTracking → QueryShaper → Filters → Sorting → Count → Paging → Map → PaginatedData
    - Implement `GetByIdAsync` with AsNoTracking, optional QueryShaper, NotFound handling
    - Implement `AddAsync` with DTO-to-entity mapping, SaveChangesAsync, 201 status, DbUpdateException → 409 Conflict
    - Implement `UpdateAsync` with FindAsync, Entry.CurrentValues.SetValues, SaveChangesAsync, NotFound handling, DbUpdateException → 409 Conflict
    - Implement `DeleteAsync` with `typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity))` for soft/hard delete decision, NotFound handling
    - Add XML documentation comments on the class, constructor, and all public/protected members
    - Do NOT use the sealed modifier (designed for inheritance)
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8, 12.1, 12.2, 12.3, 12.4, 13.1, 13.2, 13.3, 13.4, 14.1, 14.2, 14.3, 14.4, 15.1, 15.2, 15.3, 15.4, 19.5_
  - Run `dotnet build groundup.sln` to verify compilation
  - Commit: "Implement BaseRepository with full CRUD, filtering, paging, and soft delete"

- [ ] 6. Checkpoint — Verify ExpressionHelper and BaseRepository compile
  - Ensure `dotnet build groundup.sln` passes with zero errors
  - Ensure `ExpressionHelper.cs` and `BaseRepository.cs` exist in `src/GroundUp.Repositories/`
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 7. Set up test infrastructure and add test project dependencies
  - [ ] 7.1 Update `GroundUp.Tests.Unit.csproj` with new dependencies
    - Add project reference to `GroundUp.Repositories`
    - Add project reference to `GroundUp.Data.Abstractions`
    - Add NuGet reference to `Microsoft.EntityFrameworkCore.InMemory` (Version 8.*)
    - _Requirements: 16.8, 17.6, 17.7_
  - [ ] 7.2 Create test helper classes in `tests/GroundUp.Tests.Unit/Repositories/TestHelpers/`
    - Create `TestEntity` — extends `BaseEntity` with `string Name` property
    - Create `SoftDeletableTestEntity` — extends `BaseEntity`, implements `ISoftDeletable`, with `string Name` property
    - Create `TestDto` — a simple class with `Guid Id` and `string Name`
    - Create `TestDbContext` — EF Core DbContext with `UseInMemoryDatabase`, `DbSet<TestEntity>` and `DbSet<SoftDeletableTestEntity>`
    - Create `TestRepository` — concrete class extending `BaseRepository<TestEntity, TestDto>` with identity-like mapping delegates
    - Create `SoftDeletableTestRepository` — concrete class extending `BaseRepository<SoftDeletableTestEntity, TestDto>` with identity-like mapping delegates
    - _Requirements: 17.6_
  - Run `dotnet build groundup.sln` to verify compilation
  - Commit: "Add test infrastructure for repository tests"

- [ ] 8. Write ExpressionHelper property-based tests
  - [ ]* 8.1 Write property test for BuildPredicate string exact-match round-trip
    - Create `tests/GroundUp.Tests.Unit/Repositories/ExpressionHelperPropertyTests.cs`
    - **Property 1: BuildPredicate string exact-match round-trip**
    - For any non-null string value, BuildPredicate compiled and invoked against a matching entity returns true, against a non-matching entity returns false
    - Use `[Property(MaxTest = 100)]` attribute
    - **Validates: Requirements 4.3, 16.7**
  - [ ]* 8.2 Write property test for BuildPredicate Guid exact-match round-trip
    - **Property 2: BuildPredicate Guid exact-match round-trip**
    - For any Guid value, BuildPredicate with guid.ToString() compiled and invoked against a matching entity returns true, against a different Guid returns false
    - **Validates: Requirements 4.4**
  - [ ]* 8.3 Write property test for invalid property name safe default
    - **Property 3: Invalid property name produces safe default**
    - For any string that does not match a property name, all ExpressionHelper predicate methods return always-true, ApplySorting returns queryable unchanged
    - **Validates: Requirements 4.5, 5.3, 6.5, 7.3, 8.4**
  - [ ]* 8.4 Write property test for BuildContainsPredicate substring match
    - **Property 4: BuildContainsPredicate substring match**
    - For any non-null string value and entity whose string property contains that value (case-insensitively), returns true; for non-containing, returns false
    - **Validates: Requirements 5.2**
  - [ ]* 8.5 Write property test for BuildRangePredicate range correctness
    - **Property 5: BuildRangePredicate range correctness**
    - For any numeric value and optional min/max bounds, returns true iff value satisfies (min is null OR value >= min) AND (max is null OR value <= max)
    - **Validates: Requirements 6.2, 6.3, 6.4**
  - [ ]* 8.6 Write property test for BuildDateRangePredicate date range correctness
    - **Property 6: BuildDateRangePredicate date range correctness**
    - For any DateTime value and valid min/max date strings, returns true iff value satisfies the date range
    - **Validates: Requirements 7.2**
  - [ ]* 8.7 Write property test for ApplySorting order correctness
    - **Property 7: ApplySorting order correctness**
    - For any list of entities and valid property name, ascending sort returns entities in ascending order, "PropertyName desc" returns descending order
    - **Validates: Requirements 8.2, 8.3**
  - Run `dotnet test` to verify all property tests pass
  - Commit: "Add ExpressionHelper property-based tests (7 properties)"
  - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 16.6, 16.7, 16.8_

- [ ] 9. Checkpoint — Verify all ExpressionHelper tests pass
  - Ensure `dotnet test` passes with zero failures
  - Ensure all 7 property tests are green
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 10. Write BaseRepository unit tests
  - [ ]* 10.1 Write unit test: GetByIdAsync returns Ok when entity exists
    - Create `tests/GroundUp.Tests.Unit/Repositories/BaseRepositoryTests.cs`
    - Seed a TestEntity, call GetByIdAsync, assert OperationResult.Ok with mapped DTO
    - _Requirements: 17.1_
  - [ ]* 10.2 Write unit test: GetByIdAsync returns NotFound when entity missing
    - Call GetByIdAsync with non-existent Guid, assert OperationResult.NotFound
    - _Requirements: 17.2_
  - [ ]* 10.3 Write unit test: AddAsync persists entity and returns Ok with 201
    - Call AddAsync with a TestDto, assert entity persisted in DB, assert OperationResult.Ok with 201 status
    - _Requirements: 17.3_
  - [ ]* 10.4 Write unit test: AddAsync returns 409 Conflict on DbUpdateException
    - Trigger a duplicate key scenario, assert OperationResult.Fail with 409 and Conflict error code
    - _Requirements: 13.4_
  - [ ]* 10.5 Write unit test: UpdateAsync applies changes and returns Ok
    - Seed a TestEntity, call UpdateAsync with updated DTO, assert changes persisted
    - _Requirements: 14.1, 14.2_
  - [ ]* 10.6 Write unit test: UpdateAsync returns NotFound when entity missing
    - Call UpdateAsync with non-existent Guid, assert OperationResult.NotFound
    - _Requirements: 14.3_
  - [ ]* 10.7 Write unit test: UpdateAsync returns 409 Conflict on DbUpdateException
    - Trigger a concurrency conflict scenario, assert OperationResult.Fail with 409
    - _Requirements: 14.4_
  - [ ]* 10.8 Write unit test: DeleteAsync soft-deletes ISoftDeletable entity
    - Seed a SoftDeletableTestEntity, call DeleteAsync, assert IsDeleted=true and DeletedAt set, entity still in DB
    - _Requirements: 17.4_
  - [ ]* 10.9 Write unit test: DeleteAsync hard-deletes non-ISoftDeletable entity
    - Seed a TestEntity, call DeleteAsync, assert entity removed from DB
    - _Requirements: 17.5_
  - [ ]* 10.10 Write unit test: DeleteAsync returns NotFound when entity missing
    - Call DeleteAsync with non-existent Guid, assert OperationResult.NotFound
    - _Requirements: 15.4_
  - Run `dotnet test` to verify all unit tests pass
  - Commit: "Add BaseRepository unit tests (10 tests)"
  - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5, 17.6, 17.7_

- [ ] 11. Final checkpoint — Full solution build and test verification
  - Run `dotnet build groundup.sln` and verify zero errors
  - Run `dotnet test` and verify all tests pass (existing + new ExpressionHelper + new BaseRepository tests)
  - Verify file-scoped namespaces, nullable reference types, XML documentation, one-class-per-file across all new files
  - Ensure all tests pass, ask the user if questions arise.
  - Commit: "Phase 3A complete — all tests green"
  - _Requirements: 18.1, 18.2, 19.1, 19.2, 19.3, 19.4, 19.5, 19.6_

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each major component
- Property tests (tasks 8.1–8.7) validate the 7 correctness properties from the design document using FsCheck.Xunit
- Unit tests (tasks 10.1–10.10) validate BaseRepository CRUD behavior using EF Core InMemory
- BaseRepository tests use EF Core InMemory (acceptable for unit tests — integration tests in later phases use Testcontainers with real Postgres)
- No events are published by the repository — events are published by BaseService in Phase 3D
- Only DbUpdateException is caught — no catch-all exception handling
