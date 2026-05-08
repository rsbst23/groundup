# Requirements Document

## Introduction

Phase 9B builds the data layer for the GroundUp authentication and authorization module. This includes repository interfaces (abstractions), repository implementations extending the framework's BaseRepository/BaseTenantRepository, EF Core entity configurations using Fluent API, the AuthDbContext, an initial migration, and the DI registration extension method.

The auth data layer follows the same patterns established in the core framework (Phase 3A–3C) and the settings module (Phase 6A–6B): repository interfaces in an abstractions project, implementations in a repositories project, and EF Core configurations in a Postgres-specific project.

## Glossary

- **Auth_Data_Abstractions**: The `GroundUp.Auth.Data.Abstractions` project containing repository interfaces for auth entities.
- **Auth_Repositories**: The `GroundUp.Auth.Repositories` project containing concrete repository implementations.
- **Auth_Data_Postgres**: The `GroundUp.Auth.Data.Postgres` project containing EF Core entity configurations, AuthDbContext, and DI registration.
- **AuthDbContext**: The EF Core DbContext for auth entities, inheriting from `GroundUpDbContext`.
- **BaseRepository**: The abstract generic repository providing CRUD operations with filtering, sorting, and paging.
- **BaseTenantRepository**: The abstract repository extending BaseRepository with automatic tenant isolation via ITenantEntity filtering.
- **Entity_Configuration**: An `IEntityTypeConfiguration<T>` implementation defining the EF Core Fluent API schema for an entity.
- **Repository_Interface**: An interface extending `IBaseRepository<TDto>` that defines the data access contract for a specific auth entity.
- **Mapperly_Mapper**: A source-generated mapper class that converts between entities and DTOs.
- **OperationResult**: The standard result type returned by all repository methods, encapsulating success/failure with status codes.
- **QueryShaper**: A `Func<IQueryable<T>, IQueryable<T>>` delegate used to customize queries (e.g., Include navigation properties).
- **ITenantContext**: Interface providing the current tenant identity for automatic tenant filtering.

## Requirements

### Requirement 1: Auth Data Abstractions Project Structure

**User Story:** As a framework consumer, I want repository interfaces for all auth entities defined in a dedicated abstractions project, so that I can depend on contracts without coupling to implementations.

#### Acceptance Criteria

1. THE Auth_Data_Abstractions project SHALL target net8.0 with nullable reference types enabled.
2. THE Auth_Data_Abstractions project SHALL reference GroundUp.Core, GroundUp.Auth.Core, and GroundUp.Data.Abstractions.
3. THE Auth_Data_Abstractions project SHALL NOT reference any EF Core packages or implementation projects.

### Requirement 2: User Repository Interface

**User Story:** As a framework consumer, I want a repository interface for User entities, so that I can perform CRUD operations on users across tenants.

#### Acceptance Criteria

1. THE Auth_Data_Abstractions project SHALL define an IUserRepository interface that extends `IBaseRepository<UserDto>`.
2. THE IUserRepository SHALL declare a method to retrieve a user by ExternalUserId, returning `OperationResult<UserDto>`.
3. THE IUserRepository SHALL declare a method to retrieve a user by email address, returning `OperationResult<UserDto>`.

### Requirement 3: Tenant Repository Interface

**User Story:** As a framework consumer, I want a repository interface for Tenant entities, so that I can manage tenants with hierarchical visibility rules.

#### Acceptance Criteria

1. THE Auth_Data_Abstractions project SHALL define an ITenantRepository interface that extends `IBaseRepository<TenantDto>`.
2. THE ITenantRepository SHALL declare a method to retrieve a tenant by slug, returning `OperationResult<TenantDto>`.
3. THE ITenantRepository SHALL declare a method to retrieve child tenants of a given parent tenant ID, returning `OperationResult<PaginatedData<TenantDto>>`.

### Requirement 4: Role Repository Interface

**User Story:** As a framework consumer, I want a repository interface for Role entities, so that I can manage tenant-scoped roles and their policy assignments.

#### Acceptance Criteria

1. THE Auth_Data_Abstractions project SHALL define an IRoleRepository interface that extends `IBaseRepository<RoleDto>`.
2. THE IRoleRepository SHALL declare a method to retrieve all policies assigned to a given role ID, returning `OperationResult<List<PolicyDto>>`.
3. THE IRoleRepository SHALL declare a method to assign a policy to a role by role ID and policy ID, returning `OperationResult`.
4. THE IRoleRepository SHALL declare a method to remove a policy from a role by role ID and policy ID, returning `OperationResult`.

