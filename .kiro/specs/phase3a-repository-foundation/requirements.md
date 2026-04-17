# Requirements Document

## Introduction

Phase 3A of the GroundUp framework builds the repository interfaces in GroundUp.Data.Abstractions and the base repository implementation (including ExpressionHelper) in GroundUp.Repositories. This is the first of six sub-phases that together deliver the full data access layer. Phase 3A focuses exclusively on the abstractions and base implementation — EF Core provider setup (DbContext, interceptors, Postgres), services, controllers, and the sample app are deferred to later sub-phases (3B through 3F).

The repository layer is the data access boundary of the framework. All public methods return OperationResult&lt;T&gt; or OperationResult (non-generic) — business logic errors are never communicated via exceptions. The base repository provides generic CRUD with filtering, sorting, paging, soft delete awareness, and queryShaper hooks for derived repository customization. ExpressionHelper translates FilterParams dictionaries into LINQ expressions dynamically, keeping the repository layer database-agnostic.

## Glossary

- **Data_Abstractions**: The GroundUp.Data.Abstractions class library project containing repository and data access interfaces. Depends only on Core_Project.
- **Repositories_Project**: The GroundUp.Repositories class library project containing base repository implementations. Depends on Core_Project, Data_Abstractions, and Events_Project.
- **Core_Project**: The GroundUp.Core class library project containing foundational shared types (BaseEntity, OperationResult, FilterParams, PaginatedData, ISoftDeletable, etc.).
- **Unit_Test_Project**: The GroundUp.Tests.Unit xUnit test project for unit tests.
- **IBaseRepository**: A generic interface (IBaseRepository&lt;TDto&gt;) defining the standard CRUD contract that all repositories implement.
- **IUnitOfWork**: An interface providing transactional execution of multiple repository operations via ExecuteInTransactionAsync.
- **IDataSeeder**: An interface for reference data seeding on application startup, with an idempotent SeedAsync method.
- **ExpressionHelper**: A static class in Repositories_Project that builds LINQ expressions dynamically from FilterParams dictionaries for exact match, contains, range, date range, and sorting.
- **BaseRepository**: A generic abstract class (BaseRepository&lt;TEntity, TDto&gt;) implementing IBaseRepository&lt;TDto&gt; with CRUD, filtering, paging, soft delete awareness, and queryShaper hooks.
- **QueryShaper**: A delegate of type Func&lt;IQueryable&lt;TEntity&gt;, IQueryable&lt;TEntity&gt;&gt; used by derived repositories to customize queries (e.g., include navigation properties, add extra filters).
- **FilterParams**: The existing parameter class in Core_Project carrying exact match, contains, min/max range, multi-value, and search term filtering criteria along with pagination and sorting.
- **OperationResult**: The generic (OperationResult&lt;T&gt;) and non-generic (OperationResult) result types in Core_Project used as the single standardized return type.
- **PaginatedData**: The generic wrapper (PaginatedData&lt;T&gt;) in Core_Project that holds a page of results with pagination metadata.
- **ISoftDeletable**: The opt-in interface in Core_Project that entities implement to enable soft delete behavior.
- **BaseEntity**: The abstract base class in Core_Project providing a Guid Id property for all framework entities.
- **Mapperly**: A source-generator-based object mapper (Riok.Mapperly NuGet package) used for entity-to-DTO and DTO-to-entity mapping. NOT AutoMapper.
- **DbContext**: The EF Core database context class that BaseRepository depends on for data access. Provided by Microsoft.EntityFrameworkCore.

## Requirements

### Requirement 1: Define IBaseRepository Interface

**User Story:** As a framework developer, I want a generic repository interface with standard CRUD methods returning OperationResult, so that the service layer can depend on a consistent data access contract without coupling to a specific implementation.

#### Acceptance Criteria

1. THE Data_Abstractions SHALL contain an IBaseRepository&lt;TDto&gt; interface in the GroundUp.Data.Abstractions namespace.
2. THE IBaseRepository SHALL define a method GetAllAsync that accepts a FilterParams parameter and a CancellationToken and returns Task&lt;OperationResult&lt;PaginatedData&lt;TDto&gt;&gt;&gt;.
3. THE IBaseRepository SHALL define a method GetByIdAsync that accepts a Guid parameter and a CancellationToken and returns Task&lt;OperationResult&lt;TDto&gt;&gt;.
4. THE IBaseRepository SHALL define a method AddAsync that accepts a TDto parameter and a CancellationToken and returns Task&lt;OperationResult&lt;TDto&gt;&gt;.
5. THE IBaseRepository SHALL define a method UpdateAsync that accepts a Guid parameter and a TDto parameter and a CancellationToken and returns Task&lt;OperationResult&lt;TDto&gt;&gt;.
6. THE IBaseRepository SHALL define a method DeleteAsync that accepts a Guid parameter and a CancellationToken and returns Task&lt;OperationResult&gt; (non-generic).
7. THE IBaseRepository SHALL have XML documentation comments on the interface and all methods.
8. THE IBaseRepository SHALL use a file-scoped namespace.

