# Requirements Document

## Introduction

Phase 3B of the GroundUp framework adds BaseTenantRepository&lt;TEntity, TDto&gt; to the GroundUp.Repositories project. This class extends BaseRepository&lt;TEntity, TDto&gt; (built in Phase 3A) with automatic tenant isolation — every query is filtered by the current tenant, and every mutation verifies tenant ownership before proceeding. The goal is transparent multi-tenancy: consuming code writes normal repository calls and tenant scoping happens automatically.

BaseTenantRepository uses a compile-time generic constraint (`where TEntity : BaseEntity, ITenantEntity`) to enforce that only tenant-aware entities can be used. There are no runtime type checks. Tenant filtering is applied via the existing queryShaper mechanism — the tenant filter wraps any derived class queryShaper so it always runs first. ITenantContext (already defined in GroundUp.Core) provides the current tenant identity.

Key design improvements over the previous GroundUp implementation: (1) generic constraint instead of runtime IsAssignableFrom checks, (2) single database call per operation — no double-fetch in GetByIdAsync, (3) Guid IDs instead of int IDs, (4) non-generic OperationResult for DeleteAsync, and (5) Mapperly mapping delegates instead of AutoMapper.

## Glossary

- **Repositories_Project**: The GroundUp.Repositories class library project containing base repository implementations. Depends on Core_Project, Data_Abstractions, and Events_Project.
- **Core_Project**: The GroundUp.Core class library project containing foundational shared types (BaseEntity, OperationResult, FilterParams, ITenantEntity, ITenantContext, etc.).
- **Unit_Test_Project**: The GroundUp.Tests.Unit xUnit test project for unit tests.
- **BaseTenantRepository**: A generic abstract class (BaseTenantRepository&lt;TEntity, TDto&gt;) extending BaseRepository&lt;TEntity, TDto&gt; that adds automatic tenant isolation to all CRUD operations.
- **BaseRepository**: The generic abstract class (BaseRepository&lt;TEntity, TDto&gt;) built in Phase 3A providing CRUD, filtering, paging, soft delete awareness, and queryShaper hooks.
- **ITenantEntity**: The opt-in interface in Core_Project declaring a Guid TenantId property. Entities implementing this interface are automatically scoped to the current tenant by BaseTenantRepository.
- **ITenantContext**: The abstraction in Core_Project providing the current tenant's Guid TenantId. Implementations are registered as scoped services and populated from request headers, JWT claims, or other sources.
- **QueryShaper**: A delegate of type Func&lt;IQueryable&lt;TEntity&gt;, IQueryable&lt;TEntity&gt;&gt; used by derived repositories to customize queries (e.g., include navigation properties, add extra filters).
- **TenantShaper**: A QueryShaper created by BaseTenantRepository that prepends a Where clause filtering by ITenantContext.TenantId before delegating to any derived class QueryShaper.
- **OperationResult**: The generic (OperationResult&lt;T&gt;) and non-generic (OperationResult) result types in Core_Project used as the single standardized return type.
- **BaseEntity**: The abstract base class in Core_Project providing a Guid Id property for all framework entities.
- **FilterParams**: The parameter class in Core_Project carrying filtering, sorting, and pagination criteria.
- **PaginatedData**: The generic wrapper (PaginatedData&lt;T&gt;) in Core_Project that holds a page of results with pagination metadata.
- **Mapperly**: A source-generator-based object mapper (Riok.Mapperly NuGet package) used for entity-to-DTO and DTO-to-entity mapping.
- **DbContext**: The EF Core database context class that BaseRepository depends on for data access.

## Requirements

### Requirement 1: BaseTenantRepository Class Definition and Generic Constraints

**User Story:** As a framework developer, I want a generic abstract tenant repository class with compile-time enforcement of ITenantEntity, so that only tenant-aware entities can be used and no runtime type checks are needed.

#### Acceptance Criteria

1. THE Repositories_Project SHALL contain an abstract BaseTenantRepository&lt;TEntity, TDto&gt; class in the GroundUp.Repositories namespace.
2. THE BaseTenantRepository SHALL extend BaseRepository&lt;TEntity, TDto&gt;.
3. THE BaseTenantRepository SHALL have generic constraints `where TEntity : BaseEntity, ITenantEntity` and `where TDto : class`.
4. THE BaseTenantRepository SHALL NOT contain any runtime type checks using typeof, IsAssignableFrom, or similar reflection-based type inspection for tenant enforcement.
5. THE BaseTenantRepository SHALL use a file-scoped namespace.
6. THE BaseTenantRepository SHALL have XML documentation comments on the class and all public and protected members.
7. THE BaseTenantRepository SHALL NOT use the sealed modifier because it is designed for inheritance by derived tenant repositories.

