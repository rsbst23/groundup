# Requirements Document

## Introduction

Phase 3C of the GroundUp framework builds the EF Core infrastructure in GroundUp.Data.Postgres — the ONLY project containing Postgres-specific code. This phase delivers the abstract GroundUpDbContext that consuming applications inherit from, SaveChanges interceptors for automatic audit field population and soft delete conversion, global query filters for ISoftDeletable entities, UUID v7 default value generation via the UUIDNext NuGet package, Postgres-specific unique constraint violation detection, a DataSeederRunner hosted service, and the AddGroundUpPostgres registration extension method.

GroundUp.Data.Postgres provides the configured DbContext that BaseRepository (in GroundUp.Repositories) operates on. Consuming applications create their own DbContext inheriting from GroundUpDbContext, and BaseRepository receives that DbContext via constructor injection. Interceptors are registered as singletons but resolve scoped services (ICurrentUser) from the current DI scope via IServiceProvider. Entity configurations use Fluent API exclusively — never data annotations.

## Glossary

- **Data_Postgres_Project**: The GroundUp.Data.Postgres class library project containing all Postgres-specific EF Core infrastructure. Depends on Core_Project, Data_Abstractions, and Repositories_Project.
- **Core_Project**: The GroundUp.Core class library project containing foundational shared types (BaseEntity, IAuditable, ISoftDeletable, ICurrentUser, etc.).
- **Data_Abstractions**: The GroundUp.Data.Abstractions class library project containing repository and data access interfaces (IDataSeeder, IUnitOfWork).
- **Repositories_Project**: The GroundUp.Repositories class library project containing BaseRepository.
- **Unit_Test_Project**: The GroundUp.Tests.Unit xUnit test project for unit tests.
- **GroundUpDbContext**: An abstract class extending Microsoft.EntityFrameworkCore.DbContext that consuming applications inherit from. Configures entity conventions, applies global query filters for ISoftDeletable, and generates UUID v7 default values for BaseEntity.Id.
- **AuditableInterceptor**: A SaveChangesInterceptor that auto-populates IAuditable fields (CreatedAt/CreatedBy on Added entities, UpdatedAt/UpdatedBy on Modified entities) using ICurrentUser resolved from the current DI scope.
- **SoftDeleteInterceptor**: A SaveChangesInterceptor that converts Remove() calls on ISoftDeletable entities to soft deletes (sets IsDeleted to true, DeletedAt to DateTime.UtcNow, DeletedBy from ICurrentUser). Acts as a safety net for direct DbContext usage — BaseRepository already handles soft delete explicitly.
- **ICurrentUser**: The abstraction in Core_Project providing the authenticated user's identity (UserId, Email, DisplayName). Registered as a scoped service.
- **IAuditable**: The opt-in interface in Core_Project with CreatedAt, CreatedBy, UpdatedAt, UpdatedBy properties.
- **ISoftDeletable**: The opt-in interface in Core_Project with IsDeleted, DeletedAt, DeletedBy properties.
- **BaseEntity**: The abstract base class in Core_Project providing a Guid Id property for all framework entities.
- **IDataSeeder**: The interface in Data_Abstractions with an Order property and an idempotent SeedAsync method.
- **DataSeederRunner**: An IHostedService that discovers all IDataSeeder implementations from DI, orders them by Order property, and runs SeedAsync on each during application startup.
- **UUIDNext**: A NuGet package providing UUID v7 generation for .NET 8 (which lacks Guid.CreateVersion7()).
- **PostgresErrorHelper**: A static helper class that detects Postgres-specific unique constraint violations (error code 23505) from DbUpdateException.
- **AddGroundUpPostgres**: An extension method on IServiceCollection that registers the DbContext with Npgsql, registers interceptors, and registers DataSeederRunner.

## Requirements

### Requirement 1: GroundUpDbContext Base Class

**User Story:** As a consuming application developer, I want an abstract base DbContext that configures entity conventions, global query filters, and UUID v7 generation, so that I can inherit from it and get framework infrastructure without manual setup.

