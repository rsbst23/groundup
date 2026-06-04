# GroundUp — Phased Build Plan

This document breaks the GroundUp framework build into iterative phases. Each phase includes what to build, success criteria, and how to manually verify everything works. Take it slow. Validate as you go. Commit after each meaningful milestone.

---

## Phase 0: Environment & Repository Setup

### Goal
Get your development environment fully configured and the empty repository ready.

### Steps

**0.1 — Create the GitHub repository**
1. Go to https://github.com/new
2. Name: `groundup`
3. Visibility: your choice (private recommended during development)
4. Initialize with a README
5. Add a `.gitignore` for Visual Studio / .NET
6. Create the repo

**0.2 — Clone locally**
```powershell
cd C:\repos   # or wherever you keep projects
git clone https://github.com/rsbst23/groundup.git
cd groundup
```

**0.3 — Add the CLAUDE.md and BUILD_PHASES.md**
- Copy the CLAUDE.md and BUILD_PHASES.md files into the root of the repo
- Commit:
```powershell
git add CLAUDE.md BUILD_PHASES.md
git commit -m "Add CLAUDE.md and BUILD_PHASES.md"
git push
```

**0.4 — Verify Docker Desktop**
- Open Docker Desktop and confirm it's running
- Open a terminal and run:
```powershell
docker --version
docker compose version
```
- Both should return version numbers

**0.5 — Start Postgres via Docker**
- Create a `docker-compose.yml` in the repo root (Claude Code can do this, or copy from CLAUDE.md)
- Run:
```powershell
docker compose up -d postgres
```
- Verify Postgres is running:
```powershell
docker compose ps
```
- You should see the postgres container running and healthy
- Commit the docker-compose.yml

**0.6 — Verify Visual Studio 2022**
- Open Visual Studio 2022
- Confirm you have the "ASP.NET and web development" workload installed
- Install the latest .NET 8 SDK if not already installed: https://dotnet.microsoft.com/download/dotnet/8.0
- Verify from terminal:
```powershell
dotnet --version
```
- Should show 8.x.x or 9.x.x

**0.7 — Install Claude Code**
```powershell
npm install -g @anthropic-ai/claude-code
```
- Requires Node.js 18+. If you don't have Node.js:
  - Download from https://nodejs.org (LTS version)
  - Install, then retry the npm command
- After install, authenticate:
```powershell
cd C:\repos\groundup
claude
```
- It will prompt you to log in with your Anthropic account
- Once connected, ask: "What do you know about this project?"
- Claude Code should describe the GroundUp framework from your CLAUDE.md
- Type `/exit` to close Claude Code for now

### Success Criteria
- [ ] GitHub repo exists with CLAUDE.md and BUILD_PHASES.md committed
- [ ] Docker Desktop running, Postgres container healthy
- [ ] `dotnet --version` returns 8.x or 9.x
- [ ] Visual Studio 2022 opens and has ASP.NET workload
- [ ] Claude Code installed and recognizes the project
- [ ] docker-compose.yml committed

### Commit
```powershell
git add -A
git commit -m "Phase 0: Environment setup complete"
git push
```

---

## Phase 1: Solution Structure & Core Types

### Goal
Create the solution file, the initial project files with correct references, and the foundational types in GroundUp.Api.Core. Everything compiles. No business logic yet.

### What to Build

**1.1 — Create the solution and projects**

Open Claude Code in the repo:
```powershell
cd C:\repos\groundup
claude
```

Ask Claude Code to create:
- `groundup.sln`
- `src/GroundUp.Api.Core/GroundUp.Api.Core.csproj` — class library, net8.0, zero external dependencies
- `src/GroundUp.Events/GroundUp.Events.csproj` — class library, references Api.Core
- `src/GroundUp.Api.Data.Abstractions/GroundUp.Api.Data.Abstractions.csproj` — class library, references Api.Core
- `src/GroundUp.Api.Repositories/GroundUp.Api.Repositories.csproj` — class library, references Api.Core, Api.Data.Abstractions, Events
- `src/GroundUp.Api.Data.Postgres/GroundUp.Api.Data.Postgres.csproj` — class library, references Api.Core, Api.Data.Abstractions, Api.Repositories (EF Core + Npgsql packages)
- `src/GroundUp.Api.Services/GroundUp.Api.Services.csproj` — class library, references Api.Core, Api.Data.Abstractions, Events (FluentValidation package)
- `src/GroundUp.Api/GroundUp.Api.csproj` — class library (NOT web project), references Api.Core, Api.Services
- `samples/GroundUp.Sample/GroundUp.Sample.csproj` — web project (ASP.NET Core), references ALL of the above
- `tests/GroundUp.Api.Tests.Unit/GroundUp.Api.Tests.Unit.csproj` — xUnit test project
- `tests/GroundUp.Api.Tests.Integration/GroundUp.Api.Tests.Integration.csproj` — xUnit test project

**Verification after 1.1:**
```powershell
dotnet build groundup.sln
```
Should compile with zero errors (projects are mostly empty at this point).

**Commit:**
```powershell
git add -A
git commit -m "Phase 1.1: Solution structure with all core projects"
git push
```

**1.2 — Build GroundUp.Api.Core types**