### Requirement 2: BaseTenantRepository Constructor and ITenantContext Dependency

**User Story:** As a framework developer, I want BaseTenantRepository to accept ITenantContext in addition to BaseRepository dependencies, so that it can access the current tenant identity for automatic filtering.

#### Acceptance Criteria

1. THE BaseTenantRepository SHALL accept an ITenantContext parameter via its constructor in addition to the DbContext and Mapperly mapping delegates required by BaseRepository.
2. THE BaseTenantRepository SHALL store the ITenantContext in a private readonly field for use in tenant filtering operations.
3. THE BaseTenantRepository SHALL pass the DbContext and mapping delegates to the BaseRepository base constructor.
4. THE BaseTenantRepository constructor SHALL have XML documentation comments describing all parameters.

### Requirement 3: Tenant-Filtered GetAllAsync

**User Story:** As a framework developer, I want GetAllAsync to automatically filter results by the current tenant, so that tenant isolation is enforced transparently on all list queries without consuming code needing to remember to filter.

#### Acceptance Criteria

1. WHEN GetAllAsync is called on BaseTenantRepository, THE BaseTenantRepository SHALL create a TenantShaper that applies a Where clause filtering entities by ITenantContext.TenantId matching the entity TenantId property.
2. WHEN GetAllAsync is called with a derived class QueryShaper, THE BaseTenantRepository SHALL apply the tenant filter first, then delegate to the derived class QueryShaper.
3. WHEN GetAllAsync is called without a derived class QueryShaper, THE BaseTenantRepository SHALL apply only the tenant filter.
4. THE BaseTenantRepository SHALL invoke the base class GetAllAsync with the composed TenantShaper, reusing all existing filtering, sorting, and paging logic from BaseRepository.
5. THE BaseTenantRepository SHALL execute the tenant filter and all other query operations in a single database call.

### Requirement 4: Tenant-Filtered GetByIdAsync

**User Story:** As a framework developer, I want GetByIdAsync to automatically scope lookups to the current tenant, so that an entity belonging to a different tenant is treated as not found.

#### Acceptance Criteria

1. WHEN GetByIdAsync is called on BaseTenantRepository, THE BaseTenantRepository SHALL create a TenantShaper that applies a Where clause filtering entities by ITenantContext.TenantId matching the entity TenantId property.
2. WHEN GetByIdAsync is called with a derived class QueryShaper, THE BaseTenantRepository SHALL apply the tenant filter first, then delegate to the derived class QueryShaper.
3. WHEN GetByIdAsync is called for an entity that exists but belongs to a different tenant, THE BaseTenantRepository SHALL return OperationResult&lt;TDto&gt;.NotFound.
4. THE BaseTenantRepository SHALL invoke the base class GetByIdAsync with the composed TenantShaper, executing the ID lookup and tenant filter in a single database call.

### Requirement 5: Tenant-Scoped AddAsync

**User Story:** As a framework developer, I want AddAsync to automatically set the TenantId on new entities before persisting, so that consuming code does not need to manually assign tenant ownership.

#### Acceptance Criteria

1. WHEN AddAsync is called on BaseTenantRepository, THE BaseTenantRepository SHALL set the TenantId property on the entity to ITenantContext.TenantId before persisting.
2. WHEN AddAsync is called with a DTO that maps to an entity where TenantId is already set to a non-default value, THE BaseTenantRepository SHALL overwrite the TenantId with ITenantContext.TenantId to prevent tenant spoofing.
3. THE BaseTenantRepository SHALL delegate to the base class AddAsync after setting the TenantId on the entity.
4. WHEN AddAsync completes successfully, THE BaseTenantRepository SHALL return OperationResult&lt;TDto&gt;.Ok with the created DTO including the assigned TenantId.

### Requirement 6: Tenant-Enforced UpdateAsync

**User Story:** As a framework developer, I want UpdateAsync to verify that the target entity belongs to the current tenant before applying changes, so that cross-tenant data modification is prevented.