#### Acceptance Criteria

1. THE Data_Postgres_Project SHALL contain an abstract GroundUpDbContext class in the GroundUp.Data.Postgres namespace that extends Microsoft.EntityFrameworkCore.DbContext.
2. THE GroundUpDbContext SHALL accept a DbContextOptions parameter in its constructor and pass it to the base DbContext constructor.
3. WHEN OnModelCreating is invoked, THE GroundUpDbContext SHALL scan the model for all entity types implementing ISoftDeletable and apply a global query filter (HasQueryFilter) that excludes entities where IsDeleted equals true.
4. WHEN OnModelCreating is invoked, THE GroundUpDbContext SHALL configure a default value generation for BaseEntity.Id using UUIDNext to produce UUID v7 values.
5. THE GroundUpDbContext SHALL call base.OnModelCreating before applying its own configurations, so that derived DbContext entity configurations are registered first.
6. THE GroundUpDbContext SHALL use a file-scoped namespace and have XML documentation comments on the class and all public or protected members.
7. THE GroundUpDbContext SHALL NOT be sealed because consuming applications must inherit from it.

### Requirement 2: AuditableInterceptor

**User Story:** As a framework developer, I want a SaveChanges interceptor that automatically populates IAuditable fields on Added and Modified entities, so that consuming applications get audit trail population without manual code.

#### Acceptance Criteria

1. THE Data_Postgres_Project SHALL contain a sealed AuditableInterceptor class in the GroundUp.Data.Postgres.Interceptors namespace that extends SaveChangesInterceptor.
2. THE AuditableInterceptor SHALL accept an IServiceProvider via constructor injection.
3. WHEN SavingChangesAsync is invoked, THE AuditableInterceptor SHALL resolve ICurrentUser from the current DI scope using IServiceProvider.CreateScope().
4. WHEN SavingChangesAsync is invoked, THE AuditableInterceptor SHALL iterate over all tracked entities implementing IAuditable with EntityState.Added and set CreatedAt to DateTime.UtcNow and CreatedBy to the ICurrentUser.UserId.ToString().
5. WHEN SavingChangesAsync is invoked, THE AuditableInterceptor SHALL iterate over all tracked entities implementing IAuditable with EntityState.Modified and set UpdatedAt to DateTime.UtcNow and UpdatedBy to the ICurrentUser.UserId.ToString().
6. IF ICurrentUser cannot be resolved from the service scope (no user context available), THEN THE AuditableInterceptor SHALL still set the timestamp fields (CreatedAt, UpdatedAt) and leave the user fields (CreatedBy, UpdatedBy) as null.
7. THE AuditableInterceptor SHALL use a file-scoped namespace and have XML documentation comments on the class and all public members.

### Requirement 3: SoftDeleteInterceptor

**User Story:** As a framework developer, I want a SaveChanges interceptor that converts Remove() calls on ISoftDeletable entities to soft deletes, so that direct DbContext usage has a safety net preventing accidental hard deletes of soft-deletable entities.

#### Acceptance Criteria

1. THE Data_Postgres_Project SHALL contain a sealed SoftDeleteInterceptor class in the GroundUp.Data.Postgres.Interceptors namespace that extends SaveChangesInterceptor.
2. THE SoftDeleteInterceptor SHALL accept an IServiceProvider via constructor injection.
3. WHEN SavingChangesAsync is invoked, THE SoftDeleteInterceptor SHALL iterate over all tracked entities implementing ISoftDeletable with EntityState.Deleted, change their state to EntityState.Modified, set IsDeleted to true, and set DeletedAt to DateTime.UtcNow.
4. WHEN SavingChangesAsync is invoked and ICurrentUser can be resolved from the current DI scope, THE SoftDeleteInterceptor SHALL set DeletedBy to the ICurrentUser.UserId.ToString() on soft-deleted entities.
5. IF ICurrentUser cannot be resolved from the service scope, THEN THE SoftDeleteInterceptor SHALL still set IsDeleted and DeletedAt and leave DeletedBy as null.
6. THE SoftDeleteInterceptor SHALL use a file-scoped namespace and have XML documentation comments on the class and all public members.