### Requirement 2: Define IUnitOfWork Interface

**User Story:** As a framework developer, I want a unit of work interface with transactional execution, so that services can wrap multiple repository operations in a single database transaction.

#### Acceptance Criteria

1. THE Data_Abstractions SHALL contain an IUnitOfWork interface in the GroundUp.Data.Abstractions namespace.
2. THE IUnitOfWork SHALL define a method ExecuteInTransactionAsync that accepts a Func&lt;CancellationToken, Task&gt; parameter and a CancellationToken and returns Task&lt;OperationResult&gt; (non-generic).
3. THE IUnitOfWork SHALL have XML documentation comments on the interface and the method.
4. THE IUnitOfWork SHALL use a file-scoped namespace.

### Requirement 3: Define IDataSeeder Interface

**User Story:** As a framework developer, I want a data seeder interface, so that modules and consuming applications can register idempotent seed operations that run on application startup.

#### Acceptance Criteria

1. THE Data_Abstractions SHALL contain an IDataSeeder interface in the GroundUp.Data.Abstractions namespace.
2. THE IDataSeeder SHALL define a method SeedAsync that accepts a CancellationToken and returns Task.
3. THE IDataSeeder SHALL define a read-only int Order property for controlling seeder execution order, where lower values execute first.
4. THE IDataSeeder SHALL have XML documentation comments on the interface, the SeedAsync method, and the Order property.
5. THE IDataSeeder SHALL use a file-scoped namespace.

### Requirement 4: Build ExpressionHelper — Exact Match Predicate

**User Story:** As a framework developer, I want a method that builds an exact-match LINQ expression from a property name and value string, so that BaseRepository can dynamically filter entities by FilterParams.Filters entries.

#### Acceptance Criteria

1. THE Repositories_Project SHALL contain a static ExpressionHelper class in the GroundUp.Repositories namespace.
2. THE ExpressionHelper SHALL provide a static BuildPredicate&lt;T&gt; method that accepts a string property name and a string value and returns an Expression&lt;Func&lt;T, bool&gt;&gt;.
3. WHEN BuildPredicate is called with a valid property name, THE ExpressionHelper SHALL return an expression that compares the entity property to the provided value using case-insensitive string comparison for string properties.
4. WHEN BuildPredicate is called with a valid property name for a non-string property (Guid, int, DateTime, bool, enum), THE ExpressionHelper SHALL parse the value string and return an equality expression for the parsed typed value.
5. IF BuildPredicate is called with a property name that does not exist on type T, THEN THE ExpressionHelper SHALL return a predicate that always evaluates to true (no filtering applied).
6. THE ExpressionHelper SHALL have XML documentation comments on the class and all public methods.
7. THE ExpressionHelper SHALL use a file-scoped namespace.

### Requirement 5: Build ExpressionHelper — Contains Predicate

**User Story:** As a framework developer, I want a method that builds a substring-match LINQ expression, so that BaseRepository can dynamically filter entities by FilterParams.ContainsFilters entries.

#### Acceptance Criteria

1. THE ExpressionHelper SHALL provide a static BuildContainsPredicate&lt;T&gt; method that accepts a string property name and a string value and returns an Expression&lt;Func&lt;T, bool&gt;&gt;.
2. WHEN BuildContainsPredicate is called with a valid string property name, THE ExpressionHelper SHALL return an expression that checks whether the entity property contains the provided value using case-insensitive comparison.
3. IF BuildContainsPredicate is called with a property name that does not exist on type T or refers to a non-string property, THEN THE ExpressionHelper SHALL return a predicate that always evaluates to true.

### Requirement 6: Build ExpressionHelper — Range Predicate

**User Story:** As a framework developer, I want a method that builds a min/max range LINQ expression, so that BaseRepository can dynamically filter entities by FilterParams.MinFilters and MaxFilters entries.

#### Acceptance Criteria

