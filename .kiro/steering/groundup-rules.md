# GroundUp Framework — Project Rules

This is a modular, enterprise-grade foundational framework for .NET 8+ distributed as NuGet packages. It is NOT an application — it is building blocks that applications consume. Full architecture details are in #[[file:CLAUDE.md]].

## Architecture Principles

- Framework, not application. Never assume GroundUp is the final product.
- SDK-first: all authorization and business logic lives in the service layer, never in controllers. Controllers are thin HTTP adapters.
- Strict layered architecture: API → Services → Repositories → Data. Never skip layers.
- Multi-tenancy is foundational. Tenant isolation is enforced at the repository level automatically.
- Every module is independently optional. The core API module works completely without authentication or any other module.

## Project Dependency Rules

```
API → Services → Repositories → Data.Abstractions ← Data.Postgres
All layers → Core (DTOs, entities, interfaces, enums)
Events → Core only
```

- API projects ONLY depend on their Services project and Core
- Services depend on Data.Abstractions (interfaces) and Core
- Services MAY depend on other modules' service INTERFACES for cross-module orchestration
- Repositories depend on Data.Abstractions and Core
- Data.Postgres depends on Repositories, Data.Abstractions, and Core
- Core has ZERO dependencies on other GroundUp projects
- Cross-module communication goes through service interfaces — never access another module's repositories directly

## Layer Rules

### Controllers (GroundUp.Api)
- ZERO business logic, ZERO security checks
- Convert OperationResult<T> to ActionResult
- NO [Authorize] attribute — security is enforced at the service layer
- No direct repository access — services only
- Route pattern: [Route("api/[controller]")]

### Services (GroundUp.Api.Services)
- This is the business logic AND security boundary
- All public methods return OperationResult<T>
- Validation via FluentValidation before calling repositories
- Publish domain events via IEventBus after successful operations
- [RequiresPermission] attributes go on service INTERFACE methods
- Never access HttpContext directly — use ICurrentUser and ITenantContext
- Must work identically whether called from a controller (API) or directly (SDK)

### Repositories (GroundUp.Api.Repositories)
- All public methods return OperationResult<T> — never throw for business logic
- Use queryShaper hooks for customization in derived repositories
- Always use AsNoTracking() for read-only queries
- Tenant filtering is automatic in BaseTenantRepository
- Mapperly mappers for entity ↔ DTO conversion

### Data (GroundUp.Api.Data.Postgres)
- Entity configurations use Fluent API (IEntityTypeConfiguration<T>), never data annotations for schema
- SaveChanges interceptor handles IAuditable fields and ISoftDeletable interception
- Global query filters for ISoftDeletable entities
- UUID v7 default value generation
- Provider-specific error detection (Postgres error codes) lives here only

## Coding Conventions

- Target net8.0, nullable reference types enabled everywhere
- Async all the way down — no .Result, .Wait(), or Task.Run() wrapping async code
- Use sealed on classes not designed for inheritance
- Use records for DTOs, value objects, and events
- File-scoped namespaces
- One class/interface per file
- All user-facing strings use resource strings for localization

### Naming
- Interfaces: I prefix (IUserRepository)
- DTOs: Dto suffix (RoleDto)
- Entities: no suffix (Role)
- Services: Service suffix (RoleService)
- Repositories: Repository suffix (RoleRepository)
- Events: past tense (EntityCreated, OrderPlaced)
- Extension classes: {Module}ServiceCollectionExtensions
- Seeders: Seeder suffix (DefaultRoleSeeder)

### Error Handling
- Business logic errors: return OperationResult.Fail(...) — NEVER throw exceptions
- Cross-cutting errors: use typed exception hierarchy (ForbiddenAccessException, NotFoundException, ConflictException, ValidationException, BusinessRuleException)
- All error responses include the correlation ID

### Mapping & Validation
- Mapperly (source generator) — NOT AutoMapper
- FluentValidation for all input validation, validators in .Core projects
- Validation runs in the service layer before repository calls

### Database
- EF Core with Fluent API — NOT data annotations for schema
- All entity configurations in separate EntityConfiguration classes
- Migrations managed per data project
- Database-agnostic in repositories — provider-specific code ONLY in Data.Postgres
- Connection string comes from the consuming application

### Testing
- xUnit + NSubstitute (NOT Moq)
- Test naming: MethodName_Scenario_ExpectedResult
- Testcontainers for real Postgres (NOT EF InMemory)
- WebApplicationFactory-based integration tests

### Logging
- Serilog structured logging with correlation ID in every entry
- Never log sensitive data (passwords, tokens, PII)

## Anti-Patterns (NEVER DO)

- Never use int for entity IDs — always UUID v7 (Guid)
- Never use AutoMapper — use Mapperly
- Never throw exceptions for business logic — return OperationResult.Fail
- Never access repositories from controllers — go through services
- Never put security logic in controllers — service layer is the boundary
- Never hardcode connection strings or secrets in framework code
- Never use Task.Run(() => asyncMethod()).Result — async all the way
- Never put provider-specific code in repositories or services
- Never use string matching on exception messages — use typed exceptions
- Never use Castle.DynamicProxy
- Never create ApiResponse<T> — OperationResult<T> is the ONE result type
- Never use DateTime.Now — always DateTime.UtcNow
- Never use data annotations for database schema
- Never let a module register another module's services
- Never use EF InMemory for integration tests
- Never force soft delete or audit fields on all entities — opt-in via interfaces
- Never assume authentication is present
- Never send notifications synchronously — use background jobs

## Key Patterns to Preserve

1. QueryShaper hooks: `Func<IQueryable<T>, IQueryable<T>>?` in base repositories
2. FilterParams: exact match, contains, min/max range, multi-value, search term, sorting, paging
3. Tenant hierarchy with ParentTenantId
4. User → UserTenant many-to-many with per-tenant ExternalUserId
5. Permission interceptor at service layer (not Castle.DynamicProxy)
6. Dual JWT authentication (identity provider + custom app tokens)
7. Auth flow state machine with callback-based state parameter encoding
8. Permission caching with per-user keys and invalidation
9. ExpressionHelper for dynamic LINQ expression building
10. OperationResult with static factory methods
11. ServiceCollectionExtensions pattern: AddGroundUp{Module}()

## Git Workflow

- Never commit directly to main. Always work from a feature branch.
- Branch naming: `phase-{number}/{short-description}` (e.g., `phase-1/solution-structure`, `phase-3/base-repository`)
- Commit frequently after each sub-step that compiles and works. Small commits with clear messages.
- Merge to main only when the phase is verified and complete.
- Push branches with `-u` flag to set up remote tracking.

## Jira Project

- Project key: GU
- URL: https://prosbeck.atlassian.net
- Epics map to build phases (GU-1 = Phase 0, GU-2 = Phase 1, etc.)
