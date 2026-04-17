# GroundUp — Architecture

GroundUp is a modular, enterprise-grade foundational framework for .NET 8+ distributed as NuGet packages. It is **not** an application — it provides building blocks that applications consume. A consuming app references only the GroundUp packages it needs, wires them up in `Program.cs`, and gets CRUD, filtering, paging, multi-tenancy, auth, and more out of the box.

## Project Naming Convention

Only the API layer project uses `.Api` in its name (`GroundUp.Api`). All other projects use `GroundUp.*` without `.Api`, because they work equally well in SDK-only scenarios where no API/HTTP layer is present.

## Project Structure

```
groundup.sln
├── src/
│   ├── GroundUp.Core                 # Shared types: entities, DTOs, interfaces, enums, OperationResult
│   ├── GroundUp.Events               # Event abstractions and in-process event bus
│   ├── GroundUp.Data.Abstractions    # Repository and data access interfaces
│   ├── GroundUp.Repositories         # Base repository implementations (CRUD, filtering, paging)
│   ├── GroundUp.Data.Postgres        # Postgres-specific EF Core setup (DbContext, interceptors)
│   ├── GroundUp.Services             # Base service layer (validation, events, OperationResult)
│   └── GroundUp.Api                  # Base controllers, middleware, API infrastructure
├── samples/
│   └── GroundUp.Sample               # Sample ASP.NET Core app demonstrating framework usage
└── tests/
    ├── GroundUp.Tests.Unit           # Unit tests (xUnit + NSubstitute + FsCheck)
    └── GroundUp.Tests.Integration    # Integration tests (xUnit + Testcontainers)
```

## Projects

### GroundUp.Core

Foundational types shared by every layer in the framework. This project has **zero NuGet dependencies** and **zero project references** — it is the leaf of the dependency graph. Contains:

- `BaseEntity` — abstract base with Guid ID for all entities
- Opt-in interfaces: `IAuditable`, `ISoftDeletable`, `ITenantEntity`
- Context abstractions: `ICurrentUser`, `ITenantContext`
- `OperationResult<T>` — the single result type for all service/repository returns
- `PaginationParams`, `FilterParams`, `PaginatedData<T>` — query parameter types
- `ErrorCodes` — standardized error code constants
- Exception hierarchy: `GroundUpException`, `NotFoundException`

### GroundUp.Events

Event abstractions (`IEvent`, `IEventBus`, `IEventHandler<T>`) and the in-process event bus implementation. Enables loose coupling between modules via domain events. Depends only on Core.

### GroundUp.Data.Abstractions

Repository and data access interfaces (`IBaseRepository<T>`, `IUnitOfWork`, `IDataSeeder`). This layer keeps the service layer database-agnostic — services depend on these interfaces, not on EF Core or Postgres directly. Depends only on Core.

### GroundUp.Repositories

Base repository implementations with generic CRUD, filtering (via `FilterParams`), sorting, paging, and soft delete support. Database-agnostic — works against an EF Core `DbContext` but contains no provider-specific code. Depends on Core, Data.Abstractions, and Events.

### GroundUp.Data.Postgres

Postgres-specific EF Core setup:

- `GroundUpDbContext` base class
- SaveChanges interceptors for `IAuditable` fields and `ISoftDeletable` soft delete conversion
- Global query filters for soft-deleted entities
- UUID v7 default value generation
- Postgres-specific error detection (constraint violation codes)
- Migration support

This is the **only** project that contains provider-specific code. Depends on Core, Data.Abstractions, Repositories, plus EF Core and Npgsql NuGet packages.

### GroundUp.Services

Base service layer with FluentValidation pipeline, domain event publishing via `IEventBus`, and `OperationResult<T>` wrapping. This is the **business logic and security boundary** — all authorization checks happen here, not in controllers. Services work identically whether called from a controller (API) or directly (SDK). Depends on Core, Data.Abstractions, Events, plus FluentValidation.

### GroundUp.Api