### Requirement 4: UUID v7 Default Value Generation

**User Story:** As a framework developer, I want all BaseEntity.Id properties to default to UUID v7 values, so that new entities get sequential, sortable identifiers without consuming applications needing to set them manually.

#### Acceptance Criteria

1. WHEN OnModelCreating is invoked, THE GroundUpDbContext SHALL locate all entity types that inherit from BaseEntity and configure their Id property with a value generator that produces UUID v7 values using the UUIDNext package.
2. THE Data_Postgres_Project SHALL include a NuGet package reference for UUIDNext.
3. WHEN a new entity inheriting from BaseEntity is added to the DbContext with a default (empty Guid) Id, THE GroundUpDbContext value generation SHALL assign a UUID v7 value before persisting.
4. WHEN a new entity inheriting from BaseEntity is added to the DbContext with a non-default (pre-set) Id, THE GroundUpDbContext value generation SHALL preserve the pre-set Id value.

### Requirement 5: Global Query Filter for ISoftDeletable

**User Story:** As a framework developer, I want all queries against ISoftDeletable entities to automatically exclude soft-deleted records, so that consuming applications never accidentally retrieve deleted data without explicitly opting in.

#### Acceptance Criteria

1. WHEN OnModelCreating is invoked, THE GroundUpDbContext SHALL dynamically scan the model for all entity types implementing ISoftDeletable.
2. FOR ALL entity types implementing ISoftDeletable, THE GroundUpDbContext SHALL apply HasQueryFilter with the expression e => !e.IsDeleted using reflection to build the filter expression dynamically.
3. WHEN a query is executed against an ISoftDeletable entity type, THE global query filter SHALL exclude entities where IsDeleted equals true.
4. WHEN a query uses IgnoreQueryFilters(), THE global query filter SHALL be bypassed, allowing retrieval of soft-deleted entities.

### Requirement 6: Postgres Unique Constraint Violation Detection

**User Story:** As a framework developer, I want a helper method that detects Postgres-specific unique constraint violations from DbUpdateException, so that BaseRepository can provide meaningful conflict error messages.

#### Acceptance Criteria

1. THE Data_Postgres_Project SHALL contain a static PostgresErrorHelper class in the GroundUp.Data.Postgres namespace.
2. THE PostgresErrorHelper SHALL provide a static IsUniqueConstraintViolation method that accepts a DbUpdateException and returns a boolean.
3. WHEN IsUniqueConstraintViolation is called with a DbUpdateException whose inner exception is a Npgsql.PostgresException with SqlState equal to "23505", THE PostgresErrorHelper SHALL return true.
4. WHEN IsUniqueConstraintViolation is called with a DbUpdateException that does not contain a Npgsql.PostgresException with SqlState "23505", THE PostgresErrorHelper SHALL return false.
5. WHEN IsUniqueConstraintViolation is called with a null DbUpdateException, THE PostgresErrorHelper SHALL return false.
6. THE PostgresErrorHelper SHALL use a file-scoped namespace and have XML documentation comments on the class and all public members.
7. THE PostgresErrorHelper class SHALL be sealed and static.

### Requirement 7: DataSeederRunner Hosted Service

**User Story:** As a framework developer, I want a hosted service that discovers and runs all IDataSeeder implementations on application startup, so that reference data is seeded automatically without consuming applications writing startup code.

#### Acceptance Criteria

1. THE Data_Postgres_Project SHALL contain a sealed DataSeederRunner class in the GroundUp.Data.Postgres namespace that implements IHostedService.
2. THE DataSeederRunner SHALL accept an IServiceProvider via constructor injection.
3. WHEN StartAsync is invoked, THE DataSeederRunner SHALL create a new DI scope, resolve all IDataSeeder implementations from the scoped service provider, order them by their Order property (ascending), and call SeedAsync on each sequentially.
4. IF a seeder throws an exception during SeedAsync, THEN THE DataSeederRunner SHALL log the error using ILogger and continue executing the remaining seeders.
5. WHEN StopAsync is invoked, THE DataSeederRunner SHALL complete without performing any action (no cleanup required).
6. THE DataSeederRunner SHALL use a file-scoped namespace and have XML documentation comments on the class and all public members.

