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
Integrate with Keycloak and implement all the auth flows from the existing GroundUp implementation.

### What to Build

**10.1 — Keycloak Docker setup**
- Add Keycloak to docker-compose.yml
- Configure default realm, client, and redirect URIs

**10.2 — GroundUp.Authentication.Keycloak project**
- IIdentityProviderService implementation
- IIdentityProviderAdminService implementation
- Realm CRUD, client management, user provisioning, token exchange

**10.3 — Auth flows**
- Port AuthFlowService from existing codebase
- Port AuthUrlBuilderService, EnterpriseSignupService
- All 7 flows: New Org, Invitation, Join Link, Enterprise First Admin, Enterprise SSO Auto-Join, Multi-Tenant Selection, Token Refresh
- Update invitation flow to use Notifications module for sending invitation emails

**10.4 — Auth controllers**
- AuthController (callback, login, register, set-tenant, me)
- InvitationController, JoinLinkController, TenantController

### Manual Verification
Follow the test scenarios from the existing "Copilot New Thread Instructions.md":
1. Standard tenant creation + first user signup via Keycloak
2. Invitation flow — invite a user, accept via Keycloak
3. Enterprise tenant provisioning — create realm, first admin registers
4. Enterprise invitation — invite into enterprise realm
5. SSO auto-join — domain allowlist
6. Multi-tenant selection — user with multiple memberships switches tenants

### Success Criteria
- [ ] Keycloak starts via Docker and is reachable
- [ ] All 7 auth flows work end-to-end
- [ ] Invitation emails sent via Notifications module
- [ ] Tokens issued correctly with tenant/user claims
- [ ] Cookie-based and header-based auth both work

### Commit
```powershell
git add -A
git commit -m "Phase 10: Keycloak integration and auth flows"
git push
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
