# Implementation Plan: Phase 3B — Tenant Repository

## Overview

Build `BaseTenantRepository<TEntity, TDto>` in GroundUp.Repositories, extending BaseRepository with automatic tenant isolation. Implementation follows: feature branch → single class file → test helpers → property-based tests (4 FsCheck properties) → example-based unit tests (20 tests) → final verification. Each task is a small compilable increment with a commit.

All code is C# targeting .NET 8, matching the design document.

## Tasks

- [x] 1. Create feature branch
  - Create and checkout branch `phase-3b/tenant-repository` from `main`
  - Run `dotnet build groundup.sln` to verify clean starting point
  - Commit is not needed — branch creation only
  - _Requirements: 14.1_

- [x] 2. Implement BaseTenantRepository class
  - [x] 2.1 Create `BaseTenantRepository<TEntity, TDto>` in `src/GroundUp.Repositories/BaseTenantRepository.cs`
    - Define in `GroundUp.Repositories` namespace with file-scoped namespace
    - Extend `BaseRepository<TEntity, TDto>`
    - Generic constraints: `where TEntity : BaseEntity, ITenantEntity` and `where TDto : class`
    - Do NOT use the `sealed` modifier — designed for inheritance
    - Constructor: accept `DbContext context`, `ITenantContext tenantContext`, `Func<TEntity, TDto> mapToDto`, `Func<TDto, TEntity> mapToEntity` — pass context and mapping delegates to base
    - Store `ITenantContext` in a `private readonly` field
    - Implement private `ComposeTenantShaper` method: captures `_tenantContext.TenantId` into a local variable, returns a `Func<IQueryable<TEntity>, IQueryable<TEntity>>` that prepends `Where(e => e.TenantId == tenantId)` before any derived queryShaper
    - Override protected `GetAllAsync(FilterParams, queryShaper?, CancellationToken)`: compose TenantShaper, delegate to `base.GetAllAsync`
    - Override protected `GetByIdAsync(Guid, queryShaper?, CancellationToken)`: compose TenantShaper, delegate to `base.GetByIdAsync`
    - Override public `AddAsync(TDto, CancellationToken)`: map DTO→entity, set `entity.TenantId = _tenantContext.TenantId`, add to DbSet, SaveChangesAsync, return Ok with 201. Catch `DbUpdateException` → 409 Conflict
    - Override public `UpdateAsync(Guid, TDto, CancellationToken)`: FindAsync, check null or wrong tenant → NotFound, apply DTO values via `SetValues`, preserve TenantId, SaveChangesAsync. Catch `DbUpdateException` → 409 Conflict
    - Override public `DeleteAsync(Guid, CancellationToken)`: FindAsync, check null or wrong tenant → NotFound, soft delete if `ISoftDeletable` (set IsDeleted=true, DeletedAt=DateTime.UtcNow), hard delete otherwise, SaveChangesAsync
    - Add XML documentation comments on the class, constructor, and all public/protected members
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 4.3, 4.4, 5.1, 5.2, 5.3, 5.4, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 15.1, 15.2, 15.3, 15.4, 15.5, 15.6, 15.7_
  - Run `dotnet build groundup.sln` to verify compilation
  - Commit: "Add BaseTenantRepository with tenant isolation for all CRUD operations"