### Requirement 8: AddGroundUpPostgres Extension Method

**User Story:** As a consuming application developer, I want a single extension method call to register the DbContext with Npgsql, interceptors, and the data seeder runner, so that wiring up GroundUp's Postgres infrastructure requires minimal boilerplate.

#### Acceptance Criteria

1. THE Data_Postgres_Project SHALL contain a static PostgresServiceCollectionExtensions class in the GroundUp.Data.Postgres namespace.
2. THE PostgresServiceCollectionExtensions SHALL provide a static AddGroundUpPostgres&lt;TContext&gt; extension method on IServiceCollection that accepts a string connectionString parameter, where TContext is constrained to GroundUpDbContext.
3. WHEN AddGroundUpPostgres is called, THE extension method SHALL register TContext with the DI container using AddDbContext with Npgsql as the database provider and the provided connection string.
4. WHEN AddGroundUpPostgres is called, THE extension method SHALL register AuditableInterceptor and SoftDeleteInterceptor as singleton services and configure them as interceptors on the DbContext.
5. WHEN AddGroundUpPostgres is called, THE extension method SHALL register DataSeederRunner as a hosted service.
6. WHEN AddGroundUpPostgres is called, THE extension method SHALL return the IServiceCollection to support method chaining.
7. THE PostgresServiceCollectionExtensions SHALL use a file-scoped namespace and have XML documentation comments on the class and the extension method.

### Requirement 9: NuGet Package References

**User Story:** As a framework developer, I want the Data.Postgres project to have the correct NuGet package references, so that all EF Core, Npgsql, and UUID v7 dependencies are available at compile time.

#### Acceptance Criteria

1. THE Data_Postgres_Project SHALL include a NuGet package reference for Microsoft.EntityFrameworkCore version 8.0.*.
2. THE Data_Postgres_Project SHALL include a NuGet package reference for Npgsql.EntityFrameworkCore.PostgreSQL version 8.0.*.
3. THE Data_Postgres_Project SHALL include a NuGet package reference for UUIDNext.
4. THE Data_Postgres_Project SHALL include a NuGet package reference for Microsoft.Extensions.Hosting.Abstractions (for IHostedService).
5. THE Data_Postgres_Project SHALL retain its existing project references to Core_Project, Data_Abstractions, and Repositories_Project.

### Requirement 10: Unit Tests for AuditableInterceptor

**User Story:** As a framework developer, I want unit tests verifying that the AuditableInterceptor correctly populates IAuditable fields, so that I have confidence audit fields are set on Added and Modified entities.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that WHEN an entity implementing IAuditable is in EntityState.Added, THE AuditableInterceptor sets CreatedAt to a value within one second of DateTime.UtcNow and CreatedBy to the ICurrentUser.UserId.ToString().
2. THE Unit_Test_Project SHALL contain a test verifying that WHEN an entity implementing IAuditable is in EntityState.Modified, THE AuditableInterceptor sets UpdatedAt to a value within one second of DateTime.UtcNow and UpdatedBy to the ICurrentUser.UserId.ToString().
3. THE Unit_Test_Project SHALL contain a test verifying that WHEN an entity NOT implementing IAuditable is in EntityState.Added, THE AuditableInterceptor does not modify the entity.
4. THE Unit_Test_Project SHALL contain a test verifying that WHEN ICurrentUser cannot be resolved, THE AuditableInterceptor still sets CreatedAt on Added entities and leaves CreatedBy as null.
5. THE Unit_Test_Project SHALL use xUnit and NSubstitute, consistent with existing test conventions.

### Requirement 11: Unit Tests for SoftDeleteInterceptor