1. THE ExpressionHelper SHALL provide a static BuildRangePredicate&lt;T&gt; method that accepts a string property name, an optional string minValue, and an optional string maxValue and returns an Expression&lt;Func&lt;T, bool&gt;&gt;.
2. WHEN BuildRangePredicate is called with both minValue and maxValue, THE ExpressionHelper SHALL return an expression that checks the entity property is greater than or equal to minValue AND less than or equal to maxValue.
3. WHEN BuildRangePredicate is called with only minValue, THE ExpressionHelper SHALL return an expression that checks the entity property is greater than or equal to minValue.
4. WHEN BuildRangePredicate is called with only maxValue, THE ExpressionHelper SHALL return an expression that checks the entity property is less than or equal to maxValue.
5. IF BuildRangePredicate is called with a property name that does not exist on type T, THEN THE ExpressionHelper SHALL return a predicate that always evaluates to true.

### Requirement 7: Build ExpressionHelper — Date Range Predicate

**User Story:** As a framework developer, I want a date-specific range predicate method, so that BaseRepository can filter entities by date ranges with proper DateTime parsing.

#### Acceptance Criteria

1. THE ExpressionHelper SHALL provide a static BuildDateRangePredicate&lt;T&gt; method that accepts a string property name, an optional string minDate, and an optional string maxDate and returns an Expression&lt;Func&lt;T, bool&gt;&gt;.
2. WHEN BuildDateRangePredicate is called with valid date strings, THE ExpressionHelper SHALL parse the strings as DateTime values and return a range expression using greater-than-or-equal and less-than-or-equal comparisons.
3. IF BuildDateRangePredicate is called with a property name that does not exist on type T or refers to a non-DateTime property, THEN THE ExpressionHelper SHALL return a predicate that always evaluates to true.
4. IF BuildDateRangePredicate is called with an unparseable date string, THEN THE ExpressionHelper SHALL return a predicate that always evaluates to true.

### Requirement 8: Build ExpressionHelper — Dynamic Sorting

**User Story:** As a framework developer, I want a method that applies dynamic sorting from a string property name, so that BaseRepository can sort query results based on FilterParams.SortBy.

#### Acceptance Criteria

1. THE ExpressionHelper SHALL provide a static ApplySorting&lt;T&gt; method that accepts an IQueryable&lt;T&gt; and a string sortExpression and returns IQueryable&lt;T&gt;.
2. WHEN ApplySorting is called with a sort expression containing only a property name (e.g., "Name"), THE ExpressionHelper SHALL return the queryable ordered ascending by that property.
3. WHEN ApplySorting is called with a sort expression containing a property name followed by "desc" (e.g., "Name desc"), THE ExpressionHelper SHALL return the queryable ordered descending by that property.
4. IF ApplySorting is called with a property name that does not exist on type T, THEN THE ExpressionHelper SHALL return the queryable unchanged (no sorting applied).
5. IF ApplySorting is called with a null or empty sort expression, THEN THE ExpressionHelper SHALL return the queryable unchanged.

### Requirement 9: Add EF Core NuGet Dependency to Repositories Project

**User Story:** As a framework developer, I want the Repositories project to reference the Microsoft.EntityFrameworkCore NuGet package, so that BaseRepository can use DbContext, DbSet, and IQueryable extension methods without provider-specific code.

#### Acceptance Criteria

1. THE Repositories_Project SHALL include a NuGet package reference for Microsoft.EntityFrameworkCore.
2. THE Repositories_Project SHALL include a NuGet package reference for Riok.Mapperly.
3. THE Repositories_Project SHALL retain its existing project references to Core_Project, Data_Abstractions, and Events_Project.
4. THE Repositories_Project SHALL contain no provider-specific NuGet packages (no Npgsql, no SqlServer).

### Requirement 10: Build BaseRepository — Constructor and Dependencies

**User Story:** As a framework developer, I want a generic abstract base repository class that accepts a DbContext and Mapperly-based mapping delegates, so that derived repositories inherit CRUD infrastructure without boilerplate.

#### Acceptance Criteria

1. THE Repositories_Project SHALL contain an abstract BaseRepository&lt;TEntity, TDto&gt; class in the GroundUp.Repositories namespace where TEntity : BaseEntity and TDto : class.
2. THE BaseRepository SHALL implement IBaseRepository&lt;TDto&gt;.
3. THE BaseRepository SHALL accept a DbContext via constructor injection.
4. THE BaseRepository SHALL accept mapping delegates (Func&lt;TEntity, TDto&gt; for entity-to-DTO and Func&lt;TDto, TEntity&gt; for DTO-to-entity) via constructor parameters, enabling Mapperly-generated mappers to be passed in by derived classes.
5. THE BaseRepository SHALL expose a protected DbSet&lt;TEntity&gt; property for derived class access.
6. THE BaseRepository SHALL have XML documentation comments on the class, constructor, and all public and protected members.
7. THE BaseRepository SHALL use a file-scoped namespace.