- [x] 3. Checkpoint — Verify BaseTenantRepository compiles
  - Ensure `dotnet build groundup.sln` passes with zero errors
  - Ensure `BaseTenantRepository.cs` exists in `src/GroundUp.Repositories/`
  - Ensure all existing Phase 3A tests still pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Create tenant-aware test helpers
  - [x] 4.1 Create `TenantTestEntity` in `tests/GroundUp.Tests.Unit/Repositories/TestHelpers/TenantTestEntity.cs`
    - Extend `BaseEntity`, implement `ITenantEntity`
    - Properties: `string Name`, `Guid TenantId`
    - _Requirements: 8.5, 8.6_
  - [x] 4.2 Create `SoftDeletableTenantTestEntity` in `tests/GroundUp.Tests.Unit/Repositories/TestHelpers/SoftDeletableTenantTestEntity.cs`
    - Extend `BaseEntity`, implement `ITenantEntity` and `ISoftDeletable`
    - Properties: `string Name`, `Guid TenantId`, `bool IsDeleted`, `DateTime? DeletedAt`, `string? DeletedBy`
    - _Requirements: 12.4_
  - [x] 4.3 Create `TenantTestDto` in `tests/GroundUp.Tests.Unit/Repositories/TestHelpers/TenantTestDto.cs`
    - Properties: `Guid Id`, `string Name`, `Guid TenantId`
    - _Requirements: 8.5, 8.6_
  - [x] 4.4 Create `TenantTestRepository` in `tests/GroundUp.Tests.Unit/Repositories/TestHelpers/TenantTestRepository.cs`
    - Extend `BaseTenantRepository<TenantTestEntity, TenantTestDto>`
    - Constructor: accept `TestDbContext context`, `ITenantContext tenantContext` — pass identity-like mapping delegates to base
    - Static mapping methods: `MapEntityToDto` and `MapDtoToEntity` copying Id, Name, TenantId
    - _Requirements: 8.5, 8.6_
  - [x] 4.5 Create `SoftDeletableTenantTestRepository` in `tests/GroundUp.Tests.Unit/Repositories/TestHelpers/SoftDeletableTenantTestRepository.cs`
    - Extend `BaseTenantRepository<SoftDeletableTenantTestEntity, TenantTestDto>`
    - Constructor: accept `TestDbContext context`, `ITenantContext tenantContext`
    - Static mapping methods copying Id, Name, TenantId
    - _Requirements: 12.4_
  - [x] 4.6 Extend `TestDbContext` with new DbSets
    - Add `DbSet<TenantTestEntity> TenantTestEntities` property
    - Add `DbSet<SoftDeletableTenantTestEntity> SoftDeletableTenantTestEntities` property
    - Add entity configurations in `OnModelCreating` for both new entity types (HasKey, Name required/max length)
    - _Requirements: 8.5, 8.6_
  - Run `dotnet build groundup.sln` to verify compilation
  - Commit: "Add tenant-aware test helpers and extend TestDbContext"

- [x] 5. Checkpoint — Verify test helpers compile
  - Ensure `dotnet build groundup.sln` passes with zero errors
  - Ensure all 5 new test helper files exist in `tests/GroundUp.Tests.Unit/Repositories/TestHelpers/`
  - Ensure all existing tests still pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Write property-based tests for tenant isolation
  - [x]* 6.1 Write property test for GetAllAsync tenant isolation invariant
    - Create `tests/GroundUp.Tests.Unit/Repositories/BaseTenantRepositoryPropertyTests.cs`
    - **Property 1: GetAllAsync tenant isolation invariant**
    - For any two distinct tenant IDs and any set of entities split across both tenants, GetAllAsync with ITenantContext.TenantId set to tenantA returns zero entities whose TenantId equals tenantB
    - Use `[Property(MaxTest = 100)]` attribute
    - Use NSubstitute for ITenantContext mock
    - Use EF Core InMemory with fresh database per iteration
    - **Validates: Requirements 3.1, 3.3, 13.1**
  - [x]* 6.2 Write property test for GetByIdAsync cross-tenant access invariant
    - **Property 2: GetByIdAsync cross-tenant access invariant**
    - For any entity with a TenantId that differs from ITenantContext.TenantId, GetByIdAsync returns OperationResult.NotFound
    - **Validates: Requirements 4.1, 4.3, 13.2**
  - [x]* 6.3 Write property test for AddAsync tenant assignment invariant
    - **Property 3: AddAsync tenant assignment invariant**
    - For any valid DTO with any TenantId value (including Guid.Empty, random Guid, current tenant Guid), AddAsync produces a persisted entity whose TenantId equals ITenantContext.TenantId
    - **Validates: Requirements 5.1, 5.2, 5.4, 13.3**
  - [x]* 6.4 Write property test for UpdateAsync TenantId preservation invariant
    - **Property 4: UpdateAsync TenantId preservation invariant**
    - For any existing entity belonging to the current tenant and any DTO containing any TenantId value, UpdateAsync preserves the entity's original TenantId equal to ITenantContext.TenantId
    - **Validates: Requirements 6.4**
  - Run `dotnet test` to verify all property tests pass
  - Commit: "Add BaseTenantRepository property-based tests (4 properties)"
  - _Requirements: 13.1, 13.2, 13.3, 13.4_

