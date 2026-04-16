# GroundUp Framework — CLAUDE.md

## What is GroundUp?

GroundUp is a modular, enterprise-grade foundational framework for .NET 8+ distributed as NuGet packages. It is NOT an application — it is the building blocks that applications are built on top of. Developers consume GroundUp packages to rapidly spin up enterprise-grade software without rebuilding foundational infrastructure.

A consuming application (e.g., "MyStore") references GroundUp NuGet packages, owns the hosting (Program.cs, Dockerfile), and provides its own controllers/services/entities that extend GroundUp's base classes and patterns.

**Database ownership:** The consuming application owns the database and connection string. GroundUp modules add their tables to the consuming application's database — they do NOT create separate databases by default. GroundUp entity configurations are registered into the consuming app's DbContext (or into module-specific DbContexts pointing at the same database). The consuming app's entities and GroundUp's entities coexist in the same database. However, each module optionally supports a separate database via its own connection string — if a consuming app wants audit logs in a different database, they pass a different connection string to that module's registration method.

## Architecture Philosophy

- **Framework, not application.** GroundUp provides base classes, interfaces, and infrastructure. It never assumes it is the final product.
- **Modular NuGet packages.** Each module (api, settings, authentication, notifications, audit, security, caching, messaging, etc.) is a self-contained set of packages. Consuming apps pick what they need. Every module is optional — the core API module works completely independently.
- **SDK-first design.** GroundUp can be consumed as an API (via HTTP controllers) OR as an SDK (direct service calls without HTTP). This means all authorization and business logic lives in the service layer, never in controllers. Controllers are thin HTTP adapters.
- **Convention over configuration.** Sensible defaults everywhere, with extension points for customization.
- **Strict layered architecture.** API → Services → Repositories → Data. Never skip layers. Never leak abstractions upward.
- **Multi-tenancy is foundational.** Tenant isolation is enforced at the repository level automatically, not left to developers to remember.
- **Event-driven ready.** An IEventBus abstraction supports both in-process and distributed event-driven patterns. Modules communicate through events when decoupling is needed.
- **Observability from day one.** Correlation IDs, health checks, structured logging, and request tracing are built into the core, not bolted on later.

---

## Solution Structure

### Monorepo Layout

All GroundUp modules live in a single monorepo with a single solution file. Each module produces its own set of NuGet packages.

**Why monorepo:** Cross-module changes are a single commit, version management is simple (all packages share the same version), and you immediately see if a Core change breaks Authentication in the same PR. CI is configured with path-based triggers so only affected modules' tests run on each change — you don't re-run all tests for every commit. If CI speed becomes a problem at scale, modules can be split into separate repos later, but the coordination overhead of multi-repo is worse than slightly longer CI runs during active development.

```
groundup/
├── CLAUDE.md                          # This file (root-level rules)
├── BUILD_PHASES.md                    # Phased build plan with verification steps
├── README.md
├── groundup.sln                       # Single solution referencing all projects
├── docker-compose.yml                 # Local dev: Postgres, Keycloak, etc.
├── .github/
│   └── workflows/                     # CI/CD: build, test, pack, publish (path-filtered)
│
├── src/
│   │── GroundUp.Api.Core/             # DTOs, enums, base entities, OperationResult, exceptions, interfaces
│   ├── GroundUp.Api/                  # Controllers, middleware, rate limiting, health checks, correlation ID
│   │   └── CLAUDE.md                  # Layer-specific rules
│   ├── GroundUp.Api.Services/         # Business logic, validation, BaseService
│   │   └── CLAUDE.md
│   ├── GroundUp.Api.Repositories/     # BaseRepository, BaseTenantRepository, query logic
│   │   └── CLAUDE.md
│   ├── GroundUp.Api.Data.Abstractions/# Repository interfaces, IUnitOfWork, IDataSeeder
│   ├── GroundUp.Api.Data.Postgres/    # EF Core DbContext, migrations, Postgres-specific
│   │   └── CLAUDE.md
│   │
│   ├── GroundUp.Events/               # IEventBus abstraction + in-process default
│   │
│   ├── GroundUp.Settings/                         # Settings controllers
│   ├── GroundUp.Settings.Services/                # Settings resolution, cascading logic
│   ├── GroundUp.Settings.Repositories/            # Settings persistence
│   ├── GroundUp.Settings.Data.Abstractions/       # Settings repository interfaces
│   ├── GroundUp.Settings.Data.Postgres/           # Settings EF context + migrations
│   ├── GroundUp.Settings.Core/                    # Settings DTOs, entities, enums
│   │
│   ├── GroundUp.Notifications/                    # Notification controllers
│   ├── GroundUp.Notifications.Services/           # INotificationService, template rendering
│   ├── GroundUp.Notifications.Core/               # Notification DTOs, entities, channel enums
│   ├── GroundUp.Notifications.Email/              # SMTP/SendGrid implementation
│   ├── GroundUp.Notifications.Sms/                # Twilio implementation (future)
│   ├── GroundUp.Notifications.Push/               # Firebase/APNS implementation (future)
│   │
│   ├── GroundUp.BackgroundJobs/                   # IBackgroundJobService abstraction
│   ├── GroundUp.BackgroundJobs.InProcess/          # Simple in-process implementation
│   ├── GroundUp.BackgroundJobs.Hangfire/           # Hangfire implementation (future)
│   │
│   ├── GroundUp.Authentication/                   # Auth controllers, middleware
│   ├── GroundUp.Authentication.Services/          # Auth flows, token service, permission service
│   ├── GroundUp.Authentication.Repositories/      # User, tenant, role, permission repos
│   ├── GroundUp.Authentication.Data.Abstractions/ # Auth repository interfaces
│   ├── GroundUp.Authentication.Data.Postgres/     # Auth EF context, migrations
│   ├── GroundUp.Authentication.Core/              # Auth DTOs, entities, config, security attributes
│   ├── GroundUp.Authentication.Keycloak/          # Keycloak-specific: admin API, realm management
│   │
│   ├── GroundUp.Audit/                            # Audit module (event-driven change tracking)
│   ├── GroundUp.Audit.Core/
│   ├── GroundUp.Audit.Data.Postgres/
│   │
│   ├── GroundUp.Security/                         # Object-level security (ACL/resource authorization)
│   ├── GroundUp.Security.Core/
│   ├── GroundUp.Security.Data.Postgres/
│   │
│   ├── GroundUp.Caching/                          # ICache abstraction + in-memory default (future)
│   ├── GroundUp.Caching.Redis/                    # Redis implementation (future)
│   │
│   ├── GroundUp.FileStorage/                      # IFileStorageService abstraction (future)
│   ├── GroundUp.FileStorage.Local/                # Local disk implementation (future)
│   ├── GroundUp.FileStorage.S3/                   # AWS S3 implementation (future)
│   │
│   ├── GroundUp.FeatureFlags/                     # Feature flag / dark launching (future)
│   ├── GroundUp.FeatureFlags.Core/
│   ├── GroundUp.FeatureFlags.Data.Postgres/
│   │
│   ├── GroundUp.Webhooks/                         # Outbound webhook delivery (future)
│   ├── GroundUp.Webhooks.Core/
│   ├── GroundUp.Webhooks.Data.Postgres/
│   │
│   ├── GroundUp.Import/                           # Bulk import framework (future)
│   ├── GroundUp.Import.Core/
│   │
│   ├── GroundUp.Workflow/                         # Workflow/state machine engine (future)
│   ├── GroundUp.Workflow.Core/
│   ├── GroundUp.Workflow.Data.Postgres/
│   │
│   ├── GroundUp.UI.Metadata/                      # Data-driven UI metadata for DTOs (future)
│   │
│   └── GroundUp.Events.RabbitMQ/                  # Distributed event bus (future)
│       GroundUp.Events.Aws.Sns/                   # AWS SNS implementation (future)
│       GroundUp.Events.Kafka/                     # Kafka implementation (future)
│
├── tests/
│   ├── GroundUp.Api.Tests.Unit/
│   ├── GroundUp.Api.Tests.Integration/
│   ├── GroundUp.Settings.Tests.Unit/
│   ├── GroundUp.Settings.Tests.Integration/
│   ├── GroundUp.Authentication.Tests.Unit/
│   ├── GroundUp.Authentication.Tests.Integration/
│   └── ...
│
└── samples/
    └── GroundUp.Sample/               # Example consuming application
        ├── Program.cs                 # Shows how to wire up GroundUp packages
        ├── Dockerfile
        └── docker-compose.yml
```