### Requirement 5: Policy Repository Interface

**User Story:** As a framework consumer, I want a repository interface for Policy entities, so that I can manage tenant-scoped policies and their permission assignments.

#### Acceptance Criteria

1. THE Auth_Data_Abstractions project SHALL define an IPolicyRepository interface that extends `IBaseRepository<PolicyDto>`.
2. THE IPolicyRepository SHALL declare a method to retrieve all permissions assigned to a given policy ID, returning `OperationResult<List<PermissionDto>>`.
3. THE IPolicyRepository SHALL declare a method to assign a permission to a policy by policy ID and permission ID, returning `OperationResult`.
4. THE IPolicyRepository SHALL declare a method to remove a permission from a policy by policy ID and permission ID, returning `OperationResult`.

### Requirement 6: Permission Repository Interface

**User Story:** As a framework consumer, I want a repository interface for Permission entities, so that I can manage global permission definitions.

#### Acceptance Criteria

1. THE Auth_Data_Abstractions project SHALL define an IPermissionRepository interface that extends `IBaseRepository<PermissionDto>`.
2. THE IPermissionRepository SHALL declare a method to retrieve a permission by its Key, returning `OperationResult<PermissionDto>`.
3. THE IPermissionRepository SHALL declare a method to retrieve all permissions for a given module, returning `OperationResult<PaginatedData<PermissionDto>>`.

### Requirement 7: Junction Entity Repository Interfaces

**User Story:** As a framework consumer, I want repository interfaces for UserTenant and UserRole junction entities, so that I can manage user memberships and role assignments directly.

#### Acceptance Criteria

1. THE Auth_Data_Abstractions project SHALL define an IUserTenantRepository interface that extends `IBaseRepository<UserTenantDto>`.
2. THE IUserTenantRepository SHALL declare a method to retrieve the tenant membership for a given user ID within the current tenant, returning `OperationResult<UserTenantDto>`.
3. THE IUserTenantRepository SHALL declare a system-level method `GetAllMembershipsForUserAsync(Guid userId)` that bypasses tenant filtering and returns all tenant memberships for a user across all tenants, returning `OperationResult<List<UserTenantDto>>`. This is required for the multi-tenant selection auth flow where a user needs to see all their memberships to choose which tenant to operate in.
4. THE Auth_Data_Abstractions project SHALL define an IUserRoleRepository interface that extends `IBaseRepository<UserRoleDto>`.
5. THE IUserRoleRepository SHALL declare a method to retrieve all role assignments for a given user ID within the current tenant, returning `OperationResult<List<UserRoleDto>>`.

### Requirement 8: Auth Repositories Project Structure

**User Story:** As a framework consumer, I want concrete repository implementations in a dedicated project, so that I can use the auth data layer with EF Core and Postgres.

#### Acceptance Criteria

1. THE Auth_Repositories project SHALL target net8.0 with nullable reference types enabled.
2. THE Auth_Repositories project SHALL reference GroundUp.Core, GroundUp.Auth.Core, GroundUp.Auth.Data.Abstractions, and GroundUp.Repositories.
3. THE Auth_Repositories project SHALL include a Mapperly mapper class for each entity-to-DTO mapping pair.
4. THE Auth_Repositories project SHALL include the Riok.Mapperly NuGet package reference.

### Requirement 9: User Repository Implementation

**User Story:** As a framework consumer, I want a UserRepository that extends BaseRepository, so that User CRUD operations work with standard filtering and paging.

#### Acceptance Criteria

1. THE Auth_Repositories project SHALL provide a UserRepository class that extends `BaseRepository<User, UserDto>` and implements IUserRepository.
2. WHEN a user is requested by ExternalUserId, THE UserRepository SHALL query the User entity filtered by ExternalUserId and return the mapped DTO.
3. WHEN a user is requested by email, THE UserRepository SHALL query the User entity filtered by Email (case-insensitive) and return the mapped DTO.
4. IF no user matches the query, THEN THE UserRepository SHALL return `OperationResult<UserDto>.NotFound()`.

### Requirement 10: Tenant Repository Implementation

**User Story:** As a framework consumer, I want a TenantRepository that extends BaseRepository with custom visibility rules, so that tenants can only see themselves and their children.

#### Acceptance Criteria

