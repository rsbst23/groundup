# Requirements Document

## Introduction

Phase 5 proves that `BaseTenantRepository` correctly isolates data by tenant — one of the most critical features of the GroundUp framework. This phase builds a concrete `TenantContext` implementation that reads the tenant identity from HTTP headers, creates a tenant-scoped sample entity (`Project`) to exercise `BaseTenantRepository` through the full stack, and delivers integration tests that verify cross-tenant data is invisible, unmodifiable, and undeletable.

The existing `BaseTenantRepository<TEntity, TDto>` already provides compile-time generic constraints, automatic query filtering via `TenantShaper`, auto-stamping of `TenantId` on add, and tenant verification on update/delete. What is missing is a runtime `ITenantContext` implementation and end-to-end proof that the isolation holds under real HTTP requests against a real Postgres database.

## Glossary

- **TenantContext**: A sealed scoped service in `GroundUp.Core` implementing `ITenantContext` with a settable `TenantId` property. It is a plain value holder with no HTTP dependencies, usable in any hosting context (API, SDK, console, background jobs). In HTTP scenarios, `TenantResolutionMiddleware` hydrates it from the request.
- **TenantResolutionMiddleware**: Middleware in `GroundUp.Api` that reads the tenant identity from the incoming HTTP request and sets `TenantContext.TenantId`. In Phase 5 it reads from the `X-Tenant-Id` header (temporary); in Phase 9 it will read from JWT claims.
- **ITenantContext**: Interface in `GroundUp.Core.Abstractions` exposing `Guid TenantId`. Used by `BaseTenantRepository` to scope all queries and mutations.
- **ITenantEntity**: Interface in `GroundUp.Core.Entities` declaring `Guid TenantId`. Entities implementing this are eligible for automatic tenant isolation.
- **BaseTenantRepository**: Abstract repository in `GroundUp.Repositories` that extends `BaseRepository` with automatic tenant filtering, auto-stamping, and cross-tenant guards.
- **Project**: A tenant-scoped sample entity (Name, Description, TenantId) used to prove multi-tenancy works end-to-end.
- **ProjectDto**: The DTO record for the Project entity.
- **ProjectMapper**: A Mapperly source-generated mapper for Project ↔ ProjectDto conversion.
- **ProjectRepository**: A concrete repository extending `BaseTenantRepository<Project, ProjectDto>`.
- **ProjectService**: A concrete service extending `BaseService<ProjectDto>`.
- **ProjectController**: A concrete controller extending `BaseController<ProjectDto>` with HTTP attributes.
- **SampleDbContext**: The EF Core DbContext in the Sample app that registers entity DbSets and configurations.
- **SampleApiFactory**: The `GroundUpWebApplicationFactory` subclass used for integration tests against the Sample app.
- **X-Tenant-Id**: The HTTP request header carrying the tenant identifier as a GUID string.
- **Tenant_A**: A test tenant identity (arbitrary GUID) used in isolation tests.
- **Tenant_B**: A different test tenant identity (arbitrary GUID) used in isolation tests.

## Requirements

### Requirement 1: TenantContext Implementation

**User Story:** As a framework consumer, I want a single scoped `ITenantContext` implementation that holds the current tenant identity, so that `BaseTenantRepository` can automatically filter and stamp data by tenant regardless of whether the code runs in an HTTP request, a background job, or an SDK context.

#### Acceptance Criteria

1. THE TenantContext SHALL be a sealed class in `GroundUp.Core` implementing `ITenantContext` with a settable `TenantId` property of type `Guid`.
2. THE TenantContext SHALL default `TenantId` to `Guid.Empty` when no value has been set.
3. THE TenantContext SHALL be registered as a scoped service in the dependency injection container so that each operation scope receives its own instance.
4. THE TenantContext SHALL have no dependency on `IHttpContextAccessor` or any HTTP-specific types — it is a plain value holder usable in any hosting context (API, SDK, console, background jobs).

### Requirement 1a: TenantResolutionMiddleware (HTTP Tenant Hydration)

**User Story:** As a framework consumer hosting an HTTP API, I want middleware that automatically hydrates `TenantContext.TenantId` from the incoming request, so that tenant isolation works transparently for every HTTP request.

#### Acceptance Criteria

1. THE TenantResolutionMiddleware SHALL reside in the `GroundUp.Api` project as an HTTP-layer concern.
2. WHEN an HTTP request includes a valid `X-Tenant-Id` header containing a GUID, THE TenantResolutionMiddleware SHALL parse the header value and set `TenantContext.TenantId` to the parsed GUID. This is a temporary development/testing mechanism; in Phase 9 the middleware will read the tenant identity from JWT claims instead.
3. WHEN an HTTP request does not include an `X-Tenant-Id` header or the header value is not a valid GUID, THE TenantResolutionMiddleware SHALL leave `TenantContext.TenantId` as `Guid.Empty`.
4. THE TenantResolutionMiddleware SHALL be registered via `UseGroundUpMiddleware()` so that it runs early in the pipeline before any controller or service code executes.
5. THE TenantResolutionMiddleware SHALL be clearly documented as a temporary header-based implementation that will be replaced by JWT-based tenant resolution in Phase 9 (Authentication).

### Requirement 2: Tenant-Scoped Project Entity