### Project Dependency Rules

```
API → Services → Repositories → Data.Abstractions ← Data.Postgres
                                                   ← Data.SqlServer (future)
All layers → Core (DTOs, entities, interfaces, enums)
Events → Core only (no other dependencies)
```

**Hard rules:**
- API projects ONLY depend on their Services project and Core
- Services depend on their own Data.Abstractions (interfaces) and Core
- Services MAY depend on other modules' service INTERFACES (e.g., GroundUp.Authentication.Services can call into GroundUp.Settings.Services interfaces). Cross-module communication goes through service interfaces — never directly access another module's repositories.
- Repositories depend on Data.Abstractions and Core
- Data.Postgres depends on Repositories (implements interfaces), Data.Abstractions, and Core
- Core has ZERO dependencies on other GroundUp projects
- Events has ZERO dependencies on other GroundUp projects except Core

**Cross-module dependencies are expected and legitimate.** The service layer exists specifically to orchestrate across boundaries. For example, the authentication module's services may call settings services to look up auth configuration. A consuming application's `MyStore.Services` will call into `GroundUp.Authentication.Services` and `GroundUp.Settings.Services`. The rule is: always go through the other module's service interfaces, never access their repositories directly.

---

## Module Detail: GroundUp.Api (Core Foundation)

### GroundUp.Api.Core

Contains ONLY shared types with zero GroundUp dependencies.

**Base entity (minimal — just ID):**
```csharp
public abstract class BaseEntity
{
    public Guid Id { get; set; }  // UUID v7 (sequential, sortable)
}
```

**Auditable interface (opt-in):**
```csharp
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}
```
Entities that need audit fields implement IAuditable. The SaveChanges interceptor checks for IAuditable and only sets those fields on entities that implement it. Simple lookup tables or reference entities don't need this overhead.

**Soft-deletable interface (opt-in):**
```csharp
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
```
Soft delete is NOT the default. Entities opt in by implementing ISoftDeletable. The SaveChanges interceptor intercepts delete calls and converts them to soft deletes for entities implementing this interface. Global query filters for IsDeleted are applied only to ISoftDeletable entities. Hard delete is the default behavior for entities that don't implement ISoftDeletable. The consuming application decides what's appropriate for their domain.

**Tenant entity interface:**
```csharp
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}
```

**OperationResult (the ONE result type — no ApiResponse, no other wrappers):**
```csharp
public class OperationResult<T>
{
    public T? Data { get; set; }
    public bool Success { get; set; } = true;
    public string Message { get; set; } = "Success";
    public List<string>? Errors { get; set; }
    public int StatusCode { get; set; } = 200;
    public string? ErrorCode { get; set; }

    public static OperationResult<T> Ok(T? data, string message = "Success", int statusCode = 200) => ...;
    public static OperationResult<T> Fail(string message, int statusCode, string? errorCode = null, List<string>? errors = null) => ...;
    public static OperationResult<T> NotFound(string message = "Item not found") => ...;
    public static OperationResult<T> BadRequest(string message, List<string>? errors = null) => ...;
    public static OperationResult<T> Unauthorized(string message = "Unauthorized") => ...;
    public static OperationResult<T> Forbidden(string message = "Forbidden") => ...;
}
```

**Typed Exception Hierarchy:**
```csharp
public class GroundUpException : Exception { ... }
public class ForbiddenAccessException : GroundUpException { ... }        // 403
public class NotFoundException : GroundUpException { ... }               // 404
public class ConflictException : GroundUpException { ... }               // 409 (duplicate, optimistic concurrency)
public class ValidationException : GroundUpException { ... }             // 400
public class BusinessRuleException : GroundUpException { ... }           // 422
```
Each maps to a specific HTTP status code in ExceptionHandlingMiddleware. Use these for infrastructure/cross-cutting errors. For business logic within services, prefer OperationResult.Fail over throwing exceptions.

**FilterParams / PaginatedData:** Carry forward from existing implementation. FilterParams supports exact match, contains, min/max range, multi-value (IN clause), search term, and sorting. PaginatedData wraps results with page info. PaginationParams has MaxPageSize guard (default 100).

**ErrorCodes:** Static class with string constants for standardized error codes.

**ICorrelationContext:** Provides the current request's correlation ID. Every request gets a unique correlation ID that flows through all layers — controllers, services, repositories, event handlers, and logs. Critical for production debugging.

**ICurrentUser / ITenantContext:** Abstractions for the currently authenticated user and tenant context. Defined here in Core so all layers can reference them without depending on the authentication module.

**All IDs are UUID v7.** Use `Guid.CreateVersion7()` (.NET 9) or a polyfill library. Never use int IDs. This is non-negotiable for multi-tenant, on-prem deployable software.

### GroundUp.Api.Repositories