Ask Claude Code to create:
- `BaseEntity` (abstract, Guid Id only)
- `IAuditable` interface (CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
- `ISoftDeletable` interface (IsDeleted, DeletedAt, DeletedBy)
- `ITenantEntity` interface (Guid TenantId)
- `ICurrentUser` interface (Guid UserId, string? Email, string? DisplayName)
- `ITenantContext` interface (Guid TenantId)
- `ICorrelationContext` interface (string CorrelationId)
- `OperationResult<T>` with static factory methods (Ok, Fail, NotFound, BadRequest, Unauthorized, Forbidden)
- Typed exception hierarchy: `GroundUpException`, `ForbiddenAccessException`, `NotFoundException`, `ConflictException`, `ValidationException`, `BusinessRuleException`
- `PaginationParams` (PageNumber, PageSize with MaxPageSize guard, SortBy)
- `FilterParams` extending PaginationParams (Filters, ContainsFilters, MinFilters, MaxFilters, MultiValueFilters, SearchTerm)
- `PaginatedData<T>` (Items, PageNumber, PageSize, TotalRecords, TotalPages)
- `ErrorCodes` static class

**Verification after 1.2:**
```powershell
dotnet build groundup.sln
```
Zero errors. Open Visual Studio, explore the types, make sure they look right.

**Commit:**
```powershell
git add -A
git commit -m "Phase 1.2: Core types - BaseEntity, OperationResult, FilterParams, exceptions, interfaces"
git push
```

### Success Criteria
- [ ] `dotnet build groundup.sln` compiles with zero errors
- [ ] Solution opens in Visual Studio 2022 and shows all projects
- [ ] Project references are correct (check in Solution Explorer → Dependencies)
- [ ] GroundUp.Api.Core has zero NuGet package dependencies
- [ ] All core types have XML doc comments
- [ ] Typed exception hierarchy in place

---

## Phase 2: Event Bus

### Goal
Build the IEventBus abstraction and in-process implementation.

### What to Build

**2.1 — Event types and interfaces**
- `IEvent` interface
- `IEventBus` interface
- `IEventHandler<T>` interface
- `BaseEvent` abstract record implementing IEvent
- `EntityCreatedEvent<T>`, `EntityUpdatedEvent<T>`, `EntityDeletedEvent<T>`

**2.2 — InProcessEventBus implementation**
- Uses DI to resolve all `IEventHandler<T>` for a given event type
- Calls handlers sequentially (for now)
- Catches and logs handler failures without blocking the publisher

**2.3 — DI registration**
- `AddGroundUpEvents()` extension method that registers InProcessEventBus

**Verification:**
```powershell
dotnet build groundup.sln
```
Write a simple unit test that publishes an event and verifies a handler receives it.

**Commit:**
```powershell
git add -A
git commit -m "Phase 2: Event bus - IEventBus, InProcessEventBus, entity lifecycle events"
git push
```

### Success Criteria
- [ ] `dotnet build` passes
- [ ] Unit test: publish EntityCreatedEvent → handler receives it
- [ ] Unit test: handler failure doesn't throw back to publisher

---

## Phase 3: Base Repository & Data Layer

### Goal
Build BaseRepository with full CRUD, filtering, sorting, paging, and soft delete support. Wire up EF Core with Postgres. Get the Sample app running with Swagger showing endpoints. This is the largest phase.

### What to Build

**3.1 — Repository interfaces in Data.Abstractions**
- `IBaseRepository<TDto>` — GetAllAsync, GetByIdAsync, AddAsync, UpdateAsync, DeleteAsync
- `IUnitOfWork`
- `IDataSeeder` — interface for reference data seeding on startup

**3.2 — ExpressionHelper in Repositories**
- Port the existing ExpressionHelper logic for dynamic filtering and sorting
- `BuildPredicate`, `BuildContainsPredicate`, `BuildRangePredicate`, `BuildDateRangePredicate`, `ApplySorting`

**3.3 — BaseRepository in Repositories**
- Generic CRUD with queryShaper hooks
- Filtering, sorting, paging via FilterParams
- Soft delete awareness (ISoftDeletable check)
- CSV/JSON export
- Mapperly integration (add Mapperly NuGet package)

**3.4 — BaseTenantRepository in Repositories**
- Extends BaseRepository
- `where TEntity : BaseEntity, ITenantEntity` constraint
- Auto-filters by ITenantContext.TenantId
- Tenant enforcement on Update/Delete

**3.5 — EF Core setup in Data.Postgres**
- `GroundUpDbContext` base class
- SaveChanges interceptor for IAuditable fields
- SaveChanges interceptor for ISoftDeletable (convert delete to soft delete)
- Global query filter for ISoftDeletable
- UUID v7 default value generation
- Postgres-specific unique constraint detection
- Data seeder runner (discovers and runs IDataSeeder implementations on startup)
- `AddGroundUpApiPostgres(connectionString)` extension method

**3.6 — BaseService in Services**
- Generic pass-through CRUD wrapping a repository
- FluentValidation pipeline (auto-discover validators, run before repo calls)
- Entity lifecycle event publishing via IEventBus
- `AddGroundUpApi()` extension method

**3.7 — BaseController and Middleware in Api**
- Generic CRUD endpoints (GET all, GET by id, POST, PUT, DELETE)
- OperationResult → ActionResult conversion
- ExceptionHandlingMiddleware with typed exception mapping
- CorrelationIdMiddleware (generate/read X-Correlation-Id, flow through logs)
- Health check wiring (AddGroundUpHealthChecks)
- API versioning setup
- Pagination response headers (X-Total-Count, X-Page-Number, etc.)
- `AddGroundUpApiControllers()` extension method

**3.8 — Sample app wired up**
- Create a test entity: `TodoItem` (Title, Description, IsComplete, DueDate) — just for validation purposes
- TodoItemDto, TodoItemMapper (Mapperly)
- Wire up in Sample's Program.cs:
  - AddGroundUpApi()
  - AddGroundUpApiPostgres(connectionString)
  - AddGroundUpEvents()
  - AddGroundUpHealthChecks()
  - Swagger setup
  - Middleware pipeline (correlation ID, exception handling)
- EF migration for TodoItem table
- docker-compose has Postgres running

### Manual Verification

**Start the stack:**
```powershell
docker compose up -d postgres
cd samples/GroundUp.Sample
dotnet run
```

**Open Swagger:**
- Navigate to `https://localhost:{port}/swagger`
- You should see TodoItem CRUD endpoints

**Test in Swagger:**
1. POST /api/todoitems — create a todo item → should return 201 with the created item and a GUID id
2. GET /api/todoitems — should return the item you created, wrapped in PaginatedData
3. GET /api/todoitems/{id} — should return the specific item
4. PUT /api/todoitems/{id} — update the title → should return 200
5. DELETE /api/todoitems/{id} — delete → should return 200
6. GET /api/todoitems — should be empty (or show IsDeleted=true if TodoItem implements ISoftDeletable)

**Test filtering:**
- Create 5+ todo items with different titles
- GET /api/todoitems?Filters[Title]=MyTodo → should filter
- GET /api/todoitems?SortBy=Title → should sort
- GET /api/todoitems?PageSize=2&PageNumber=1 → should page

**Test correlation ID:**
- Check response headers for X-Correlation-Id
- Send a request with X-Correlation-Id header → response should echo same ID

**Test health checks:**
- GET /health → should return healthy
- GET /ready → should return healthy

**Verify database:**
- Connect to Postgres (use pgAdmin, DBeaver, or VS extension):
  - Host: localhost, Port: 5432, User: groundup, Password: groundup_dev, DB: groundup
- Check that the TodoItems table exists and has the expected columns
- Check that IDs are GUIDs, not integers

### Success Criteria
- [ ] `dotnet build` passes
- [ ] Sample app starts without errors
- [ ] Swagger UI loads and shows endpoints
- [ ] CRUD operations work end-to-end through Swagger
- [ ] Filtering, sorting, and paging work
- [ ] IDs are GUIDs in the database
- [ ] If TodoItem implements IAuditable: CreatedAt is auto-set on create, UpdatedAt on update
- [ ] Postgres is the real database (not InMemory)
- [ ] Correlation ID appears in response headers
- [ ] Health check endpoints return healthy
- [ ] Pagination headers present in list responses

### Commits (commit after each sub-step)
```powershell
git add -A && git commit -m "Phase 3.1: Repository interfaces and IDataSeeder"
git add -A && git commit -m "Phase 3.2: ExpressionHelper for dynamic filtering"
git add -A && git commit -m "Phase 3.3: BaseRepository with CRUD, filtering, paging"
git add -A && git commit -m "Phase 3.4: BaseTenantRepository with tenant isolation"
git add -A && git commit -m "Phase 3.5: EF Core Postgres setup with interceptors and data seeding"
git add -A && git commit -m "Phase 3.6: BaseService with validation and events"
git add -A && git commit -m "Phase 3.7: BaseController, middleware, health checks, correlation ID"
git add -A && git commit -m "Phase 3.8: Sample app with TodoItem - end-to-end working"
git push
```

---

## Phase 4: Testing Foundation

### Goal
Set up unit and integration test infrastructure. Write tests that validate the base classes work correctly.

### What to Build

**4.1 — Unit test infrastructure**
- Add NSubstitute to unit test project
- BaseService unit tests: verify it calls repository methods, runs validation, publishes events

**4.2 — Integration test infrastructure**
- Add Testcontainers.PostgreSql to integration test project
- `CustomWebApplicationFactory` that:
  - Spins up a Testcontainers Postgres instance
  - Replaces the connection string
  - Runs migrations automatically
  - Provides a fresh database per test class
- `BaseIntegrationTest` base class with HttpClient, scope, cleanup
- `TestAuthHandler` for bypassing auth (not needed yet, but scaffold it)

**4.3 — Integration tests for TodoItem CRUD**
- Test: Create a todo item → returns 201 with GUID id
- Test: Get all → returns paginated results
- Test: Get by id → returns the item
- Test: Update → returns updated item
- Test: Delete → item no longer returned by Get
- Test: Filtering by title
- Test: Paging (create 10 items, page size 3, verify page count)
- Test: Sorting
- Test: Correlation ID flows through in response headers

### Manual Verification
```powershell
dotnet test groundup.sln
```
All tests should pass. Integration tests will take a few seconds to spin up Postgres containers.

### Success Criteria
- [ ] `dotnet test` passes all unit tests
- [ ] `dotnet test` passes all integration tests
- [ ] Integration tests use real Postgres (via Testcontainers), not InMemory
- [ ] Tests are independent (can run in any order)
- [ ] Test output shows Testcontainers starting/stopping Postgres

### Commit
```powershell
git add -A
git commit -m "Phase 4: Testing foundation - unit tests, integration tests with Testcontainers"
git push
```

---

## Phase 5: Multi-Tenancy

### Goal
Prove that BaseTenantRepository correctly isolates data by tenant. This is one of the most critical features of GroundUp.

### What to Build

**5.1 — TenantContext implementation**
- `TenantContext` class implementing `ITenantContext`
- For now, reads tenant ID from a header (X-Tenant-Id) or a claim — simple implementation for testing
- Register in DI as scoped

**5.2 — Tenant-scoped test entity**
- Create `Project` entity (Name, Description, TenantId) implementing ITenantEntity, IAuditable
- ProjectDto, ProjectMapper
- ProjectRepository extending BaseTenantRepository
- ProjectService extending BaseService
- ProjectController extending BaseController
- Add to Sample app's Program.cs
- Run migration

**5.3 — Tenant isolation integration tests**
- Test: Create project as Tenant A → Get as Tenant A → returns it
- Test: Create project as Tenant A → Get as Tenant B → returns empty (CRITICAL)
- Test: Create project as Tenant A → Update as Tenant B → returns NotFound (CRITICAL)
- Test: Create project as Tenant A → Delete as Tenant B → returns NotFound (CRITICAL)
- Test: Create projects for Tenant A and Tenant B → Get all as Tenant A → only Tenant A's projects returned

### Manual Verification

**In Swagger:**
1. Set header X-Tenant-Id to a GUID (e.g., `11111111-1111-1111-1111-111111111111`)
2. Create a project
3. Get all projects → should return it
4. Change X-Tenant-Id to a different GUID
5. Get all projects → should return EMPTY — you should NOT see Tenant A's project
6. Create a project under Tenant B
7. Switch back to Tenant A's header → should only see Tenant A's project

This is the most important manual test. If tenant isolation fails, stop and fix it before proceeding.

### Success Criteria
- [ ] All tenant isolation integration tests pass
- [ ] Manual Swagger test confirms cross-tenant data is invisible
- [ ] Tenant ID is stored correctly in the database
- [ ] No way to access another tenant's data through filtering, sorting, or direct ID lookup

### Commit
```powershell
git add -A
git commit -m "Phase 5: Multi-tenancy with BaseTenantRepository and tenant isolation tests"
git push
```

---

## Phase 6: Settings Module

### Goal
Build the hierarchical settings system with cascading resolution.

### What to Build

**6.1 — Settings Core types**
- SettingDefinition entity (Key, DataType, DefaultValue, Description, Group, Category, DisplayOrder, SupportedLevels, UIHints/Options)
- SettingValue entity (SettingDefinitionId, Level, LevelId, Value)
- Settings DTOs
- Setting level enum (System, Tenant, Application, Feature)

**6.2 — Settings data layer**
- Settings repository interfaces
- Settings EF configurations
- Settings DbContext / migrations
- AddGroundUpSettingsPostgres() extension method

**6.3 — Settings service with cascading resolution**
- `ISettingsService.GetAsync<T>(key)` — resolves effective value walking up the cascade
- `ISettingsService.SetAsync(key, value, level, levelId)` — sets value at a specific level
- `ISettingsService.GetAllForScopeAsync(level, levelId)` — returns all settings with effective values for a scope
- Caching layer for resolved settings
- Publish SettingChangedEvent when values change

**6.4 — Settings controllers**
- CRUD for setting definitions (admin)
- Get/Set setting values at different levels
- Get effective settings for current tenant

**6.5 — Wire into Sample app**
- Register settings module
- Create a DefaultSettingsSeeder (IDataSeeder) to seed example setting definitions
- Test cascading: set system-level value, override at tenant level, verify resolution

### Manual Verification

**In Swagger:**
1. Create a setting definition: "MaxUploadSizeMB", DataType=int, Default=50
2. Get effective value (no overrides) → should return 50
3. Set tenant-level value to 100
4. Get effective value for that tenant → should return 100
5. Get effective value for a different tenant → should return 50 (system default)
6. Delete the tenant override
7. Get effective value → should return 50 again

### Success Criteria
- [ ] Cascading resolution works: Feature → App → Tenant → System → Default
- [ ] Settings can start at any level
- [ ] Type-safe retrieval works (GetAsync<int>, GetAsync<bool>, GetAsync<string>)
- [ ] Setting changes publish events
- [ ] Integration tests cover cascading scenarios
- [ ] Settings metadata (groups, categories, UI hints) is persisted and retrievable
- [ ] Data seeder creates initial setting definitions on startup

### Commit
```powershell
git add -A
git commit -m "Phase 6: Settings module with cascading resolution"
git push
```

---

## Phase 7: Notifications Module

### Goal
Build the notification system that other modules (especially Authentication) depend on for sending emails, and eventually SMS and push notifications.

### What to Build

**7.1 — Notifications Core**
- Notification entity (Recipient, Channel, Template, Parameters, Status, RetryCount)
- NotificationChannel enum (Email, SMS, Push, InApp)
- NotificationStatus enum (Pending, Sent, Failed)
- Notification DTOs

**7.2 — Notification service**
- `INotificationService.SendAsync(notification)` — queues or sends a notification
- Template rendering: variable substitution in templates
- In-app notification support: store and query per user, mark as read/unread

**7.3 — Email channel**
- `GroundUp.Notifications.Email` project
- SMTP provider implementation
- SendGrid provider implementation (optional, can be added later)
- Email-specific configuration (from Settings module)

**7.4 — Wire into Sample app**
- Register notification module
- Send a test email via Swagger
- Query in-app notifications

### Manual Verification

**In Swagger:**
1. Send a test email notification via POST endpoint
2. Check that notification is recorded in the database with correct status
3. If SMTP is configured, verify email arrives
4. Create an in-app notification, query it, mark as read

### Success Criteria
- [ ] INotificationService sends email notifications
- [ ] Notification records persisted with status tracking
- [ ] In-app notifications queryable per user
- [ ] Template variable substitution works
- [ ] Integration tests for notification creation and retrieval

### Commit
```powershell
git add -A
git commit -m "Phase 7: Notifications module with email channel"
git push
```

---

## Phase 8: Background Jobs Module

### Goal
Build the background job system for async processing. Needed by Notifications (async email delivery), Authentication (invitation expiration), and other modules.

### What to Build

**8.1 — Background job abstractions**
- `IBackgroundJobService`: EnqueueAsync, ScheduleAsync, RecurringAsync
- `IBackgroundJob` interface

**8.2 — In-process implementation**
- Simple queue using `Channel<T>` with a hosted service consumer
- Job execution with tenant context preservation
- Configurable retry with exponential backoff
- Job failure logging

**8.3 — Wire into Sample app and Notifications**
- Register background jobs module
- Update Notifications to use background jobs for async email delivery
- Create a sample recurring job (e.g., cleanup expired data)

### Manual Verification

**In Swagger:**
1. Trigger a notification that sends via background job
2. Check logs to verify job executed asynchronously
3. Verify recurring job runs on schedule (check logs)

### Success Criteria
- [ ] Fire-and-forget jobs execute asynchronously
- [ ] Scheduled jobs execute after delay
- [ ] Recurring jobs execute on schedule
- [ ] Job failures are logged and retried
- [ ] Notifications use background jobs for delivery
- [ ] Tenant context preserved within job execution

### Commit
```powershell
git add -A
git commit -m "Phase 8: Background jobs with in-process implementation"
git push
```

---

## Phase 9: Authentication Foundation

### Goal
Build the core authentication infrastructure — user/tenant entities, permission system, JWT handling. NOT the Keycloak integration yet.

### What to Build

**9.1 — Auth Core entities and DTOs**
- User, Tenant (with hierarchy), UserTenant junction, Role, Policy, Permission
- Junction tables: RolePolicy, PolicyPermission, UserRole
- TenantInvitation, TenantJoinLink
- All related DTOs
- RequiresPermissionAttribute

**9.2 — Auth data layer**
- Auth repository interfaces, EF configurations, DbContext, migrations
- Auth-specific repositories (UserRepository, TenantRepository, RoleRepository, PermissionRepository)
- Auth data seeders (default system roles, default permissions)

**9.3 — Permission service and enforcement**
- HasPermission, HasAnyPermission, GetUserPermissions with caching
- Permission interceptor (replace Castle.DynamicProxy)
- ICurrentUser and ITenantContext implementations from JWT claims

**9.4 — JWT token service**
- Generate/validate custom JWT tokens
- Dual scheme support

**9.5 — Auth DI registration**
- AddGroundUpAuthentication() and AddGroundUpAuthenticationPostgres()

### Manual Verification
1. Create roles, policies, permissions via Swagger
2. Assign permissions to policies, policies to roles
3. Verify permission checks work on protected service methods

### Success Criteria
- [ ] All auth entities created and migrated
- [ ] Permission hierarchy (Permission → Policy → Role) works
- [ ] Permission checking with caching works
- [ ] [RequiresPermission] enforcement works at service layer
- [ ] JWT token generation and validation works
- [ ] Default roles and permissions seeded on startup

### Commit
```powershell
git add -A
git commit -m "Phase 9: Authentication foundation - entities, permissions, JWT"
git push
```

---

## Phase 10: Keycloak Integration & Auth Flows

### Goal
Integrate with Keycloak and implement all 7 auth flows from the existing GroundUp implementation: New Organization, Invitation, Join Link, Enterprise First Admin, Enterprise SSO Auto-Join, Multi-Tenant Selection, Token Refresh.

### Cross-cutting decisions for Phase 10

These decisions span all sub-phases and should be referenced when writing each spec.

**Multi-tenancy framing.** GroundUp is multi-tenant by default. A single-tenant app is a multi-tenant app with one tenant — typically the seeded system tenant. There is no separate code path. A deployment can run with only standard tenants, only enterprise tenants, or both.

**State parameter is stateful.** OAuth `state` is an opaque GUID id pointing to an `AuthFlowState` row. Provides one-shot replay protection (`ConsumedAt`), revocation visibility, audit trail, and bounded payload size. Cleaner alternative to a signed JWT state because invitations and join links already live in the DB — keeping flow context there is consistent.

**Notifications and BackgroundJobs are stubbed.** Phases 7 and 8 (Notifications, BackgroundJobs) were not built before Phase 10. Phase 10 ships with `IInvitationEmailSender` (default: `LoggingInvitationEmailSender`) and an `IHostedService`-based `AuthFlowState` cleanup sweeper. GU-64 tracks the future swap to Notifications + BackgroundJobs.

**Keycloak is reset.** The existing `docker-compose.yml` Keycloak entry is replaced from scratch — pinned version, dedicated `keycloak` Postgres database, realm import on first boot. No data carried over.

**Default tenant for single-tenant deployments.** A configurable default tenant (resolves to the system tenant if not set) makes single-tenant apps Just Work without ever showing the picker.

**Application domain is a system-only setting.** `auth.application.default-domain` is seeded at the system level. Tenant-specific routing uses `Tenant.Slug` as a subdomain — no cascading needed. The unused `Tenant.CustomDomain` column from Phase 9 is dropped in the 10a migration since enterprise customers wanting their own registrable domain self-deploy a single-tenant instance instead of being served from the shared deployment.

**Login behavior by host (CRITICAL).** If `IHostTenantResolver` returns a tenant, the auth flow is **locked to that tenant** — skip the multi-tenant picker, skip auto-select, skip multi-tenant logic.

| Host | Realm | Picker shown? |
|---|---|---|
| `sampleapp.com` (no resolved tenant) | General realm | Yes if user has 2+ memberships; auto-select if 1; deny if 0 |
| `acme.sampleapp.com` (standard tenant on subdomain) | General realm, flow pinned to acme | No. Non-member of acme is denied. |
| `bigco.sampleapp.com` (enterprise tenant on subdomain) | `Tenant.RealmName` | No. Single tenant context by definition. |

**Authentication state across hosts.** The shared GroundUp deployment serves all tenants under subdomains of a single registrable domain (the configured `auth.application.default-domain`). The cookie writer derives the cookie's `Domain` attribute from that setting — when configured, cookies are scoped to `.{default-domain}` so they share across all tenant subdomains; when empty (single-host single-tenant deployment), cookies are host-only. Cross-registrable-domain handoff is **explicitly out of scope**: enterprise customers who want their own registrable domain self-deploy a single-tenant instance instead of being served from the shared deployment.

**`auth.roles.assign-system` lives in `IUserRoleService`.** All role-assignment writes — including via invitations and join links — route through this single service. No bypassing.

**Settings are operational source of truth; secrets are encrypted in the DB.** GroundUp follows a WordPress-style "configure from the UI" model. The only file/env config is the irreducible bootstrap: database connection string, master encryption key, and a one-time bootstrap admin token. Everything else lives in the settings table. Sensitive settings (`IsSecret=true`) are AES-GCM encrypted at rest using the master key. An optional `ISecretResolver` interface routes `secretref://` values through Azure Key Vault, AWS Secrets Manager, or HSM (no implementations ship in Phase 10). The substrate lands in 10ab; 10b is the first phase that uses it for an actual sensitive value (the Keycloak admin client secret).

**First-run setup wizard.** When `BootstrapState.IsComplete=false`, all routes redirect to `/setup/*` and a one-time bootstrap admin token is the only authentication mechanism. Wizard steps capture app identity, identity-provider URLs, and Keycloak admin bootstrap (Path B: master admin creds passed once, used to provision a service-account client, never persisted), then create the first super admin and exit setup mode. Cannot be re-entered without database manipulation.

### Sub-phase breakdown

Phase 10 is split into six sub-phases with separate specs and PRs. Each sub-phase ships independently and keeps PRs at ~10–15 files. Tracked in Jira under epic GU-9.

| Sub-phase | Title | Jira |
|---|---|---|
| 10a | Keycloak infra, IdP admin contract, AuthFlowState | GU-37 |
| 10ab | Initial Setup & Secrets Foundation | GU-70 |
| 10b | `GroundUp.Auth.Keycloak` provider implementation | GU-38 |
| 10c | Auth dispatcher, host resolver, cookie writer, basic flows | GU-67 |
| 10d | Invitations, Join Links, `IUserRoleService` | GU-68 |
| 10e | Enterprise flows: realm provisioning, first admin, SSO auto-join | GU-69 |

#### Phase 10a — Keycloak infra, IdP admin contract, AuthFlowState (GU-37)

Foundation. No flow logic yet.

- Replace `docker-compose.yml` Keycloak entry: pin version (target 26.x), add a dedicated `keycloak` Postgres database, mount `keycloak/realm.json` import on first boot with default `groundup` realm, client, and redirect URIs.
- Add `IIdentityProviderAdminService` interface to `GroundUp.Auth.Services` (realm CRUD, client management, user provisioning). Interface only.
- Add `AuthFlowState` entity, EF configuration, repository, service interface across `GroundUp.Auth.*` (Core / Data.Abstractions / Repositories / Data.Postgres / Services). Captures FlowType, optional TenantId/InvitationId/JoinLinkId, ReturnUrl, Realm, Nonce, Status (Pending/Consumed/Expired/Failed), CreatedByIp, CreatedByUserAgent, FailureReason. 15-min default expiry, replay-protection via `ConsumedAt`.
- Add `IAuthCookieWriter` abstraction in `GroundUp.Auth.Services` (deferred from Phase 9). Interface only. Cookie writer derives the cookie's `Domain` attribute from `auth.application.default-domain` (no separate config knob).
- Add system-only setting `auth.application.default-domain` to `DefaultAuthSettingsSeeder`.
- Drop the unused `Tenant.CustomDomain` column as part of the 10a migration.
- `IHostedService`-based `AuthFlowState` cleanup sweeper. Migrates to BackgroundJobs in Phase 8 / GU-64.

Verification: Keycloak starts via Docker with realm imported, all interfaces compile, `AuthFlowState` round-trips through repository integration tests, cleanup sweeper deletes expired Pending rows.

#### Phase 10ab — Initial Setup & Secrets Foundation (GU-70)

Bridges 10a and 10b. Lands the secret-management substrate, master-key abstraction, encrypted-at-rest settings, and the WordPress-style first-run setup wizard. Required before 10b ships any sensitive configuration.

- `IMasterKeyProvider` abstraction with default implementation auto-detecting from `GroundUp:MasterKeyPath` (file) → `GroundUp:MasterKey` (env) → fail-fast at startup. 256-bit minimum, validated at boot.
- `IsSecret` column on the settings value table. `ISettingsService` transparently AES-GCM encrypts on save, decrypts on load. Encrypted form is `aes-gcm-v1:{nonce}:{ciphertext}:{tag}` for future rotation.
- API endpoints return redacted markers for secret settings — never plaintext. Plaintext access is service-layer-internal.
- `ISecretResolver` interface for Azure Key Vault / AWS Secrets Manager / HSM routing via `secretref://` prefix. No implementations in 10ab.
- `BootstrapState` entity (singleton row). Bootstrap-mode middleware redirects all non-setup routes to `/setup/*` until setup completes.
- Setup wizard with one-time bootstrap admin token: app identity → identity-provider URLs → Keycloak realm bootstrap (Path B auto-provision) → first super admin. Master admin creds never persist. Token rejected after completion.
- Configuration schema: `GroundUp:DatabaseConnection`, `GroundUp:MasterKey`/`MasterKeyPath`, `GroundUp:BootstrapAdminToken`, optional `GroundUp:Keycloak:BootstrapAdminUsername`/`Password`.

Verification: app refuses to start without a valid master key; settings round-trip through encryption verified by direct DB inspection; bootstrap-mode middleware blocks all non-setup routes; setup wizard provisions Keycloak admin client and stores credentials encrypted; bootstrap token rejected after completion.

#### Phase 10b — `GroundUp.Auth.Keycloak` provider (GU-38)

Implement both identity-provider interfaces. Self-contained — no flow logic. **Depends on 10ab** (KeycloakOptions reads sensitive values from `IsSecret` settings).

- New project `GroundUp.Auth.Keycloak` (referenced from Sample app, NOT from `GroundUp.Auth.Services`).
- Implement `IIdentityProviderService`: code-for-token exchange, token validation, userinfo. HttpClient with Polly retries.
- Implement `IIdentityProviderAdminService`: admin token via the realm-management client (provisioned during 10ab setup), realm CRUD, client CRUD, user provisioning, role extraction from `resource_access` claims.
- `KeycloakOptions` resolved entirely from settings (NOT `appsettings.json`): public-base-url, shared-realm-name, internal-base-url, admin-client-id, admin-client-secret (`IsSecret=true`).
- `KeycloakAdminLinkBuilder` service returning realm-specific deep-link URLs. Hard-coded URL pattern.
- `AddGroundUpAuthKeycloak()` extension method.
- Integration tests against a Testcontainers Keycloak instance.

Verification: code-for-token exchange works against a running Keycloak; realm CRUD round-trips; user provisioning creates a Keycloak user with correct attributes; role extraction reads `resource_access` correctly; admin link builder returns correct URLs for shared and per-tenant realms.

#### Phase 10c — Auth dispatcher, host resolver, cookie writer, basic flows (GU-67)

First user-facing slice. Three of the seven flows go end-to-end.

- `IAuthCookieWriter` implementation honoring `AuthOptions.CookieName/Secure/SameSite` and deriving the cookie's `Domain` attribute from `auth.application.default-domain` (parent-domain cookie when set, host-only when empty).
- `IHostTenantResolver` interface + `HostTenantResolver` implementation in `GroundUp.Auth.Api`. Resolution: incoming Host → if matches `*.{auth.application.default-domain}` strip subdomain and look up by `Tenant.Slug`; otherwise no tenant (general app or unknown host).
- Pre-auth tenant resolution middleware so generic controllers can know the tenant context for non-flow paths.
- `AuthUrlBuilderService` — builds Keycloak authorize URLs with `state` referencing `AuthFlowState` rows.
- `AuthFlowService` — initiates flows (creates `AuthFlowState` rows) and dispatches callbacks based on `FlowType`. Reads host-resolved tenant; if a tenant is resolved, the flow is pinned to it (no picker, no auto-select).
- Flow handlers: New Organization, Multi-Tenant Selection, Token Refresh.
- Cross-subdomain authentication state via parent-domain cookie. (Cross-registrable-domain handoff is out of scope — enterprise customers wanting their own registrable domain self-deploy a single-tenant instance.)
- `AuthController` endpoints: `GET /auth/login`, `GET /auth/register`, `GET /auth/callback`, `GET /auth/me`, `POST /auth/set-tenant`, `POST /auth/refresh`, `POST /auth/logout`.
- Sliding refresh: token reissued and cookie rewritten when past halfway point of lifetime.
- Sample app wiring (minimum: JSON endpoints; HTML/SPA decided collaboratively when this lands).

Verification: new-org flow end-to-end; multi-tenant picker shown only on the general app; standard subdomain pins flow and denies non-members; cross-subdomain cookie sharing works on the same registrable domain; replay attack on consumed `AuthFlowState` returns 410.

#### Phase 10d — Invitations, Join Links, `IUserRoleService` (GU-68)

Adds two more flows. Brings `TenantInvitation` and `TenantJoinLink` into existence (carried over from Phase 9 scope).

- Entities: `TenantInvitation` (Pending/Accepted/Expired/Revoked, expiry, role assignment, invited email, token, audit, soft-delete) and `TenantJoinLink` (token, status, optional expiry, optional max uses, role assignment, audit, soft-delete).
- EF configurations, migrations, repositories.
- `IInvitationService`, `IJoinLinkService` in `GroundUp.Auth.Services`. Membership creation and role assignment on acceptance.
- `IUserRoleService` (lean) — encapsulates role-assignment writes, enforces `auth.roles.assign-system`. Invitation/join-link acceptance routes through this service.
- `IInvitationEmailSender` interface with default `LoggingInvitationEmailSender` implementation. Future swap to Notifications module tracked by GU-64.
- Flow handlers: Invitation, Join Link.
- Controllers: `InvitationController`, `JoinLinkController`.

Verification: invitation acceptance creates membership with correct role; revoked or expired invitations/links return 410; non-SuperAdmin cannot assign SuperAdmin via any path; invitation email-match enforced.

#### Phase 10e — Enterprise flows: realm provisioning, first admin, SSO auto-join (GU-69)

Riskiest sub-phase. Closes out all 7 flows.

- `EnterpriseSignupService` orchestrates: enterprise tenant creation, Keycloak realm creation, client configuration, optional default identity provider/SSO config.
- First-admin guard: realm registration disabled after first admin signs up. Idempotent — second attempt returns 409.
- SSO auto-join: enterprise realm callback → auto-create `UserTenant` membership with the tenant's configured default role. The realm itself is the access boundary; the realm admin controls who can authenticate (via federated IdP, manual provisioning). No email-domain allowlist needed.
- Flow handlers: Enterprise First Admin, Enterprise SSO Auto-Join.
- Tenant default-role configuration (likely `Tenant.DefaultRoleId`).
- Login on enterprise tenant subdomain hits `Tenant.RealmName`, never the general realm. No picker shown.
- Sample app demo wiring (revisited collaboratively).
- Full integration test sweep across all 7 flows.

Verification: enterprise tenant provisioning creates a working Keycloak realm + client; first-admin guard blocks second attempt; first-time SSO login auto-joins the user with the tenant's default role; subsequent logins do not create duplicate memberships.

### Manual Verification (full suite, validated in 10e)
1. Standard tenant creation + first user signup via Keycloak (10c)
2. Invitation flow — invite a user, accept via Keycloak (10d)
3. Join link flow — share a link, accept via Keycloak (10d)
4. Enterprise tenant provisioning — create realm, first admin registers (10e)
5. Enterprise invitation — invite into enterprise realm (10d + 10e)
6. SSO auto-join — first-time login against enterprise realm auto-creates membership with tenant's default role (10e)
7. Multi-tenant selection — user with multiple memberships switches tenants on the general app (10c)
8. Token refresh — sliding expiration at halfway point (10c)
9. Standard tenant subdomain — login pinned to tenant, no picker even when user has multiple memberships (10c)
10. Cross-subdomain authentication — login on `acme.sampleapp.com` and access at `sampleapp.com` (parent-domain cookie travels naturally between subdomains of the configured app domain). (10c)

### Success Criteria
- [ ] Keycloak starts via Docker against pinned version with realm imported
- [ ] All 7 auth flows work end-to-end
- [ ] Login behavior matches the host-based table above (general app, standard subdomain, enterprise subdomain)
- [ ] `IHostTenantResolver` correctly resolves subdomain → `Tenant.Slug`; unknown host → no tenant
- [ ] `AuthFlowState` rows are one-shot (consumed rows reject replay with 410)
- [ ] Invitation emails go through `IInvitationEmailSender` stub (logging-only); GU-64 tracks the swap to Notifications
- [ ] Tokens issued correctly with tenant/user claims
- [ ] Cookie-based and header-based auth both work
- [ ] Cross-subdomain cookie sharing works (parent-domain cookie travels between subdomains of the configured app domain)
- [ ] `auth.roles.assign-system` enforced through `IUserRoleService` for every role-assignment path

### Commit
Each sub-phase ships its own PR. The umbrella commit message after all five merge:
```powershell
git commit -m "Phase 10: Keycloak integration and auth flows (10a–10e)"
```

---

## Phase 11: Audit Module

### Goal
Build the audit system with both event-driven and manual audit logging.

### What to Build
- AuditLog entity and DTOs
- Event handlers subscribing to EntityCreated/Updated/Deleted events
- IAuditService with LogAsync for manual entries
- [Audited] attribute for entity opt-in
- Audit data layer (same or separate DB)
- Audit query endpoints (search by entity, user, tenant, date range, correlation ID)

### Manual Verification
1. Enable auditing on TodoItem with [Audited]
2. Create/Update/Delete a todo item via Swagger
3. Query audit logs → should show all changes with old/new values and correlation ID
4. Call IAuditService.LogAsync for a custom event
5. Query audit logs → should include the manual entry

### Success Criteria
- [ ] Automatic audit logging for [Audited] entities
- [ ] Manual audit entries via IAuditService
- [ ] Audit logs include: who, what, when, old values, new values, correlation ID
- [ ] Audit log queries work
- [ ] Audit failures don't block business operations

### Commit
```powershell
git add -A
git commit -m "Phase 11: Audit module with event-driven and manual logging"
git push
```

---

## Phase 12: Object-Level Security Module

### Goal
Build the resource-level ACL system.

### What to Build
- ResourceAccess entity
- BaseSecuredRepository extending BaseTenantRepository
- IResourceAccessService (grant, revoke, check, list)
- Query filter integration
- Events published on access changes

### Manual Verification
1. Create a secured entity (e.g., Document)
2. Grant User A read access to Document 1
3. As User A → Get Document 1 → returns it
4. As User B (no grant) → Get Document 1 → not found
5. Grant User B access → now User B can see it
6. Revoke User A access → User A can no longer see it

### Success Criteria
- [ ] Object-level security filtering works
- [ ] Users can only see resources they have explicit access to
- [ ] Access grants and revocations work
- [ ] Integration tests prove security boundaries

### Commit
```powershell
git add -A
git commit -m "Phase 12: Object-level security module"
git push
```

---

## Phase 13: NuGet Packaging & Distribution

### Goal
Set up the NuGet packaging pipeline and prove a separate consuming application can reference GroundUp packages.

### What to Build

**13.1 — NuGet packaging**
- Configure .csproj files with PackageId, Version, Description, etc.
- `dotnet pack -c Release -o ./nupkgs`
- Verify .nupkg files are created for each module

**13.2 — Local NuGet feed**
- Create local NuGet source folder
- `dotnet nuget add source C:\repos\groundup\nupkgs --name GroundUpLocal`

**13.3 — MyStore consuming application**
- New repo: `mystore`
- New solution with MyStore.Api web project
- Reference GroundUp packages via PackageReference
- Create Store entity, StoreDto, StoreRepository, StoreService, StoreController
- Wire up with AddGroundUpApi(), AddGroundUpApiPostgres()
- Verify CRUD works via Swagger

**13.4 — GitHub Packages (optional)**
- GitHub Actions to pack and publish on release tags

### Success Criteria
- [ ] `dotnet pack` produces .nupkg for each module
- [ ] MyStore references GroundUp packages (not project references)
- [ ] MyStore CRUD works end-to-end using only GroundUp base classes
- [ ] MyStore developers only write entity, DTO, mapper, and optional custom service

### Commit
```powershell
git add -A
git commit -m "Phase 13: NuGet packaging and MyStore consuming app"
git push
```

---

## Phase 14: CI/CD Pipeline

### Goal
Set up automated build, test, pack, and publish via GitHub Actions.

### What to Build
- GitHub Actions workflow: build + test on every PR
- Path-based triggers (only run affected module tests)
- Separate workflow: pack + publish NuGet packages on release tags
- Branch protection rules on main

### Success Criteria
- [ ] PRs trigger automated build and test
- [ ] Only affected module tests run on each PR
- [ ] Release tags trigger NuGet package publishing
- [ ] Main branch protected (requires passing CI)

---

## Future Phases (Planned — Build When Needed)

### Phase 15: Caching Module
Build ICache abstraction with in-memory default. Migrate permission service from direct IMemoryCache to ICache. Future: GroundUp.Caching.Redis for distributed caching. Multi-tenant cache key scoping.

### Phase 16: File Storage Module
Build IFileStorageService abstraction with local disk implementation. Future: GroundUp.FileStorage.S3 for AWS, GroundUp.FileStorage.AzureBlob. File metadata tracking, multi-tenant file scoping, integration with Security module for file-level access control.

### Phase 17: Feature Flags Module
Build on top of Settings module. On/off flags, percentage-based rollout, user/tenant targeting, date-based activation. Multi-tenant aware. Performance-sensitive with caching. Feature flag lifecycle management (flags are temporary, unlike settings).

### Phase 18: Webhooks Module
Outbound webhook delivery. Webhook subscription management with event filters. Subscribes to IEventBus events. HTTP delivery with HMAC-SHA256 payload signing. Retry queue with exponential backoff. Dead letter queue. Multi-tenant subscriptions.

### Phase 19: Import Module
Bulk import framework. Parse CSV/Excel/JSON files. Column mapping with auto-detection. Row-level validation with error reporting. Batch processing for large files. Dry-run mode. Async processing via Background Jobs. Progress tracking.

### Phase 20: Workflow Module
State machine engine. State definitions, transitions, guards (conditions), actions (side effects). Workflow templates in database, configurable per tenant. Audit trail of state transitions. Integration with Notifications and Events.

### Phase 21: UI Metadata Module
Data-driven UI metadata system. DTO property metadata: display labels, input types, field order, grouping, validation messages, visibility rules. Metadata endpoint per resource. Layered metadata sources (auto-derived → config-based → attribute-based). Separate from Core — opt-in module.

### Phase 22: Localization
Add resource files (.resx) for all framework-provided strings. Replace hardcoded English strings with resource references. Locale resolution from headers/user preference/tenant config. This is a cross-cutting refactor, not a new module.

### Phase 23: Distributed Events
GroundUp.Events.RabbitMQ, GroundUp.Events.Aws.Sns, GroundUp.Events.Kafka — same IEventBus interface, distributed implementations. Consuming app swaps registration, zero code changes.

### Phase 24: Background Jobs — Hangfire
GroundUp.BackgroundJobs.Hangfire — production-grade implementation with dashboard, persistence, retries. Drop-in replacement for in-process implementation.

### Phase 25: Terraform / AWS Infrastructure
ECS/EKS deployment configurations. RDS PostgreSQL. Secrets Manager. CloudWatch. ECR. Reusable Terraform modules for consuming apps.

---

## General Rules for All Phases

**Commit frequently.** After every sub-step that compiles and works, commit. Small commits with clear messages.

**Don't skip verification.** Every phase has manual verification steps. Do them. If something doesn't work, fix it before moving on.

**Use Claude Code for building, this chat for architecture.** If you hit a design question during a phase, come back to this conversation to discuss it. Use Claude Code for the actual code generation.

**Run tests continuously.** After Phase 4, run `dotnet test` after every change. Tests should never be broken.

**Keep the Sample app current.** As you build new modules, wire them into the Sample app so you always have a working end-to-end demo.

**Use resource strings from day one.** Even in early phases, use `_localizer["ItemNotFound"]` patterns instead of hardcoded English strings, so localization can be added later without rewriting.