#### Acceptance Criteria

1. WHEN UpdateAsync is called on BaseTenantRepository, THE BaseTenantRepository SHALL load the entity by ID and verify that the entity TenantId matches ITenantContext.TenantId.
2. WHEN UpdateAsync is called for an entity that exists but belongs to a different tenant, THE BaseTenantRepository SHALL return OperationResult&lt;TDto&gt;.NotFound.
3. WHEN UpdateAsync is called for an entity that does not exist, THE BaseTenantRepository SHALL return OperationResult&lt;TDto&gt;.NotFound.
4. WHEN UpdateAsync verifies tenant ownership successfully, THE BaseTenantRepository SHALL apply the DTO values to the entity, preserve the original TenantId, and call SaveChangesAsync.
5. THE BaseTenantRepository SHALL execute the entity lookup and update in a single tracked query (no separate AsNoTracking fetch followed by a second tracked fetch).
6. IF UpdateAsync encounters a DbUpdateException, THEN THE BaseTenantRepository SHALL return OperationResult&lt;TDto&gt;.Fail with a 409 status code and Conflict error code.

### Requirement 7: Tenant-Enforced DeleteAsync

**User Story:** As a framework developer, I want DeleteAsync to verify that the target entity belongs to the current tenant before deleting, so that cross-tenant data deletion is prevented.

#### Acceptance Criteria

1. WHEN DeleteAsync is called on BaseTenantRepository, THE BaseTenantRepository SHALL load the entity by ID and verify that the entity TenantId matches ITenantContext.TenantId.
2. WHEN DeleteAsync is called for an entity that exists but belongs to a different tenant, THE BaseTenantRepository SHALL return OperationResult.NotFound.
3. WHEN DeleteAsync is called for an entity that does not exist, THE BaseTenantRepository SHALL return OperationResult.NotFound.
4. WHEN DeleteAsync verifies tenant ownership successfully and the entity implements ISoftDeletable, THE BaseTenantRepository SHALL set IsDeleted to true and DeletedAt to DateTime.UtcNow and call SaveChangesAsync.
5. WHEN DeleteAsync verifies tenant ownership successfully and the entity does not implement ISoftDeletable, THE BaseTenantRepository SHALL remove the entity from the DbSet and call SaveChangesAsync.
6. WHEN DeleteAsync completes successfully, THE BaseTenantRepository SHALL return OperationResult.Ok.

### Requirement 8: Unit Tests for BaseTenantRepository GetAllAsync

**User Story:** As a framework developer, I want unit tests verifying that GetAllAsync filters results by the current tenant, so that I have confidence tenant isolation works correctly on list queries.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that GetAllAsync returns only entities matching the current tenant when entities from multiple tenants exist.
2. THE Unit_Test_Project SHALL contain a test verifying that GetAllAsync returns an empty result set when no entities match the current tenant.
3. THE Unit_Test_Project SHALL contain a test verifying that GetAllAsync applies the tenant filter before any derived class QueryShaper.
4. THE Unit_Test_Project SHALL contain a test verifying that GetAllAsync correctly applies FilterParams filtering and paging in combination with tenant filtering.
5. THE Unit_Test_Project SHALL use an EF Core in-memory database provider for isolated repository testing, consistent with Phase 3A conventions.
6. THE Unit_Test_Project SHALL use xUnit and NSubstitute, consistent with existing test conventions.

### Requirement 9: Unit Tests for BaseTenantRepository GetByIdAsync

**User Story:** As a framework developer, I want unit tests verifying that GetByIdAsync enforces tenant scoping, so that I have confidence cross-tenant entity access is prevented.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that GetByIdAsync returns OperationResult.Ok with the mapped DTO when the entity exists and belongs to the current tenant.
2. THE Unit_Test_Project SHALL contain a test verifying that GetByIdAsync returns OperationResult.NotFound when the entity exists but belongs to a different tenant.
3. THE Unit_Test_Project SHALL contain a test verifying that GetByIdAsync returns OperationResult.NotFound when the entity does not exist.
4. THE Unit_Test_Project SHALL contain a test verifying that GetByIdAsync applies the tenant filter and derived class QueryShaper together.

### Requirement 10: Unit Tests for BaseTenantRepository AddAsync