**BaseRepository<TEntity, TDto>:**
- Generic base with full CRUD: GetAllAsync, GetByIdAsync, AddAsync, UpdateAsync, DeleteAsync, ExportAsync
- All methods return `OperationResult<T>` — never throw exceptions for business logic
- QueryShaper hooks: `GetAllInternalAsync`, `GetByIdInternalAsync`, `ExportInternalAsync` accept optional `Func<IQueryable<T>, IQueryable<T>>` for derived repos to customize queries (include navigation properties, add extra filters, etc.)
- Built-in filtering, sorting, paging via FilterParams (carry forward ExpressionHelper logic)
- Soft delete support: if entity implements ISoftDeletable, DeleteAsync performs a soft delete; otherwise it performs a hard delete. A separate HardDeleteAsync method is available for force-removing soft-deletable entities when needed.
- Built-in CSV/JSON export
- Unique constraint violation handling (database-agnostic — push provider-specific detection to the Data project)
- Uses Mapperly for entity ↔ DTO mapping (NOT AutoMapper)

**BaseTenantRepository<TEntity, TDto>:**
- Extends BaseRepository
- Generic constraint: `where TEntity : BaseEntity, ITenantEntity`
- Automatically filters ALL queries by `ITenantContext.TenantId`
- No runtime type checks (`typeof(ITenantEntity).IsAssignableFrom(...)` is gone)
- Tenant enforcement on Update and Delete (verify entity belongs to current tenant before modifying)
- Wraps queryShaper to prepend tenant filter before any derived class customization

**BaseSecuredRepository<TEntity, TDto> (from GroundUp.Security module):**
- Extends BaseTenantRepository
- Adds object-level security filtering (see Security module below)

### GroundUp.Api.Services

**BaseService<TEntity, TDto>:**
- Generic base that wraps a repository and provides pass-through CRUD
- Developers only create a custom service class when they have real business logic
- Validation via FluentValidation: validators are auto-discovered and run before repository calls
- Publishes entity lifecycle events via IEventBus: EntityCreated, EntityUpdated, EntityDeleted
- If no custom service exists, consuming apps can register `BaseService<T, TDto>` directly
- **All authorization happens here** — the service layer is the security boundary, not controllers

### GroundUp.Api (Controllers)

**BaseController<TDto>:**
- Generic base controller with standard CRUD endpoints
- Converts OperationResult to ActionResult automatically (no manual StatusCode mapping)
- Standard route pattern: `api/{resource}`
- All endpoints return `OperationResult<T>` as JSON
- **No [Authorize] attribute by default** — controllers have NO security logic. Authentication and authorization are enforced at the service layer so that GroundUp works both as an API and as an SDK (direct service calls without HTTP). The consuming application decides whether to add [Authorize] to their controllers.
- Controllers are thin HTTP adapters — zero business logic, zero permission checks
- API versioning support built in (consuming app configures versioning strategy)

**Middleware:**
- ExceptionHandlingMiddleware: catches unhandled exceptions, maps typed exceptions to HTTP status codes, returns standardized OperationResult error responses. Uses the typed exception hierarchy — NOT string matching on exception messages.
- CorrelationIdMiddleware: generates or reads X-Correlation-Id header, makes it available via ICorrelationContext, includes in all log entries and outgoing responses.
- Rate limiting via configurable policies (AddGroundUpRateLimiting extension method). Supports per-tenant rate limits tied to Settings.

**Health Checks:**
- GroundUp wires into ASP.NET Core's built-in health check framework
- Each module registers its own health checks when added: database connectivity (Postgres), identity provider reachability (Keycloak), cache availability (Redis), message broker (RabbitMQ)
- `/health` (liveness) and `/ready` (readiness) endpoints
- AddGroundUpHealthChecks() extension method auto-discovers module health checks

**Swagger/OpenAPI:**
- The Sample project includes Swagger (Swashbuckle or Microsoft.AspNetCore.OpenApi) configured out of the box
- Security definitions for bearer token and cookie authentication
- All GroundUp endpoints visible and testable from the Swagger UI
- Consuming applications can include `AddGroundUpSwagger()` for quick setup

**Pagination Response Headers:**
- In addition to PaginatedData in the response body, pagination metadata is included in response headers: X-Total-Count, X-Page-Number, X-Page-Size, X-Total-Pages
- Link headers for next/previous pages following REST conventions

### GroundUp.Api.Data.Abstractions