1. THE Auth_Repositories project SHALL provide a TenantRepository class that extends `BaseRepository<Tenant, TenantDto>` and implements ITenantRepository.
2. WHEN GetAllAsync is called, THE TenantRepository SHALL filter results to only include the current tenant (matching ITenantContext.TenantId) and its direct children (where ParentTenantId matches ITenantContext.TenantId).
3. WHEN GetByIdAsync is called, THE TenantRepository SHALL return NotFound if the requested tenant is neither the current tenant nor a child of the current tenant.
4. WHEN a tenant is requested by slug, THE TenantRepository SHALL query the Tenant entity filtered by Slug and return the mapped DTO, subject to the same visibility rules.
5. WHEN child tenants are requested, THE TenantRepository SHALL query tenants where ParentTenantId matches the provided parent ID, subject to the same visibility rules.
6. IF no tenant matches the query, THEN THE TenantRepository SHALL return `OperationResult<TenantDto>.NotFound()`.
7. WHEN a tenant is soft-deleted, THE TenantRepository SHALL set IsDeleted to true and DeletedAt to the current UTC time.

### Requirement 11: Tenant-Scoped Repository Implementations

**User Story:** As a framework consumer, I want repositories for tenant-scoped entities (Role, Policy, UserRole, UserTenant) that extend BaseTenantRepository, so that tenant isolation is automatic.

#### Acceptance Criteria

1. THE Auth_Repositories project SHALL provide a RoleRepository class that extends `BaseTenantRepository<Role, RoleDto>` and implements IRoleRepository.
2. THE Auth_Repositories project SHALL provide a PolicyRepository class that extends `BaseTenantRepository<Policy, PolicyDto>` and implements IPolicyRepository.
3. THE Auth_Repositories project SHALL provide a UserRoleRepository class that extends `BaseTenantRepository<UserRole, UserRoleDto>` and implements IUserRoleRepository.
4. THE Auth_Repositories project SHALL provide a UserTenantRepository class that extends `BaseTenantRepository<UserTenant, UserTenantDto>` and implements IUserTenantRepository.
5. WHEN any tenant-scoped repository creates an entity, THE repository SHALL automatically set TenantId to the current tenant from ITenantContext.
6. WHEN any tenant-scoped repository queries entities, THE repository SHALL automatically filter by the current tenant from ITenantContext.
7. THE RoleRepository SHALL implement policy assignment methods (GetPoliciesForRoleAsync, AssignPolicyAsync, RemovePolicyAsync) that manage RolePolicy junction records through navigation properties.
8. THE PolicyRepository SHALL implement permission assignment methods (GetPermissionsForPolicyAsync, AssignPermissionAsync, RemovePermissionAsync) that manage PolicyPermission junction records through navigation properties.

### Requirement 12: Global Repository Implementations

**User Story:** As a framework consumer, I want a repository for Permission that extends BaseRepository, so that it operates without tenant filtering as a global entity.

#### Acceptance Criteria

1. THE Auth_Repositories project SHALL provide a PermissionRepository class that extends `BaseRepository<Permission, PermissionDto>` and implements IPermissionRepository.

### Requirement 13: Mapperly Mappers

**User Story:** As a framework consumer, I want source-generated mappers for all auth entity-DTO pairs, so that mapping is compile-time verified and allocation-free.

#### Acceptance Criteria

1. THE Auth_Repositories project SHALL define a Mapperly mapper class that maps User to UserDto and UserDto to User.
2. THE Auth_Repositories project SHALL define a Mapperly mapper class that maps Tenant to TenantDto and TenantDto to Tenant.
3. THE Auth_Repositories project SHALL define a Mapperly mapper class that maps Role to RoleDto and RoleDto to Role.
4. THE Auth_Repositories project SHALL define a Mapperly mapper class that maps Policy to PolicyDto and PolicyDto to Policy.
5. THE Auth_Repositories project SHALL define a Mapperly mapper class that maps Permission to PermissionDto and PermissionDto to Permission.
6. THE Auth_Repositories project SHALL define Mapperly mapper classes for junction entity DTOs: UserTenantDto, UserRoleDto.

### Requirement 14: Auth Data Postgres Project Structure

**User Story:** As a framework consumer, I want EF Core configurations, the AuthDbContext, and DI registration in a dedicated Postgres project, so that the auth module integrates with the existing data infrastructure.

#### Acceptance Criteria

1. THE Auth_Data_Postgres project SHALL target net8.0 with nullable reference types enabled.
2. THE Auth_Data_Postgres project SHALL reference GroundUp.Core, GroundUp.Auth.Core, GroundUp.Auth.Data.Abstractions, GroundUp.Auth.Repositories, and GroundUp.Data.Postgres.
3. THE Auth_Data_Postgres project SHALL include EF Core, Npgsql.EntityFrameworkCore.PostgreSQL, and Microsoft.EntityFrameworkCore.Design package references.