**User Story:** As a framework developer, I want a tenant-scoped `Project` entity in the Sample app that exercises `BaseTenantRepository` through the full stack (entity → DTO → mapper → repository → service → controller), so that multi-tenancy can be tested end-to-end.

#### Acceptance Criteria

1. THE Project entity SHALL implement both `ITenantEntity` and `IAuditable` and extend `BaseEntity`.
2. THE Project entity SHALL have a `Name` property of type `string` (required, max length 200) and a `Description` property of type `string?` (optional, max length 1000).
3. THE ProjectDto SHALL be a class with `Id` (Guid), `Name` (string), and `Description` (string?) properties.
4. THE ProjectMapper SHALL be a Mapperly source-generated static partial class providing `ToDto` and `ToEntity` mapping methods.
5. THE ProjectRepository SHALL extend `BaseTenantRepository<Project, ProjectDto>` and accept `SampleDbContext` and `ITenantContext` as constructor parameters.
6. THE ProjectService SHALL extend `BaseService<ProjectDto>` following the same pattern as `TodoItemService`.
7. THE ProjectController SHALL extend `BaseController<ProjectDto>` and expose `GetAll`, `GetById`, `Create`, `Update`, and `Delete` endpoints with appropriate HTTP method attributes.
8. THE SampleDbContext SHALL include a `DbSet<Project>` property and Fluent API configuration for the Project entity.
9. WHEN the Sample app starts, THE EF Core migration for the Project table SHALL have been generated and applied, creating the Projects table with Id, Name, Description, TenantId, CreatedAt, CreatedBy, UpdatedAt, and UpdatedBy columns.
10. THE Sample app's `Program.cs` SHALL register `ProjectRepository` as `IBaseRepository<ProjectDto>`, `ProjectService` as `BaseService<ProjectDto>`, and register `TenantContext` as the `ITenantContext` implementation (scoped).

### Requirement 3: Tenant Isolation on Read Operations

**User Story:** As a framework consumer, I want assurance that querying data as one tenant never returns another tenant's records, so that tenant data boundaries are enforced automatically.

#### Acceptance Criteria

1. WHEN Tenant_A creates a Project and Tenant_A queries all Projects, THE ProjectController SHALL return a paginated result containing the created Project.
2. WHEN Tenant_A creates a Project and Tenant_B queries all Projects, THE ProjectController SHALL return a paginated result with zero items belonging to Tenant_A.
3. WHEN Tenant_A creates a Project and Tenant_B queries that Project by its ID, THE ProjectController SHALL return a 404 NotFound response.
4. WHEN Tenant_A creates multiple Projects and Tenant_B creates multiple Projects and Tenant_A queries all Projects, THE ProjectController SHALL return only Tenant_A's Projects and none of Tenant_B's Projects.

### Requirement 4: Tenant Isolation on Write Operations

**User Story:** As a framework consumer, I want assurance that one tenant cannot modify or delete another tenant's records, so that data integrity across tenants is guaranteed.

#### Acceptance Criteria

1. WHEN Tenant_A creates a Project and Tenant_B attempts to update that Project by its ID, THE ProjectController SHALL return a 404 NotFound response and the Project SHALL remain unchanged.
2. WHEN Tenant_A creates a Project and Tenant_B attempts to delete that Project by its ID, THE ProjectController SHALL return a 404 NotFound response and the Project SHALL remain in the database.
3. WHEN Tenant_A creates a Project, THE BaseTenantRepository SHALL auto-stamp the Project's TenantId with Tenant_A's identity regardless of any TenantId value provided in the DTO.

### Requirement 5: Tenant Isolation Integration Tests

**User Story:** As a framework developer, I want HTTP-level integration tests that prove tenant isolation holds under real database conditions, so that regressions in multi-tenancy are caught automatically.

#### Acceptance Criteria

1. THE integration test suite SHALL include a test that creates a Project as Tenant_A, retrieves it as Tenant_A, and verifies the Project is returned successfully.
2. THE integration test suite SHALL include a test that creates a Project as Tenant_A, retrieves all Projects as Tenant_B, and verifies zero Projects are returned.
3. THE integration test suite SHALL include a test that creates a Project as Tenant_A, attempts to retrieve it by ID as Tenant_B, and verifies a 404 NotFound response.
4. THE integration test suite SHALL include a test that creates a Project as Tenant_A, attempts to update it as Tenant_B, and verifies a 404 NotFound response, then retrieves it as Tenant_A and verifies the original values are unchanged.
5. THE integration test suite SHALL include a test that creates a Project as Tenant_A, attempts to delete it as Tenant_B, and verifies a 404 NotFound response, then retrieves it as Tenant_A and verifies the Project still exists.
6. THE integration test suite SHALL include a test that creates Projects for both Tenant_A and Tenant_B, retrieves all Projects as Tenant_A, and verifies only Tenant_A's Projects are returned with the correct count.
7. THE integration tests SHALL use the existing `SampleApiFactory` and `IntegrationTestBase` infrastructure with Testcontainers for real Postgres.
8. THE integration tests SHALL set the `X-Tenant-Id` header on each HTTP request to simulate different tenant contexts.
9. THE integration tests SHALL use unique GUID values for Tenant_A and Tenant_B identities to ensure test isolation.