- [x] 7. Checkpoint — Verify property tests pass
  - Ensure `dotnet test` passes with zero failures
  - Ensure all 4 property tests are green
  - Ensure all existing tests still pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Write unit tests for GetAllAsync and GetByIdAsync
  - [x]* 8.1 Write unit test: GetAllAsync_WithMultipleTenants_ReturnsOnlyCurrentTenantEntities
    - Create `tests/GroundUp.Tests.Unit/Repositories/BaseTenantRepositoryTests.cs`
    - Seed entities for tenantA and tenantB, set ITenantContext to tenantA, verify only tenantA entities returned
    - _Requirements: 8.1_
  - [x]* 8.2 Write unit test: GetAllAsync_WithNoCurrentTenantEntities_ReturnsEmptyResult
    - Seed entities for tenantB only, set ITenantContext to tenantA, verify empty PaginatedData
    - _Requirements: 8.2_
  - [x]* 8.3 Write unit test: GetAllAsync_WithQueryShaper_AppliesTenantFilterBeforeQueryShaper
    - Seed entities for both tenants, provide a queryShaper that filters by Name, verify tenant filter applied first
    - _Requirements: 8.3_
  - [x]* 8.4 Write unit test: GetAllAsync_WithFilterParamsAndPaging_CombinesWithTenantFilter
    - Seed multiple entities for current tenant, apply FilterParams with paging, verify correct page of tenant-scoped results
    - _Requirements: 8.4_
  - [x]* 8.5 Write unit test: GetByIdAsync_WithOwnTenantEntity_ReturnsOk
    - Seed entity for current tenant, call GetByIdAsync, verify OperationResult.Ok with mapped DTO
    - _Requirements: 9.1_
  - [x]* 8.6 Write unit test: GetByIdAsync_WithOtherTenantEntity_ReturnsNotFound
    - Seed entity for tenantB, set ITenantContext to tenantA, call GetByIdAsync with that entity's ID, verify NotFound
    - _Requirements: 9.2_
  - [x]* 8.7 Write unit test: GetByIdAsync_WithNonExistentId_ReturnsNotFound
    - Call GetByIdAsync with random Guid, verify NotFound
    - _Requirements: 9.3_
  - [x]* 8.8 Write unit test: GetByIdAsync_WithQueryShaper_AppliesTenantFilterAndQueryShaper
    - Seed entity for current tenant, provide a queryShaper, verify both tenant filter and queryShaper applied
    - _Requirements: 9.4_
  - Run `dotnet test` to verify all tests pass
  - Commit: "Add BaseTenantRepository GetAllAsync and GetByIdAsync unit tests"
  - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 9.1, 9.2, 9.3, 9.4_

- [x] 9. Write unit tests for AddAsync
  - [x]* 9.1 Write unit test: AddAsync_SetsEntityTenantIdToContextTenantId
    - Create DTO without TenantId set, call AddAsync, verify persisted entity has ITenantContext.TenantId
    - _Requirements: 10.1_
  - [x]* 9.2 Write unit test: AddAsync_OverwritesPreSetTenantId_PreventsSpoof
    - Create DTO with TenantId set to a different tenant, call AddAsync, verify persisted entity has ITenantContext.TenantId (not the spoofed value)
    - _Requirements: 10.2_
  - [x]* 9.3 Write unit test: AddAsync_ReturnsOkWithCorrectTenantIdInDto
    - Call AddAsync, verify returned DTO in OperationResult has correct TenantId and 201 status
    - _Requirements: 10.3_
  - Run `dotnet test` to verify all tests pass
  - Commit: "Add BaseTenantRepository AddAsync unit tests"
  - _Requirements: 10.1, 10.2, 10.3_