**User Story:** As a framework developer, I want unit tests verifying that the SoftDeleteInterceptor correctly converts deletes to soft deletes, so that I have confidence the safety net works for direct DbContext usage.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that WHEN an entity implementing ISoftDeletable is in EntityState.Deleted, THE SoftDeleteInterceptor changes the state to EntityState.Modified, sets IsDeleted to true, and sets DeletedAt to a value within one second of DateTime.UtcNow.
2. THE Unit_Test_Project SHALL contain a test verifying that WHEN an entity NOT implementing ISoftDeletable is in EntityState.Deleted, THE SoftDeleteInterceptor does not modify the entity state.
3. THE Unit_Test_Project SHALL contain a test verifying that WHEN ICurrentUser can be resolved, THE SoftDeleteInterceptor sets DeletedBy to the ICurrentUser.UserId.ToString() on soft-deleted entities.
4. THE Unit_Test_Project SHALL use xUnit and NSubstitute, consistent with existing test conventions.

### Requirement 12: Unit Tests for PostgresErrorHelper

**User Story:** As a framework developer, I want unit tests verifying that PostgresErrorHelper correctly identifies Postgres unique constraint violations, so that I have confidence the error detection logic is accurate.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that IsUniqueConstraintViolation returns true when the DbUpdateException contains a PostgresException with SqlState "23505".
2. THE Unit_Test_Project SHALL contain a test verifying that IsUniqueConstraintViolation returns false when the DbUpdateException contains a PostgresException with a different SqlState.
3. THE Unit_Test_Project SHALL contain a test verifying that IsUniqueConstraintViolation returns false when the DbUpdateException has no inner exception.
4. THE Unit_Test_Project SHALL contain a test verifying that IsUniqueConstraintViolation returns false when called with a null argument.
5. THE Unit_Test_Project SHALL use xUnit, consistent with existing test conventions.

### Requirement 13: Unit Tests for DataSeederRunner

**User Story:** As a framework developer, I want unit tests verifying that DataSeederRunner discovers, orders, and executes seeders correctly, so that I have confidence reference data seeding works on startup.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that WHEN StartAsync is called with multiple IDataSeeder implementations, THE DataSeederRunner calls SeedAsync on each in ascending Order.
2. THE Unit_Test_Project SHALL contain a test verifying that WHEN a seeder throws an exception, THE DataSeederRunner logs the error and continues executing the remaining seeders.
3. THE Unit_Test_Project SHALL contain a test verifying that WHEN no IDataSeeder implementations are registered, THE DataSeederRunner completes StartAsync without error.
4. THE Unit_Test_Project SHALL use xUnit and NSubstitute, consistent with existing test conventions.

### Requirement 14: Solution Build Verification

**User Story:** As a framework developer, I want the entire solution to compile after all Phase 3C changes, so that I know the new EF Core infrastructure integrates correctly with the existing codebase.

#### Acceptance Criteria

1. WHEN `dotnet build groundup.sln` is executed after all Phase 3C changes, THE Solution SHALL compile with zero errors.
2. WHEN `dotnet test` is executed after all Phase 3C changes, THE Unit_Test_Project SHALL pass all tests including the new interceptor, helper, and seeder runner tests.

### Requirement 15: Enforce Coding Conventions

**User Story:** As a framework developer, I want all Phase 3C types to follow established coding conventions, so that the EF Core infrastructure code is consistent with the rest of the framework.

#### Acceptance Criteria

1. THE Data_Postgres_Project SHALL use file-scoped namespaces in all source files.
2. THE Data_Postgres_Project SHALL enable nullable reference types.
3. THE Data_Postgres_Project SHALL place each class in its own separate file.
4. THE GroundUpDbContext SHALL NOT use the sealed modifier because consuming applications must inherit from it.
5. THE AuditableInterceptor, SoftDeleteInterceptor, DataSeederRunner, PostgresErrorHelper, and PostgresServiceCollectionExtensions SHALL use the sealed modifier (or static where applicable) because they are not designed for inheritance.
6. THE Data_Postgres_Project SHALL have XML documentation comments on all public types and members.
7. THE Data_Postgres_Project SHALL target net8.0.