### Requirement 11: Build BaseRepository — GetAllAsync with Filtering and Paging

**User Story:** As a framework developer, I want GetAllAsync to apply FilterParams-based filtering, sorting, and paging via ExpressionHelper, so that all repositories get dynamic query capabilities without custom code.

#### Acceptance Criteria

1. WHEN GetAllAsync is called, THE BaseRepository SHALL apply exact-match filters from FilterParams.Filters using ExpressionHelper.BuildPredicate.
2. WHEN GetAllAsync is called, THE BaseRepository SHALL apply substring-match filters from FilterParams.ContainsFilters using ExpressionHelper.BuildContainsPredicate.
3. WHEN GetAllAsync is called, THE BaseRepository SHALL apply range filters from FilterParams.MinFilters and MaxFilters using ExpressionHelper.BuildRangePredicate.
4. WHEN GetAllAsync is called with a non-null SortBy in FilterParams, THE BaseRepository SHALL apply sorting using ExpressionHelper.ApplySorting.
5. WHEN GetAllAsync is called, THE BaseRepository SHALL apply pagination using PageNumber and PageSize from FilterParams and return a PaginatedData&lt;TDto&gt; with correct TotalRecords count.
6. WHEN GetAllAsync is called, THE BaseRepository SHALL use AsNoTracking() for the read-only query.
7. WHEN GetAllAsync is called, THE BaseRepository SHALL accept an optional QueryShaper (Func&lt;IQueryable&lt;TEntity&gt;, IQueryable&lt;TEntity&gt;&gt;?) parameter for derived class query customization.
8. WHEN GetAllAsync completes, THE BaseRepository SHALL return OperationResult&lt;PaginatedData&lt;TDto&gt;&gt;.Ok with the paginated results.

### Requirement 12: Build BaseRepository — GetByIdAsync

**User Story:** As a framework developer, I want GetByIdAsync to retrieve a single entity by its Guid ID and return it as a DTO, so that services can fetch individual records through a consistent interface.

#### Acceptance Criteria

1. WHEN GetByIdAsync is called with a valid ID that matches an existing entity, THE BaseRepository SHALL return OperationResult&lt;TDto&gt;.Ok with the mapped DTO.
2. WHEN GetByIdAsync is called with an ID that does not match any entity, THE BaseRepository SHALL return OperationResult&lt;TDto&gt;.NotFound.
3. WHEN GetByIdAsync is called, THE BaseRepository SHALL use AsNoTracking() for the read-only query.
4. WHEN GetByIdAsync is called, THE BaseRepository SHALL accept an optional QueryShaper parameter for derived class query customization.

### Requirement 13: Build BaseRepository — AddAsync

**User Story:** As a framework developer, I want AddAsync to map a DTO to an entity, persist it, and return the created DTO, so that services can create new records through a consistent interface.

#### Acceptance Criteria

1. WHEN AddAsync is called with a valid TDto, THE BaseRepository SHALL map the DTO to a TEntity using the DTO-to-entity mapping delegate.
2. WHEN AddAsync is called, THE BaseRepository SHALL add the entity to the DbSet and call SaveChangesAsync on the DbContext.
3. WHEN AddAsync completes successfully, THE BaseRepository SHALL return OperationResult&lt;TDto&gt;.Ok with the mapped DTO of the persisted entity (including any database-generated values).
4. IF AddAsync encounters a DbUpdateException, THEN THE BaseRepository SHALL return OperationResult&lt;TDto&gt;.Fail with a 409 status code and Conflict error code.

### Requirement 14: Build BaseRepository — UpdateAsync

**User Story:** As a framework developer, I want UpdateAsync to find an existing entity by ID, apply DTO values, persist changes, and return the updated DTO, so that services can update records through a consistent interface.

#### Acceptance Criteria

1. WHEN UpdateAsync is called with a valid ID that matches an existing entity, THE BaseRepository SHALL apply the TDto values to the tracked entity and call SaveChangesAsync.
2. WHEN UpdateAsync completes successfully, THE BaseRepository SHALL return OperationResult&lt;TDto&gt;.Ok with the mapped DTO of the updated entity.
3. WHEN UpdateAsync is called with an ID that does not match any entity, THE BaseRepository SHALL return OperationResult&lt;TDto&gt;.NotFound.
4. IF UpdateAsync encounters a DbUpdateException, THEN THE BaseRepository SHALL return OperationResult&lt;TDto&gt;.Fail with a 409 status code and Conflict error code.

### Requirement 15: Build BaseRepository — DeleteAsync with Soft Delete Awareness