### Requirement 15: Entity Configurations — String Lengths and Required Fields

**User Story:** As a framework consumer, I want all auth entity string properties configured with explicit max lengths and required constraints, so that the database schema enforces data integrity.

#### Acceptance Criteria

1. THE Entity_Configuration for User SHALL configure ExternalUserId as required with max length 200, Email as required with max length 320, and DisplayName as required with max length 200.
2. THE Entity_Configuration for Tenant SHALL configure Name as required with max length 200, Slug as required with max length 100, RealmName with max length 200, and CustomDomain with max length 500.
3. THE Entity_Configuration for Role SHALL configure Name as required with max length 200 and Description with max length 1000.
4. THE Entity_Configuration for Policy SHALL configure Name as required with max length 200 and Description with max length 1000.
5. THE Entity_Configuration for Permission SHALL configure Key as required with max length 200, Name as required with max length 200, Description with max length 1000, and Module as required with max length 100.
6. THE Entity_Configuration for UserTenant SHALL configure ExternalUserId as required with max length 200.

### Requirement 16: Entity Configurations — Unique Constraints

**User Story:** As a framework consumer, I want unique constraints enforced at the database level, so that duplicate data is prevented regardless of application-level validation.

#### Acceptance Criteria

1. THE Entity_Configuration for Tenant SHALL define a unique index on the Slug column.
2. THE Entity_Configuration for Permission SHALL define a unique index on the Key column.
3. THE Entity_Configuration for UserTenant SHALL define a unique composite index on (UserId, TenantId).
4. THE Entity_Configuration for RolePolicy SHALL define a unique composite index on (RoleId, PolicyId).
5. THE Entity_Configuration for PolicyPermission SHALL define a unique composite index on (PolicyId, PermissionId).
6. THE Entity_Configuration for UserRole SHALL define a unique composite index on (UserId, RoleId, TenantId).

### Requirement 17: Entity Configurations — Relationships and Foreign Keys

**User Story:** As a framework consumer, I want all entity relationships configured with proper foreign keys and delete behaviors, so that referential integrity is maintained.

#### Acceptance Criteria

1. THE Entity_Configuration for Tenant SHALL configure a self-referencing relationship via ParentTenantId with Restrict delete behavior.
2. THE Entity_Configuration for UserTenant SHALL configure foreign keys to User (Cascade) and Tenant (Restrict) delete behavior.
3. THE Entity_Configuration for Role SHALL configure a foreign key to Tenant with Restrict delete behavior.
4. THE Entity_Configuration for Policy SHALL configure a foreign key to Tenant with Restrict delete behavior.
5. THE Entity_Configuration for RolePolicy SHALL configure foreign keys to Role (Cascade) and Policy (Cascade) delete behavior.
6. THE Entity_Configuration for PolicyPermission SHALL configure foreign keys to Policy (Cascade) and Permission (Restrict) delete behavior.
7. THE Entity_Configuration for UserRole SHALL configure foreign keys to User (Cascade), Role (Cascade), and Tenant (Restrict) delete behavior.

### Requirement 18: Entity Configurations — Enum Conversions

**User Story:** As a framework consumer, I want enum properties stored as integers in the database, so that storage is efficient and queries are fast.

#### Acceptance Criteria

1. THE Entity_Configuration for Tenant SHALL configure TenantType and OnboardingMode with integer conversion.
2. THE Entity_Configuration for Role SHALL configure RoleType with integer conversion.

### Requirement 19: Entity Configurations — Default Values

**User Story:** As a framework consumer, I want boolean properties with sensible defaults configured at the database level, so that new records have correct initial state.

#### Acceptance Criteria

1. THE Entity_Configuration for User SHALL configure IsActive with a default value of true.
2. THE Entity_Configuration for Tenant SHALL configure IsActive with a default value of true.
3. THE Entity_Configuration for UserTenant SHALL configure IsActive with a default value of true.
4. THE Entity_Configuration for Role SHALL configure IsSystem with a default value of false.

### Requirement 20: AuthDbContext

**User Story:** As a framework consumer, I want an AuthDbContext that inherits from GroundUpDbContext, so that auth entities get UUID v7 generation, soft delete filters, and audit interceptors automatically.

#### Acceptance Criteria

