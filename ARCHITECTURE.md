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

Applications that use the auth endpoints and middleware also call `AddGroundUpAuth(...)`, `AddGroundUpAuthApi()` (CSRF protection) and `AddGroundUpAuthKeycloak()`, then `app.UseGroundUpAuth()`. `UseGroundUpAuth` refuses to start without `AddGroundUpAuthApi()`.

## Settings Cascade

A setting is resolved along a **scope chain ordered most specific first**, for example `User → Group → Case → Tenant → System` in an application that defines those levels. The first level with a value wins; each parent level is the default for the levels below it, and the definition's default value applies when no level has a value. GroundUp creates the `System` and `Tenant` levels if the application has not (`DefaultSettingLevelSeeder`); applications add deeper levels and register their own `IScopeChainProvider`, which must keep the most-specific-first order. There is no parent-level lock. Rules that limit a child by its parent (such as the session timeouts below) are written as an `ISettingWriteValidator`.

Settings are cached in memory. A change made through `ISettingsService` clears the cache on the instance that made it; other instances see it after the cache TTL (15 minutes by default) until the caching module (Phase 15, GU-47) provides shared invalidation.

## Sessions (Administrator Notes)

**Two timeouts, as runtime settings** (group "Security > Sessions"):

| Setting | Range | Default | Meaning |
|---|---|---|---|
| `auth.session.token-lifetime-minutes` | 2 to 120 | 60 | How long a sign-in token is valid. Also the inactivity timeout: a user who makes no request for this long must sign in again. |
| `auth.session.absolute-lifetime-minutes` | 10 to 1440 | 480 | The longest a session can last from the user's last real sign-in, however active they are. Must be at least the token lifetime. |

- Both can be set at the **System** level and per **tenant**. A tenant value can only **shorten** the System value: a longer one is rejected when it is written, and if the System value is later lowered below an existing tenant value, the System value applies.
- A change applies from the next token issue or refresh. Existing tokens keep their expiry, but the absolute limit is checked against the current value at every refresh and tenant switch, so shortening it takes effect without waiting.
- The session is counted from the identity provider's `auth_time` (when the user really authenticated), so a silent single sign-on does not restart it. Every login request carries `max_age` with the absolute lifetime, so Keycloak asks for credentials when its own session is older. When a session has reached the limit the API returns `401` with `errorCode: REAUTH_REQUIRED`; the client then starts `POST /auth/login?forceLogin=true`.
- No token expires later than the user's last sign-in plus the absolute lifetime.
- Until settings authorization exists (GU-77), the settings API does not restrict who can change these values.

**Keycloak's own limits are the outer boundary.** The realm's **SSO Session Idle** and **SSO Session Max** (Realm settings → Sessions) apply to every user of the shared realm and cannot be set per tenant. They are the system administrator's responsibility in the Keycloak admin console, and they are the values a compliance reviewer will look at alongside the two settings above. `keycloak/realm.json` sets 30 minutes idle and 8 hours maximum for development, equal to the default absolute lifetime; the organization's own session policy decides the final numbers. If `auth.session.absolute-lifetime-minutes` is raised above the realm's SSO Session Max, Keycloak's limit becomes the real one. GroundUp does not read the realm values at startup to warn about this (that would need an admin API call at every start); keep the two in step by policy.

**Cookies.** Every auth cookie is HttpOnly and takes `Secure` from `GroundUp:Auth:CookieSecure`; the application refuses to start with `CookieSecure=false` outside Development.

| Cookie | Purpose | SameSite | Domain | Expires |
|---|---|---|---|---|
| `AuthToken` | GroundUp session token | `CookieSameSite` (default Strict) | `.{auth.application.default-domain}` | with the token |
| `IdToken` | `id_token_hint` for logout | as `AuthToken` | as `AuthToken` | with the auth cookie |
| `AuthState` | login flow state across the redirect to Keycloak | Lax (Strict would drop it on the return from Keycloak) | host only | 10 minutes |
| `CsrfToken` | antiforgery cookie token | Strict | host only | browser session |

A single-label default domain such as `localhost` logs a warning: browsers drop the cookie on subdomain hosts, so subdomain tenants need a domain such as `groundup.test`.

**CSRF.** A POST, PUT, PATCH or DELETE authenticated by the `AuthToken` cookie must send the token from `GET /auth/csrf` in the `X-CSRF-Token` header, or it gets `403 CSRF_VALIDATION_FAILED`. A browser client calls `/auth/csrf` once after sign-in, keeps the token in memory, and calls it again after a 403. Requests with an `Authorization: Bearer` header (mobile apps, services, MCP clients) need no CSRF token.