**User Story:** As a framework developer, I want DeleteAsync to perform a soft delete for ISoftDeletable entities and a hard delete for all others, so that delete behavior is determined by the entity's opt-in interfaces.

#### Acceptance Criteria

1. WHEN DeleteAsync is called with an ID that matches an existing entity implementing ISoftDeletable, THE BaseRepository SHALL set IsDeleted to true and DeletedAt to DateTime.UtcNow on the entity and call SaveChangesAsync.
2. WHEN DeleteAsync is called with an ID that matches an existing entity that does not implement ISoftDeletable, THE BaseRepository SHALL remove the entity from the DbSet and call SaveChangesAsync.
3. WHEN DeleteAsync completes successfully, THE BaseRepository SHALL return OperationResult.Ok.
4. WHEN DeleteAsync is called with an ID that does not match any entity, THE BaseRepository SHALL return OperationResult.NotFound.

### Requirement 16: Unit Tests for ExpressionHelper

**User Story:** As a framework developer, I want unit tests verifying ExpressionHelper expression building, so that I have confidence that dynamic filtering and sorting produce correct LINQ expressions.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain tests verifying that BuildPredicate produces a correct exact-match expression for string properties with case-insensitive comparison.
2. THE Unit_Test_Project SHALL contain tests verifying that BuildPredicate produces a correct equality expression for Guid properties.
3. THE Unit_Test_Project SHALL contain tests verifying that BuildContainsPredicate produces a correct substring-match expression for string properties.
4. THE Unit_Test_Project SHALL contain tests verifying that BuildRangePredicate produces correct range expressions for numeric properties with min-only, max-only, and both min and max values.
5. THE Unit_Test_Project SHALL contain tests verifying that ApplySorting applies ascending and descending ordering correctly.
6. THE Unit_Test_Project SHALL contain tests verifying that ExpressionHelper methods return safe defaults (always-true predicate or unchanged queryable) when given invalid property names.
7. THE Unit_Test_Project SHALL contain a property-based test verifying that FOR ALL valid string values, BuildPredicate followed by compilation and invocation produces the same result as a direct case-insensitive string comparison (round-trip correctness).
8. THE Unit_Test_Project SHALL use xUnit and FsCheck for property-based tests, consistent with existing test conventions.

### Requirement 17: Unit Tests for BaseRepository

**User Story:** As a framework developer, I want unit tests verifying BaseRepository CRUD behavior, so that I have confidence that the generic repository correctly handles success paths, not-found cases, and soft delete logic.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that GetByIdAsync returns OperationResult.Ok with the mapped DTO when the entity exists.
2. THE Unit_Test_Project SHALL contain a test verifying that GetByIdAsync returns OperationResult.NotFound when the entity does not exist.
3. THE Unit_Test_Project SHALL contain a test verifying that AddAsync persists the entity and returns OperationResult.Ok with the mapped DTO.
4. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync sets IsDeleted to true for entities implementing ISoftDeletable.
5. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync removes the entity from the DbSet for entities not implementing ISoftDeletable.
6. THE Unit_Test_Project SHALL use an EF Core in-memory database provider for isolated repository testing (acceptable for unit tests; integration tests in later phases use Testcontainers with real Postgres).
7. THE Unit_Test_Project SHALL use xUnit and NSubstitute, consistent with existing test conventions.

### Requirement 18: Solution Build Verification

**User Story:** As a framework developer, I want the entire solution to compile after all Phase 3A changes, so that I know the new interfaces and implementations integrate correctly with the existing codebase.

#### Acceptance Criteria

1. WHEN `dotnet build groundup.sln` is executed after all Phase 3A changes, THE Solution SHALL compile with zero errors.
2. WHEN `dotnet test` is executed after all Phase 3A changes, THE Unit_Test_Project SHALL pass all tests including the new ExpressionHelper and BaseRepository tests.

### Requirement 19: Enforce Coding Conventions

**User Story:** As a framework developer, I want all Phase 3A types to follow established coding conventions, so that the repository layer code is consistent with the rest of the framework.

#### Acceptance Criteria

1. THE Data_Abstractions and Repositories_Project SHALL use file-scoped namespaces in all source files.
2. THE Data_Abstractions and Repositories_Project SHALL enable nullable reference types.
3. THE Data_Abstractions and Repositories_Project SHALL place each class and interface in its own separate file.
4. THE ExpressionHelper SHALL use the sealed modifier (static classes are implicitly sealed in C#, but the class declaration SHALL use the static modifier).
5. THE BaseRepository SHALL NOT use the sealed modifier because it is designed for inheritance by derived repositories.
6. THE Data_Abstractions and Repositories_Project SHALL have XML documentation comments on all public types and members.