1. THE AuthDbContext SHALL inherit from GroundUpDbContext.
2. THE AuthDbContext SHALL define DbSet properties for all nine auth entities: User, Tenant, UserTenant, Role, Policy, Permission, RolePolicy, PolicyPermission, UserRole.
3. WHEN OnModelCreating is called, THE AuthDbContext SHALL apply all entity configurations from the Auth_Data_Postgres assembly.
4. THE AuthDbContext SHALL support a separate connection string from the main application DbContext.

### Requirement 21: DI Registration Extension Method

**User Story:** As a framework consumer, I want a single extension method to register all auth data layer services, so that integration is a one-liner in Program.cs.

#### Acceptance Criteria

1. THE Auth_Data_Postgres project SHALL provide an `AddGroundUpAuthPostgres` extension method on IServiceCollection.
2. WHEN AddGroundUpAuthPostgres is called with a connection string, THE extension method SHALL register AuthDbContext with Npgsql and interceptors.
3. WHEN AddGroundUpAuthPostgres is called, THE extension method SHALL register all repository implementations (UserRepository, TenantRepository, RoleRepository, PolicyRepository, PermissionRepository, UserTenantRepository, UserRoleRepository) against their interfaces as scoped services.
4. THE AddGroundUpAuthPostgres extension method SHALL register the audit and soft delete interceptors.

### Requirement 22: Entity Configuration Table Names

**User Story:** As a framework consumer, I want explicit table names for all auth entities with an "Auth" prefix, so that the database schema avoids collisions with consuming application tables.

#### Acceptance Criteria

1. THE Entity_Configuration for User SHALL map to a table named "AuthUsers".
2. THE Entity_Configuration for Tenant SHALL map to a table named "AuthTenants".
3. THE Entity_Configuration for UserTenant SHALL map to a table named "AuthUserTenants".
4. THE Entity_Configuration for Role SHALL map to a table named "AuthRoles".
5. THE Entity_Configuration for Policy SHALL map to a table named "AuthPolicies".
6. THE Entity_Configuration for Permission SHALL map to a table named "AuthPermissions".
7. THE Entity_Configuration for RolePolicy SHALL map to a table named "AuthRolePolicies".
8. THE Entity_Configuration for PolicyPermission SHALL map to a table named "AuthPolicyPermissions".
9. THE Entity_Configuration for UserRole SHALL map to a table named "AuthUserRoles".

### Requirement 23: Junction Entity DTOs

**User Story:** As a framework consumer, I want DTOs for junction entities that have their own repositories, so that repositories can expose relationship data without leaking EF Core entities.

#### Acceptance Criteria

1. THE GroundUp.Auth.Core project SHALL define a UserTenantDto record with properties: Id, UserId, TenantId, ExternalUserId, IsActive.
2. THE GroundUp.Auth.Core project SHALL define a UserRoleDto record with properties: Id, UserId, RoleId, TenantId.

### Requirement 24: Foreign Key Indexes

**User Story:** As a framework consumer, I want indexes on all foreign key columns, so that join queries and lookups perform efficiently.

#### Acceptance Criteria

1. THE Entity_Configuration for UserTenant SHALL define indexes on UserId and TenantId columns individually.
2. THE Entity_Configuration for Role SHALL define an index on TenantId.
3. THE Entity_Configuration for Policy SHALL define an index on TenantId.
4. THE Entity_Configuration for RolePolicy SHALL define indexes on RoleId and PolicyId columns individually.
5. THE Entity_Configuration for PolicyPermission SHALL define indexes on PolicyId and PermissionId columns individually.
6. THE Entity_Configuration for UserRole SHALL define indexes on UserId, RoleId, and TenantId columns individually.
7. THE Entity_Configuration for Tenant SHALL define an index on ParentTenantId.

### Requirement 25: Initial EF Core Migration

**User Story:** As a framework consumer, I want an initial migration that creates all auth tables, so that the database schema is ready for use after running migrations.

#### Acceptance Criteria

1. THE Auth_Data_Postgres project SHALL include an initial EF Core migration that creates all nine auth tables with their configured columns, indexes, constraints, and relationships.
2. THE migration SHALL be generated using the EF Core migration tooling and be idempotent (safe to run on an already-migrated database).
3. THE migration SHALL create tables with the "Auth" prefix naming convention.

### Requirement 26: UserTenant Entity Change — Add ITenantEntity

**User Story:** As a framework consumer, I want UserTenant to implement ITenantEntity, so that tenant membership records are automatically scoped by the BaseTenantRepository.

#### Acceptance Criteria

1. THE UserTenant entity in GroundUp.Auth.Core SHALL implement the ITenantEntity interface (it already has the TenantId property, so only the interface declaration needs to be added).