- Repository interfaces (e.g., `IRoleRepository`)
- `IUnitOfWork` with `ExecuteInTransactionAsync`
- `IDataSeeder` interface: consuming apps and modules implement this to seed required reference data on startup (default roles, system settings, initial permissions). Seeders run on application start and are idempotent (only create data if it doesn't exist).
- No concrete implementations — those live in Data.Postgres

### GroundUp.Api.Data.Postgres

- EF Core DbContext configuration
- Entity configurations (Fluent API via `IEntityTypeConfiguration<T>`, NOT data annotations for schema)
- Migrations
- SaveChanges interceptor that:
  - Sets CreatedAt/UpdatedAt/CreatedBy/UpdatedBy on entities implementing `IAuditable`
  - Converts delete operations to soft deletes for entities implementing `ISoftDeletable`
  - Sets IsDeleted/DeletedAt/DeletedBy on soft-deletable entities
- Global query filter for soft deletes: applied ONLY to entities implementing `ISoftDeletable`
- Provider-specific unique constraint detection (Postgres error codes)
- Connection string and pooling configuration
- Each module's Data.Postgres project optionally accepts its own connection string — if not provided, it uses the default/shared database connection

---

## Module Detail: GroundUp.Settings (Hierarchical Configuration)

### Purpose

A sophisticated settings framework that supports multi-tenancy and cascading values. This is foundational — many other modules (Authentication, Logging, Notifications, etc.) depend on it. Build this early.

### Cascading Resolution

Settings cascade through a configurable hierarchy. The "effective value" resolution walks up the chain until it finds a defined value:

```
Feature Level → Application Level → Tenant Level → System Level → Default Value
```

Examples:
- Setting "MaxUploadSizeMB": System=50, Tenant "Acme"=100, Store "Acme West"=200 → effective for Acme West is 200
- Setting "EnableBetaFeatures": System=false, Tenant "Acme"=true → effective for Acme is true
- Setting "LogLevel": only defined at System level → all tenants inherit system value
- Settings can START at any level — there may not be a system-level definition

### Key Design Points

- **Settings are NOT defined in code (appsettings.json) — they are database-driven and manageable at runtime**
- Each setting has metadata: data type, allowed values/options, default value, description, group/category, display order, UI hints (dropdown, toggle, text, number, etc.)
- Settings metadata drives auto-generated settings UI in consuming applications
- Groups and categories for organizing settings (e.g., "Security > Password Policy", "Email > SMTP Settings")
- Settings are scoped: a setting definition specifies which levels it supports (system only, system+tenant, system+tenant+app, etc.)
- Type-safe access: `await _settingsService.GetAsync<int>("MaxUploadSizeMB")` resolves the effective value for the current context (tenant, app, feature)
- Bulk operations: get all settings for a scope, update multiple settings at once
- Change tracking: publish events when settings change so modules can react

---

## Module Detail: GroundUp.Notifications

### Purpose

Send notifications across multiple channels: email, SMS, push notifications, in-app notifications. This is needed by the Authentication module (invitation emails, password resets) and by most consuming applications.

### Design

- `INotificationService` abstraction with `SendAsync(notification)` method
- Notification entity: recipient, channel (Email, SMS, Push, InApp), template, parameters, status (Pending, Sent, Failed), retry count
- Template rendering engine: templates stored in database (tied to Settings), support variable substitution
- Channel-specific implementations in separate packages:
  - `GroundUp.Notifications.Email` — SMTP and SendGrid support
  - `GroundUp.Notifications.Sms` — Twilio (future)
  - `GroundUp.Notifications.Push` — Firebase/APNS (future)
- In-app notification support: notifications stored in database, queryable per user, mark as read/unread
- Notification preferences per user (opt-in/out of channels)
- Integrates with Background Jobs for async delivery and retries
- Multi-tenant: notification templates and preferences are per-tenant

---

## Module Detail: GroundUp.BackgroundJobs

### Purpose

Schedule and execute background tasks: send emails asynchronously, clean up expired invitations, process queued work, run periodic maintenance.

### Design

- `IBackgroundJobService` abstraction:
  - `EnqueueAsync<T>(job)` — fire-and-forget
  - `ScheduleAsync<T>(job, delay)` — delayed execution
  - `RecurringAsync<T>(job, cronExpression)` — periodic execution
- `IBackgroundJob` interface that job classes implement
- Default implementation: simple in-process queue using `Channel<T>` with a hosted service consumer. Good enough for development and small deployments.
- `GroundUp.BackgroundJobs.Hangfire` — production-grade implementation with dashboard, persistence, retries (future)
- Jobs are tenant-aware: ITenantContext is available within job execution
- Job failures are logged and retried with configurable retry policies

---

## Module Detail: GroundUp.Authentication

### What It Provides

Authentication is FULLY OPTIONAL. The core GroundUp.Api module works completely without it.

The authentication module supports a spectrum of authentication strategies, from simple to sophisticated:
- Simple API key authentication
- Forms/cookie-based authentication
- JWT bearer token authentication
- Full OpenID Connect with external identity providers (Keycloak, Azure AD, Okta, AWS Cognito)
- The consuming application chooses what's appropriate for their use case

### Core Authentication Features

- JWT token validation and issuance (dual scheme: identity provider tokens + custom app tokens)
- ICurrentUser abstraction (user ID, email, display name, roles, permissions from JWT claims)
- ITenantContext (tenant ID extracted from JWT claims, used by BaseTenantRepository)
- Role → Policy → Permission hierarchy with many-to-many relationships
- Permission checking service with in-memory caching
- Permission enforcement via interceptor + [RequiresPermission] attribute on service interfaces
- Multi-tenant user model: User → UserTenant (many-to-many) with per-tenant external identity mapping
- Tenant management: standard/enterprise types, hierarchical tenants (parent/child), onboarding modes
- Invitation system: email invitations with token-based acceptance (uses Notifications module for delivery)
- Join links: shareable links for tenant onboarding
- Enterprise SSO: domain-based auto-join, custom realm management
- May depend on GroundUp.Settings for configurable auth behavior
- May depend on GroundUp.Notifications for sending invitation/password emails
- May depend on GroundUp.BackgroundJobs for token cleanup, invitation expiration

### Auth Flow Architecture (CRITICAL — Carry Forward)

These flows are battle-tested from the existing GroundUp implementation and must be preserved. They represent significant development investment and handle complex real-world scenarios:

1. **New Organization (standard tenant creation + first user):** User registers via identity provider → callback creates tenant + user + membership (admin). The callback flow parses state parameter to determine flow type.
2. **Invitation flow:** Admin creates invitation → invitee clicks link → redirected to identity provider → callback validates invitation + creates membership + assigns role. Invitation status tracks: Pending → Accepted/Expired/Revoked.
3. **Join link flow:** Admin creates join link → anyone with link registers/logs in → callback validates link (not revoked/expired) + creates membership. Handles "already a member" case.
4. **Enterprise first admin:** Enterprise tenant provisioned (realm created in identity provider) → first admin registers in enterprise realm → callback creates admin membership. Guard prevents re-running. Registration disabled in realm after first admin.
5. **Enterprise SSO auto-join:** User logs in via enterprise realm → if email domain matches tenant's allowlist → auto-create membership with default role. If domain not in allowlist and no invitation → access denied, no membership created.
6. **Multi-tenant user selection:** User with multiple tenant memberships → set-tenant endpoint lists tenants and issues updated token with selected tenant context. Token regenerated on tenant switch.
7. **Token refresh:** Sliding expiration — when token passes halfway point of its lifetime, a new token is issued automatically and cookie/header updated.

### Keycloak Integration (GroundUp.Authentication.Keycloak)

- Lives in a SEPARATE package so it can be swapped for other providers
- Implements `IIdentityProviderService` and `IIdentityProviderAdminService`
- Handles: admin API calls, realm CRUD, client configuration, user provisioning, role extraction from resource_access claims
- The core authentication module works against ANY OpenID Connect provider — Keycloak-specific code is isolated here

### Permission System Design

**Hierarchy:** Permission (global, e.g., "orders.read") → Policy (tenant-scoped collection of permissions) → Role (tenant-scoped, assigned to users, linked to policies). Role types: System, Application, Workspace.

**Enforcement:** Permission interceptor reads `[RequiresPermission]` attributes on service interface methods. Checked at the service layer BEFORE repository calls. Supports permission-based AND role-based checks. Supports "require any" and "require all" modes. Replace Castle.DynamicProxy with a lighter approach (source-generated interceptors or middleware-based).

**Caching:** Permission lookups cached in IMemoryCache per user with configurable TTL (default 15 min). Cache invalidated on permission/role/policy changes.

---

## Module Detail: GroundUp.Security (Object-Level Authorization)

### Purpose

Provides row-level / object-level security. Answers: "Can User A access Entity X with ID Y?" — distinct from the permission system which answers "Can User A perform action Z?"

### Design

**ResourceAccess entity:**
```csharp
public class ResourceAccess : BaseEntity, IAuditable
{
    public string ResourceType { get; set; }   // e.g., "Order", "Document"
    public Guid ResourceId { get; set; }        // The specific entity ID
    public Guid? UserId { get; set; }           // Grant to specific user
    public Guid? RoleId { get; set; }           // Grant to role
    public AccessLevel AccessLevel { get; set; } // Read, Write, Admin
    public Guid TenantId { get; set; }
}

public enum AccessLevel { Read, Write, Admin }
```

**BaseSecuredRepository:** Extends BaseTenantRepository. Adds query filter that joins against ResourceAccess to ensure the current user has access to returned entities. Configurable per entity: some entities are "open within tenant" (default), others require explicit grants.

**IResourceAccessService:** Methods to grant, revoke, check, and list access. Publishes events when access changes.

---

## Module Detail: GroundUp.Audit

### Purpose

Full change tracking: what changed, old value, new value, who, when. Separate from base entity metadata (CreatedAt/UpdatedAt).

### Design

- **Event-driven:** Subscribes to entity lifecycle events via IEventBus (EntityCreated, EntityUpdated, EntityDeleted) for automatic audit logging
- **Manual entries:** Also supports explicit `IAuditService.LogAsync(action, details)` calls for custom business events that don't map to entity changes (e.g., "User exported 500 records", "Admin impersonated User X", "Failed login attempt")
- Stores audit records in a dedicated AuditLog table (same database by default, separate database if consuming app provides a different connection string)
- Configurable per entity: `[Audited]` attribute or global configuration
- AuditLog entity: EntityType, EntityId, Action (Create/Update/Delete/Custom), Changes (JSON of old→new values), UserId, TenantId, Timestamp, CorrelationId
- Never blocks the main operation — audit failures are logged but don't cause the business operation to fail

---

## Module Detail: GroundUp.Events

### Purpose

Provides the `IEventBus` abstraction that all modules use for decoupled communication.

### Design

```csharp
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent;
}

public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}

public interface IEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
    Guid? TenantId { get; }
    string? UserId { get; }
}
```

**Default implementation:** In-process event bus using DI to resolve handlers. Handlers run after the main operation completes (not in the same transaction unless explicitly opted in).

**Future packages:** `GroundUp.Events.RabbitMQ`, `GroundUp.Events.Aws.Sns`, `GroundUp.Events.Kafka` — same IEventBus interface, distributed implementation. Consuming app swaps registration, zero code changes.

---

## Module Detail: GroundUp.Caching (Planned — Future Module)

### Purpose

Provides an `ICache` abstraction with swappable implementations for application-level caching.

### Design

- `ICache` interface: GetAsync, SetAsync, RemoveAsync, ExistsAsync with configurable TTL
- Default implementation: in-memory cache (wraps IMemoryCache with a cleaner API)
- `GroundUp.Caching.Redis` — Redis implementation for distributed caching
- Multi-tenant aware: cache keys automatically scoped by tenant to prevent cross-tenant cache leakage
- The permission service (currently using IMemoryCache directly) should use this abstraction instead

---

## Module Detail: GroundUp.FileStorage (Planned — Future Module)

### Purpose

Store and retrieve files (documents, images, attachments, exports) with a provider-agnostic abstraction.

### Design

- `IFileStorageService`: UploadAsync, DownloadAsync, DeleteAsync, GetUrlAsync, ListAsync
- Metadata tracking: file name, content type, size, upload date, uploader, tenant
- `GroundUp.FileStorage.Local` — local disk storage (development)
- `GroundUp.FileStorage.S3` — AWS S3 (production)
- `GroundUp.FileStorage.AzureBlob` — Azure Blob Storage (future)
- Multi-tenant: files scoped by tenant, no cross-tenant access
- Integrates with Security module for file-level access control

---

## Module Detail: GroundUp.FeatureFlags (Planned — Future Module)

### Purpose

Feature flagging and dark launching system. Controls whether features are active based on configurable rules. Built on top of GroundUp.Settings for cascading and multi-tenant infrastructure.

### Key Design Points

- **Goes beyond simple booleans.** Supports: on/off flags, percentage-based rollout (enable for 10% of users), user/tenant targeting (enable for specific tenants or users), date-based activation (enable after a certain date), and custom rules.
- **Multi-tenant aware.** A feature flag can be enabled at the system level but disabled for a specific tenant, or vice versa. Uses the same cascading resolution as Settings.
- **Temporary by nature.** Feature flags should be easy to create and easy to clean up. Unlike settings which are permanent configuration, feature flags are typically removed once a feature is fully launched.
- **Performance-sensitive.** Feature flag checks happen frequently (potentially every request). Must support caching with fast invalidation.
- **Depends on GroundUp.Settings** for the cascading infrastructure, but provides feature-flag-specific abstractions (targeting rules, rollout percentages, lifecycle management).
- Usage: `if (await _featureFlags.IsEnabledAsync("new-checkout-flow")) { ... }`

---

## Module Detail: GroundUp.Webhooks (Planned — Future Module)

### Purpose

Outbound webhook delivery system. Enterprise customers want to be notified via HTTP when events occur in the system ("When an order is created, POST to this URL").

### Design

- Webhook subscription management: subscribers register URLs with event filters and optional secret for signature verification
- Subscribes to events via IEventBus and dispatches HTTP POST calls to registered webhook URLs
- Delivery tracking: attempts, successes, failures, retry queue
- Payload signing (HMAC-SHA256) so subscribers can verify authenticity
- Dead letter queue for permanently failed deliveries
- Multi-tenant: webhook subscriptions are per-tenant
- Depends on BackgroundJobs for async delivery and retries

---

## Module Detail: GroundUp.Import (Planned — Future Module)

### Purpose

Bulk import framework for loading large datasets from CSV, Excel, or JSON files into the system.

### Design

- `IImportService<TDto>`: ParseAsync, ValidateAsync, ImportAsync
- Column mapping (source column → DTO property) with auto-detection
- Row-level validation with error reporting (row 47: "Email is required")
- Batch processing for large files (process in chunks, report progress)
- Dry-run mode: validate without importing
- Depends on BackgroundJobs for async processing of large imports
- Depends on FileStorage for uploaded import files

---

## Module Detail: GroundUp.Workflow (Planned — Future Module)

### Purpose

Workflow and state machine engine for approval flows, document lifecycles, order processing, and other multi-step business processes.

### Design

- State machine definition: states, transitions, guards (conditions for transition), actions (side effects on transition)
- Workflow templates stored in database, configurable per tenant
- Example: Order workflow: Draft → Submitted → Approved → Fulfilled → Closed (with guards like "only managers can approve")
- Audit trail of all state transitions
- Integration with Notifications (notify approvers) and Events (publish state change events)

---

## Module Detail: GroundUp.UI.Metadata (Planned — Future Module)

### Purpose

Data-driven UI metadata system that enables auto-generated CRUD forms, tables, and detail views from DTO definitions. A thin React frontend reads metadata and dynamically renders UI without writing per-entity UI code.

### Key Design Points

- **Metadata describes how DTOs render in a UI.** For each DTO property: display label, input type (text, dropdown, date picker, toggle, rich text, file upload), field order, grouping/sections, placeholder text, help text, validation messages, visibility rules (hidden, read-only, conditional), and allowed values/options for dropdowns.
- **Metadata endpoint.** Each resource exposes `GET /api/{resource}/metadata` returning the DTO schema with UI annotations. The React frontend fetches this and dynamically generates forms, tables, and detail views.
- **Metadata sources (layered):** Default metadata derived from DTO type information (property names, types, validation attributes) → overridden by configuration-based metadata (customizable per tenant via Settings) → overridden by explicit attribute-based metadata on DTOs. This layering means basic CRUD UI works with zero configuration, but can be refined.
- **Separate from Core.** The metadata system is an opt-in module — it does NOT add attributes or dependencies to GroundUp.Api.Core. DTOs in Core remain clean. Metadata is defined in the UI.Metadata module via a registration/configuration pattern.
- **Supports:** Auto-generated create/edit forms, list/table views with sortable/filterable columns, detail views, bulk actions, and settings pages (reusing the same infrastructure as GroundUp.Settings UI).
- **Not every consuming app needs this.** Apps with custom-designed frontends can ignore this module entirely. It's a productivity accelerator for apps where standard CRUD UI is acceptable.

---

## Localization / i18n Strategy

Not a separate module, but a cross-cutting concern baked into the framework from the start:

- All user-facing strings (error messages, validation messages, setting labels, notification templates) use resource strings, not hardcoded English
- .NET's built-in `IStringLocalizer` and resource files (.resx) for framework-provided strings
- Consuming apps provide their own resource files for their domain-specific strings
- Locale determined from request headers (Accept-Language), user preference, or tenant configuration
- This doesn't mean building full i18n now — it means using `_localizer["ItemNotFound"]` instead of `"Item not found"` so localization can be added later without rewriting every error message
- Multi-language content (translatable entity fields like product names) is a separate, more complex concern for a future module

---

## DI Registration Pattern

Every module exposes extension methods for clean opt-in registration:

```csharp
// In consuming app's Program.cs:
var connectionString = builder.Configuration.GetConnectionString("Default");

builder.Services.AddGroundUpApi();                                      // Core API base classes + correlation ID + health checks
builder.Services.AddGroundUpApiPostgres(connectionString);              // Postgres data layer (default DB)
builder.Services.AddGroundUpEvents();                                    // Event bus (in-process default)
builder.Services.AddGroundUpSettings();                                  // Settings module
builder.Services.AddGroundUpSettingsPostgres(connectionString);          // Settings data (same DB)
builder.Services.AddGroundUpNotifications();                             // Notification service
builder.Services.AddGroundUpNotificationsEmail(builder.Configuration);   // Email channel (SMTP/SendGrid)
builder.Services.AddGroundUpBackgroundJobs();                            // Background job processing
builder.Services.AddGroundUpAuthentication();                            // Auth services + permission system
builder.Services.AddGroundUpAuthenticationPostgres(connectionString);    // Auth data (same DB)
builder.Services.AddGroundUpKeycloak(builder.Configuration);             // Keycloak integration (optional)
builder.Services.AddGroundUpAudit();                                     // Audit system
builder.Services.AddGroundUpAuditPostgres(auditConnectionString);       // Audit data (separate DB example)
builder.Services.AddGroundUpSecurity();                                  // Object-level security
builder.Services.AddGroundUpCaching();                                   // Cache abstraction (future)
builder.Services.AddGroundUpCachingRedis(builder.Configuration);         // Redis cache (future)
builder.Services.AddGroundUpFileStorage();                               // File storage (future)
builder.Services.AddGroundUpFileStorageS3(builder.Configuration);        // S3 provider (future)
builder.Services.AddGroundUpFeatureFlags();                              // Feature flags (future)
builder.Services.AddGroundUpWebhooks();                                  // Outbound webhooks (future)
builder.Services.AddGroundUpUIMetadata();                                // Data-driven UI metadata (future)
builder.Services.AddGroundUpHealthChecks();                              // Auto-discover module health checks
builder.Services.AddGroundUpSwagger();                                   // Swagger/OpenAPI setup
```

Each module's extension method registers ONLY its own services. No module registers another module's services. Every module is independently optional.

---

## Coding Conventions

### General

- .NET 8+ (target net8.0, test with net9.0 compatibility)
- Nullable reference types enabled everywhere (`<Nullable>enable</Nullable>`)
- Async all the way down — no `.Result`, `.Wait()`, or `Task.Run()` wrapping async code
- Use `sealed` on classes that are not designed for inheritance
- Use `records` for DTOs, value objects, and events
- Use `required` keyword on properties that must be set
- File-scoped namespaces (`namespace X;` not `namespace X { }`)
- One class/interface per file (exceptions: small related types like enums with their entity)
- PascalCase for namespaces (e.g., `GroundUp.Api.Core`, not `GroundUp.api.core`)
- All user-facing strings use resource strings for future localization support

### Naming

- Interfaces: `I` prefix (IUserRepository, ITenantContext)
- DTOs: suffix with `Dto` (RoleDto, CreateTenantDto)
- Entities: no suffix (Role, User, Tenant)
- Services: suffix with `Service` (RoleService, PermissionService)
- Repositories: suffix with `Repository` (RoleRepository, BaseRepository)
- Events: past tense describing what happened (EntityCreated, OrderPlaced, PermissionGranted)
- Extension method classes: `{Module}ServiceCollectionExtensions`
- Data seeders: suffix with `Seeder` (DefaultRoleSeeder, SystemSettingsSeeder)

### Error Handling

- Business logic errors: return `OperationResult.Fail(...)` — NEVER throw exceptions
- Infrastructure errors (DB down, network failure): catch in repository, return OperationResult.Fail with 500
- Cross-cutting errors: use typed exception hierarchy (ForbiddenAccessException, NotFoundException, ConflictException, ValidationException, BusinessRuleException)
- ExceptionHandlingMiddleware maps typed exceptions to HTTP status codes automatically
- All error responses include the correlation ID for debugging
- NEVER use string matching on exception messages to determine error type

### Mapping

- Use **Mapperly** (source generator) — NOT AutoMapper
- Mapper classes are partial classes with `[Mapper]` attribute
- One mapper per module (e.g., `ApiCoreMapper`, `AuthenticationMapper`)
- Map entities ↔ DTOs only at the repository boundary

### Validation

- FluentValidation for all input validation
- Validators live in the `.Core` project of each module (next to the DTOs they validate)
- Validation runs in the service layer before repository calls
- Auto-discovery: `services.AddValidatorsFromAssemblyContaining<T>()`

### Logging

- Serilog for structured logging
- ILoggingService abstraction provided by GroundUp (wraps Serilog)
- Correlation ID automatically included in every log entry
- Log meaningful events, not noise. Don't log "Entity added successfully" on every CRUD operation in production.
- Use structured logging: `_logger.LogInformation("User {UserId} joined tenant {TenantId}", userId, tenantId)` — NOT string interpolation
- Never log sensitive data (passwords, tokens, connection strings, PII)

### Database

- EF Core with Fluent API configuration via `IEntityTypeConfiguration<T>` — NOT data annotations for database schema. Data annotations are acceptable for simple DTO validation only (e.g., `[Required]`, `[MaxLength]` on DTOs).
- Why Fluent API: keeps database schema concerns in the Data project, entities in Core stay clean POCOs with no infrastructure dependencies, and Fluent API supports complex configurations (composite keys, owned types, value converters) that annotations cannot express.
- All entity configurations in separate `EntityConfiguration` classes
- Migrations managed per data project (GroundUp.Api.Data.Postgres has its own migrations, GroundUp.Authentication.Data.Postgres has its own, etc.)
- Database-agnostic in repositories — provider-specific code ONLY in Data.Postgres projects
- Always use `AsNoTracking()` for read-only queries
- Connection string comes from the consuming application, not hardcoded or from environment variables in the framework

---

## Testing Strategy

### Unit Tests

- xUnit as test framework
- NSubstitute for mocking (NOT Moq — Moq had a trust violation with SponsorLink in 2023 and many teams migrated away; NSubstitute also has cleaner, more readable syntax)
- Test naming: `MethodName_Scenario_ExpectedResult`
- Each layer tested independently: services mocked with substitute repositories, repositories tested with in-memory provider for logic only
- Test the behavior, not the implementation

### Integration Tests

- Testcontainers for real Postgres instances (NOT EF InMemory provider)
- WebApplicationFactory-based tests that exercise controller → service → repository → database → back
- Each test class gets a fresh database state
- TestAuthHandler for bypassing real authentication in tests
- Test all CRUD operations, filtering, sorting, paging
- Test tenant isolation (ensure tenant A cannot see tenant B's data)
- Test permission enforcement
- Test soft delete behavior (for entities that opt in)
- Verify correlation IDs flow through to responses

### Test Project Structure

```
tests/
├── GroundUp.Api.Tests.Unit/
│   ├── Services/
│   │   └── BaseServiceTests.cs
│   └── Repositories/
│       └── BaseRepositoryTests.cs
├── GroundUp.Api.Tests.Integration/
│   ├── BaseIntegrationTest.cs
│   ├── CustomWebApplicationFactory.cs
│   └── Controllers/
│       └── CrudControllerTests.cs    # Tests generic CRUD via a test entity
```

---

## Docker & Deployment

### Local Development

```yaml
# docker-compose.yml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: groundup
      POSTGRES_USER: groundup
      POSTGRES_PASSWORD: groundup_dev
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U groundup"]
      interval: 5s
      retries: 5

  keycloak:
    image: quay.io/keycloak/keycloak:latest
    command: start-dev
    environment:
      KC_DB: postgres
      KC_DB_URL: jdbc:postgresql://postgres:5432/keycloak
      KC_DB_USERNAME: groundup
      KC_DB_PASSWORD: groundup_dev
      KEYCLOAK_ADMIN: admin
      KEYCLOAK_ADMIN_PASSWORD: admin
    ports:
      - "8080:8080"
    depends_on:
      postgres:
        condition: service_healthy

volumes:
  pgdata:
```

### Production Dockerfile (Multi-stage)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish "samples/GroundUp.Sample/GroundUp.Sample.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER app
EXPOSE 8080
HEALTHCHECK CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "GroundUp.Sample.dll"]
```

**Docker rules:**
- Multi-stage builds ONLY
- Never run as root in production containers
- Health check endpoints required: `/health` (liveness) and `/ready` (readiness)
- No secrets baked into images — use environment variables or secrets management

### AWS Deployment

- ECS Fargate or EKS for cloud hosting
- ECR for container registry
- RDS PostgreSQL for database
- Secrets Manager for connection strings and API keys
- CloudWatch for logging (via Serilog sink)

### On-Premise Deployment

- Docker Compose bundle for simple deployments
- Helm charts for Kubernetes-based customers
- All infrastructure dependencies (database, identity provider, cache, message broker) configurable via environment variables
- No hard dependency on any AWS service in the core framework

---

## NuGet Packaging

### Versioning

- Semantic versioning (SemVer): MAJOR.MINOR.PATCH
- All GroundUp packages share the same version number (monorepo versioning)
- Pre-release versions: `1.0.0-beta.1`, `1.0.0-rc.1`

### Package Metadata

Every .csproj for a NuGet package must include:
```xml
<PropertyGroup>
  <PackageId>GroundUp.Api.Core</PackageId>
  <Version>1.0.0</Version>
  <Authors>GroundUp</Authors>
  <Description>Core types and interfaces for the GroundUp framework</Description>
  <PackageTags>groundup;framework;enterprise</PackageTags>
  <RepositoryUrl>https://github.com/rsbst23/groundup</RepositoryUrl>
</PropertyGroup>
```

### Dependency Management

- Minimize external package dependencies — every dependency is a dependency for consuming apps too
- Pin major versions of critical dependencies (EF Core, FluentValidation, Mapperly)
- GroundUp.Api.Core should have ZERO external NuGet dependencies

---

## Directory-Level CLAUDE.md Rules

### src/GroundUp.Api/CLAUDE.md
```
# GroundUp.Api — Controller Layer Rules
- Controllers are thin HTTP adapters — ZERO business logic, ZERO security checks
- Every controller action converts OperationResult<T> to ActionResult
- NO [Authorize] attribute — security is enforced at the service layer
- No direct repository access — services only
- Standard route pattern: [Route("api/[controller]")]
- Inject services via constructor, not HttpContext.RequestServices
- Swagger is configured in the Sample project for manual API testing
- API versioning configured here
- Correlation ID middleware registered here
- Health check endpoints wired here
```

### src/GroundUp.Api.Services/CLAUDE.md
```
# GroundUp.Api.Services — Service Layer Rules
- This is the business logic AND security boundary
- Services depend on repository INTERFACES (from Data.Abstractions), never concrete implementations
- Services MAY depend on other modules' service interfaces for cross-module orchestration
- All public methods return OperationResult<T>
- Validation happens here via FluentValidation before calling repositories
- Publish domain events via IEventBus after successful operations
- [RequiresPermission] attributes go on service INTERFACE methods
- Services never access HttpContext directly — use ICurrentUser and ITenantContext abstractions
- This layer must work identically whether called from a controller (API) or directly (SDK)
```

### src/GroundUp.Api.Repositories/CLAUDE.md
```
# GroundUp.Api.Repositories — Repository Layer Rules
- Repositories handle data access and entity ↔ DTO mapping
- All public methods return OperationResult<T> — never throw for business logic
- Use queryShaper hooks for customization in derived repositories
- Always use AsNoTracking() for read-only queries
- Tenant filtering is automatic in BaseTenantRepository — never manually filter by tenant
- Soft delete is automatic for ISoftDeletable entities — not all entities use soft delete
- Mapperly mappers used here for entity ↔ DTO conversion
```

### src/GroundUp.Api.Data.Postgres/CLAUDE.md
```
# GroundUp.Api.Data.Postgres — Data Layer Rules
- This project is Postgres-specific — all provider-specific code lives here
- Entity configurations use Fluent API (IEntityTypeConfiguration<T>), never data annotations for schema
- SaveChanges interceptor handles: IAuditable fields, ISoftDeletable interception
- Global query filters for ISoftDeletable entities applied here
- Migrations managed here — one migration history per module
- UUID v7 default value generation configured here
- Provider-specific error detection (Postgres error codes) lives here
- Accepts its own connection string — defaults to shared DB if not specified separately
- IDataSeeder implementations run on startup for reference data seeding
```

---

## Anti-Patterns (DO NOT)

- **Never** use `int` for entity IDs — always UUID v7 (`Guid`)
- **Never** use AutoMapper — use Mapperly
- **Never** throw exceptions for business logic errors — return OperationResult.Fail
- **Never** access repositories directly from controllers — always go through services
- **Never** put security/authorization logic in controllers — service layer is the security boundary
- **Never** hardcode connection strings, secrets, or environment-specific values in framework code
- **Never** hardcode user-facing strings — use resource strings for localization support
- **Never** use `Task.Run(() => asyncMethod()).Result` — async all the way down
- **Never** put provider-specific code (Postgres error codes, MySQL syntax) in repositories or services
- **Never** use string matching on exception messages to determine error type — use typed exceptions
- **Never** use Castle.DynamicProxy in the new implementation
- **Never** create an ApiResponse<T> type — OperationResult<T> is the ONE result type
- **Never** use `DateTime.Now` — always `DateTime.UtcNow`
- **Never** use data annotations for database schema (use Fluent API) — data annotations OK for basic DTO validation only
- **Never** let a module register another module's services in DI
- **Never** use EF InMemory for integration tests — use Testcontainers with real Postgres
- **Never** force soft delete or audit fields on all entities — these are opt-in via interfaces
- **Never** assume authentication is present — the core API module works without it
- **Never** log without including the correlation ID
- **Never** send notifications synchronously in a request pipeline — use background jobs

---

## Build Order for Claude Code

When scaffolding this project from scratch, build in this order:

1. **Solution structure** — create .sln, all .csproj files with correct project references and package references
2. **GroundUp.Api.Core** — BaseEntity, IAuditable, ISoftDeletable, ITenantEntity, OperationResult, FilterParams, PaginatedData, ErrorCodes, typed exceptions, ICurrentUser, ITenantContext, ICorrelationContext
3. **GroundUp.Events** — IEvent, IEventBus, IEventHandler, InProcessEventBus
4. **GroundUp.Api.Data.Abstractions** — Repository interfaces, IUnitOfWork, IDataSeeder
5. **GroundUp.Api.Repositories** — BaseRepository, BaseTenantRepository, ExpressionHelper
6. **GroundUp.Api.Data.Postgres** — DbContext, entity configurations, SaveChanges interceptor (IAuditable + ISoftDeletable), data seeder runner, migrations
7. **GroundUp.Api.Services** — BaseService, validation pipeline
8. **GroundUp.Api** — BaseController, ExceptionHandlingMiddleware, CorrelationIdMiddleware, health checks, API versioning, rate limiting, Swagger, DI extension methods
9. **GroundUp.Settings.Core** — Setting, SettingDefinition, SettingValue entities and DTOs
10. **GroundUp.Settings.*** — remaining settings projects (cascading resolution, persistence)
11. **GroundUp.Notifications.Core** — Notification entities and DTOs
12. **GroundUp.Notifications.*** — notification service, email channel
13. **GroundUp.BackgroundJobs** — IBackgroundJobService, in-process implementation
14. **GroundUp.Authentication.Core** — Auth entities (User, Tenant, Role, Policy, Permission, UserTenant, etc.), auth DTOs, security attributes, KeycloakConfiguration
15. **GroundUp.Authentication.*** — remaining auth projects following same pattern
16. **GroundUp.Authentication.Keycloak** — Keycloak-specific implementation
17. **GroundUp.Security.*** — Object-level security
18. **GroundUp.Audit.*** — Audit system
19. **Tests** — unit and integration tests for each module
20. **GroundUp.Sample** — example consuming application demonstrating all features with Swagger
21. **Docker** — docker-compose.yml, Dockerfiles
22. **CI/CD** — GitHub Actions workflows with path-based triggers
23. **GroundUp.Caching** — ICache abstraction + in-memory default (future)
24. **GroundUp.FileStorage** — IFileStorageService + local disk (future)
25. **GroundUp.FeatureFlags** — Feature flagging system (future)
26. **GroundUp.Webhooks** — Outbound webhook delivery (future)
27. **GroundUp.Import** — Bulk import framework (future)
28. **GroundUp.Workflow** — Workflow/state machine engine (future)
29. **GroundUp.UI.Metadata** — Data-driven UI metadata (future)
30. **NuGet Packaging** — dotnet pack, local feed, MyStore consuming app

---

## Key Patterns to Preserve from Existing Codebase

These patterns were proven in the existing GroundUp implementation and must be carried forward:

1. **QueryShaper hooks** — the `Func<IQueryable<T>, IQueryable<T>>? queryShaper` pattern in base repositories for derived repos to include navigation properties or add custom filters
2. **FilterParams with multiple filter types** — exact match, contains, min/max range, multi-value, search term, sorting, paging
3. **Tenant hierarchy** — ParentTenantId for enterprise parent/child relationships
4. **User → UserTenant many-to-many** — users can belong to multiple tenants, each mapping has its own ExternalUserId for the identity provider
5. **Permission interceptor pattern** — attribute-based permission enforcement at the service layer (but modernize the implementation away from Castle.DynamicProxy)
6. **Dual JWT authentication** — support both identity provider tokens and custom app tokens
7. **Auth flow state machine** — the callback-based flow with state parameter encoding (flow type, invitation token, join token, realm)
8. **Permission caching** — in-memory cache with per-user keys and invalidation on permission changes
9. **ExpressionHelper** — dynamic LINQ expression building for filtering and sorting
10. **OperationResult with static factory methods** — Ok, Fail, NotFound, BadRequest for clean result construction
11. **ServiceCollectionExtensions pattern** — each module provides `AddGroundUp{Module}()` extension methods for DI registration