- [x] 10. Write unit tests for UpdateAsync
  - [x]* 10.1 Write unit test: UpdateAsync_WithOwnTenantEntity_ReturnsOkWithUpdatedDto
    - Seed entity for current tenant, call UpdateAsync with changed Name, verify OperationResult.Ok with updated DTO
    - _Requirements: 11.1_
  - [x]* 10.2 Write unit test: UpdateAsync_WithOtherTenantEntity_ReturnsNotFound
    - Seed entity for tenantB, set ITenantContext to tenantA, call UpdateAsync, verify NotFound
    - _Requirements: 11.2_
  - [x]* 10.3 Write unit test: UpdateAsync_WithNonExistentId_ReturnsNotFound
    - Call UpdateAsync with random Guid, verify NotFound
    - _Requirements: 11.3_
  - [x]* 10.4 Write unit test: UpdateAsync_PreservesOriginalTenantId_IgnoresDtoTenantId
    - Seed entity for current tenant, call UpdateAsync with DTO containing different TenantId, verify persisted entity still has original TenantId
    - _Requirements: 11.4_
  - Run `dotnet test` to verify all tests pass
  - Commit: "Add BaseTenantRepository UpdateAsync unit tests"
  - _Requirements: 11.1, 11.2, 11.3, 11.4_

- [x] 11. Write unit tests for DeleteAsync
  - [x]* 11.1 Write unit test: DeleteAsync_WithOwnTenantEntity_ReturnsOk
    - Seed non-ISoftDeletable entity for current tenant, call DeleteAsync, verify OperationResult.Ok and entity removed from DB
    - _Requirements: 12.1_
  - [x]* 11.2 Write unit test: DeleteAsync_WithOtherTenantEntity_ReturnsNotFound
    - Seed entity for tenantB, set ITenantContext to tenantA, call DeleteAsync, verify NotFound and entity still in DB
    - _Requirements: 12.2_
  - [x]* 11.3 Write unit test: DeleteAsync_WithNonExistentId_ReturnsNotFound
    - Call DeleteAsync with random Guid, verify NotFound
    - _Requirements: 12.3_
  - [x]* 11.4 Write unit test: DeleteAsync_WithSoftDeletableTenantEntity_SetsIsDeletedAndDeletedAt
    - Seed SoftDeletableTenantTestEntity for current tenant, call DeleteAsync via SoftDeletableTenantTestRepository, verify IsDeleted=true and DeletedAt set, entity still in DB
    - _Requirements: 12.4_
  - [x]* 11.5 Write unit test: DeleteAsync_WithNonSoftDeletableTenantEntity_RemovesFromDbSet
    - Seed TenantTestEntity for current tenant, call DeleteAsync, verify entity removed from DB
    - _Requirements: 12.5_
  - Run `dotnet test` to verify all tests pass
  - Commit: "Add BaseTenantRepository DeleteAsync unit tests"
  - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

- [-] 12. Final checkpoint — Full solution build and test verification
  - Run `dotnet build groundup.sln` and verify zero errors
  - Run `dotnet test` and verify all tests pass (existing Phase 3A tests + new Phase 3B tests)
  - Verify file-scoped namespaces, nullable reference types, XML documentation, one-class-per-file across all new files
  - Ensure all tests pass, ask the user if questions arise.
  - Commit: "Phase 3B complete — all tests green"
  - _Requirements: 14.1, 14.2, 15.1, 15.2, 15.3, 15.4, 15.5, 15.6, 15.7_

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each major component
- Property tests (tasks 6.1–6.4) validate the 4 correctness properties from the design document using FsCheck.Xunit
- Unit tests (tasks 8.1–11.5) validate specific CRUD behavior with concrete database state (20 tests total)
- All tests use EF Core InMemory for unit test isolation — integration tests with real Postgres come in later phases
- ITenantContext is mocked via NSubstitute, consistent with project conventions
- No events are published by the repository — events are published by BaseService in Phase 3D
- Only DbUpdateException is caught — no catch-all exception handling
- Cross-tenant access returns NotFound (not Forbidden) to prevent information leakage