Base controllers, middleware (exception handling, correlation ID), health checks, and API infrastructure. This is a **class library, not a web project** — it provides reusable HTTP adapter components that a consuming web application references. Controllers are thin — zero business logic, zero security checks. They convert `OperationResult<T>` to `ActionResult`. Depends on Core and Services.

### GroundUp.Sample

A sample ASP.NET Core web application that wires up all framework modules. Used for manual testing during development and as a reference for how consuming applications use GroundUp. References all src projects.

### GroundUp.Tests.Unit

Unit tests using xUnit, NSubstitute for mocking, FluentAssertions for readable assertions, and FsCheck for property-based testing.

### GroundUp.Tests.Integration

Integration tests using xUnit, Testcontainers for real Postgres instances (never EF InMemory), and the ASP.NET Core test host.

## Dependency Graph

```
GroundUp.Api → Services, Core
GroundUp.Services → Data.Abstractions, Events, Core
GroundUp.Repositories → Data.Abstractions, Events, Core
GroundUp.Data.Postgres → Repositories, Data.Abstractions, Core
GroundUp.Data.Abstractions → Core
GroundUp.Events → Core
GroundUp.Core → (nothing)
```

**Hard rules:**
- Dependencies flow strictly downward. No upward or circular references.
- Core has zero dependencies — on anything.
- Provider-specific code (Postgres error codes, connection setup) lives only in Data.Postgres.
- Cross-module communication goes through service interfaces, never through direct repository access.
- Services MAY depend on other modules' service interfaces for cross-module orchestration.

## Layering Rules

| Layer | Responsibility | Never does |
|-------|---------------|------------|
| **API** (GroundUp.Api) | HTTP adapters, middleware, ActionResult conversion | Business logic, security checks, direct repository access |
| **Services** (GroundUp.Services) | Business logic, validation, authorization, event publishing | Access HttpContext, contain provider-specific code |
| **Repositories** (GroundUp.Repositories) | Data access, filtering, paging, soft delete | Contain business logic, provider-specific code |
| **Data** (GroundUp.Data.Postgres) | EF Core setup, interceptors, migrations | Contain business logic, be referenced by Services |
| **Core** (GroundUp.Core) | Shared types, interfaces, DTOs | Depend on any other project |

## Key Design Decisions

1. **Framework, not application.** GroundUp produces NuGet packages. It never runs on its own — a consuming application wires up the modules it needs.

2. **SDK-first.** All business logic and security enforcement lives in the service layer. Services work identically whether called from a REST controller or directly as an SDK. Controllers are disposable HTTP adapters.

3. **Multi-tenancy at the repository layer.** `BaseTenantRepository` automatically filters all queries by the current tenant (from `ITenantContext`). Tenant isolation is enforced transparently — consuming code doesn't need to remember to filter.

4. **OperationResult, not exceptions.** Business logic errors return `OperationResult.Fail(...)` instead of throwing exceptions. Exceptions are reserved for infrastructure/cross-cutting errors (e.g., `NotFoundException` for missing entities, mapped to HTTP status codes by middleware).

5. **Opt-in behaviors.** Audit fields (`IAuditable`), soft delete (`ISoftDeletable`), and tenant scoping (`ITenantEntity`) are interfaces that entities implement to opt in. Nothing is forced on all entities.

6. **Strict layering.** Project references enforce the dependency rules at compile time. You physically cannot reference a repository from a controller — the project reference doesn't exist.

## How Consuming Applications Use GroundUp

A typical consuming application:

1. Creates entity classes extending `BaseEntity` (optionally implementing `IAuditable`, `ISoftDeletable`, `ITenantEntity`)
2. Creates DTOs as records
3. Creates Mapperly mappers (entity ↔ DTO)
4. Optionally creates custom repository, service, or controller classes extending the base classes
5. Wires up in `Program.cs` using extension methods:

```csharp
builder.Services.AddGroundUpApi();                              // Services + validation
builder.Services.AddGroundUpApiPostgres(connectionString);      // EF Core + Postgres
builder.Services.AddGroundUpEvents();                           // Event bus
```

The consuming app only writes the parts unique to its domain. GroundUp provides the infrastructure.