**User Story:** As a framework developer, I want unit tests verifying that AddAsync sets the TenantId automatically, so that I have confidence new entities are always assigned to the correct tenant.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that AddAsync sets the TenantId on the entity to ITenantContext.TenantId.
2. THE Unit_Test_Project SHALL contain a test verifying that AddAsync overwrites a pre-set TenantId with ITenantContext.TenantId to prevent tenant spoofing.
3. THE Unit_Test_Project SHALL contain a test verifying that AddAsync returns OperationResult.Ok with the created DTO including the correct TenantId.

### Requirement 11: Unit Tests for BaseTenantRepository UpdateAsync

**User Story:** As a framework developer, I want unit tests verifying that UpdateAsync enforces tenant ownership, so that I have confidence cross-tenant updates are prevented.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync returns OperationResult.Ok with the updated DTO when the entity belongs to the current tenant.
2. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync returns OperationResult.NotFound when the entity belongs to a different tenant.
3. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync returns OperationResult.NotFound when the entity does not exist.
4. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync preserves the original TenantId and does not allow it to be changed via the DTO.

### Requirement 12: Unit Tests for BaseTenantRepository DeleteAsync

**User Story:** As a framework developer, I want unit tests verifying that DeleteAsync enforces tenant ownership, so that I have confidence cross-tenant deletes are prevented.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync returns OperationResult.Ok when the entity belongs to the current tenant and is successfully deleted.
2. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync returns OperationResult.NotFound when the entity belongs to a different tenant.
3. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync returns OperationResult.NotFound when the entity does not exist.
4. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync performs a soft delete (sets IsDeleted and DeletedAt) for entities implementing ISoftDeletable.
5. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync performs a hard delete (removes from DbSet) for entities not implementing ISoftDeletable.

### Requirement 13: Property-Based Tests for Tenant Isolation

**User Story:** As a framework developer, I want property-based tests verifying tenant isolation invariants, so that I have high confidence the tenant boundary holds across a wide range of inputs.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a property-based test verifying that FOR ALL valid tenant ID pairs where tenantA differs from tenantB, GetAllAsync with ITenantContext.TenantId set to tenantA returns zero entities that have TenantId equal to tenantB (isolation invariant).
2. THE Unit_Test_Project SHALL contain a property-based test verifying that FOR ALL valid entity IDs and tenant ID pairs where the entity TenantId differs from ITenantContext.TenantId, GetByIdAsync returns NotFound (cross-tenant access invariant).
3. THE Unit_Test_Project SHALL contain a property-based test verifying that FOR ALL valid DTOs, AddAsync produces an entity whose TenantId equals ITenantContext.TenantId regardless of the TenantId value in the input DTO (tenant assignment invariant).
4. THE Unit_Test_Project SHALL use xUnit and FsCheck for property-based tests, consistent with existing test conventions.

### Requirement 14: Solution Build Verification

**User Story:** As a framework developer, I want the entire solution to compile after all Phase 3B changes, so that I know BaseTenantRepository integrates correctly with the existing BaseRepository and Core types.

#### Acceptance Criteria

1. WHEN `dotnet build groundup.sln` is executed after all Phase 3B changes, THE Solution SHALL compile with zero errors.
2. WHEN `dotnet test` is executed after all Phase 3B changes, THE Unit_Test_Project SHALL pass all tests including the new BaseTenantRepository tests and all existing Phase 3A tests.

### Requirement 15: Enforce Coding Conventions

**User Story:** As a framework developer, I want all Phase 3B types to follow established coding conventions, so that the tenant repository code is consistent with the rest of the framework.

#### Acceptance Criteria

1. THE BaseTenantRepository SHALL use a file-scoped namespace.
2. THE BaseTenantRepository SHALL enable nullable reference types.
3. THE BaseTenantRepository SHALL be placed in its own separate file (BaseTenantRepository.cs) in the Repositories_Project.
4. THE BaseTenantRepository SHALL NOT use the sealed modifier because it is designed for inheritance.
5. THE BaseTenantRepository SHALL have XML documentation comments on all public and protected members.
6. THE BaseTenantRepository SHALL use async/await throughout with no synchronous blocking calls (.Result, .Wait(), Task.Run).
7. THE BaseTenantRepository SHALL use DateTime.UtcNow for all timestamp assignments.