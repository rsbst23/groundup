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

### GroundUp.Sample.Web

A React app (`samples/GroundUp.Sample.Web`: Vite, TypeScript, MUI, TanStack Query and Router) for calling and testing the sample API from a browser. Its dev server proxies the API paths with the Host kept, so cookies, CSRF and the Keycloak redirects behave as for a deployed client. Development only; not in the solution and never packed. See its README for the conventions and TESTING.md for the manual tests.

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

Module projects follow the same table (`GroundUp.Auth.Api`, `GroundUp.Auth.Services`, `GroundUp.Auth.Repositories`, `GroundUp.Auth.Data.Postgres`). The checked rules:

| Project kind | Must not use |
|---|---|
| `*.Api` | `*.Data.Abstractions` (repository interfaces), `*.Data.Postgres`, EF Core, Npgsql |
| `*.Services` | `*.Data.Postgres`, EF Core, Npgsql |
| `*.Repositories` | `*.Data.Postgres`, Npgsql (provider-specific code such as unique-violation detection goes through `IUniqueConstraintDetector`) |
| `GroundUp.Core` | any other GroundUp project |
| `GroundUp.Events` | any GroundUp project except Core |

There are no exceptions (since GU-82). Services that need query shapes the generic `IBaseRepository` does not fit (settings resolution, bootstrap state, the setup transaction log) use dedicated repositories: interfaces in `GroundUp.Data.Abstractions` (`ISettingsRepository`, `ISettingsAdminRepository`, `IBootstrapStateRepository`, `ISetupTransactionLogRepository`, `IDatabaseMigrator`), implementations in `GroundUp.Repositories` (or the provider for the migrator), registered by the provider's `AddGroundUpPostgres`. A new provider (for example `Data.SqlServer`) registers the same interfaces and its own `IUniqueConstraintDetector`.

## Key Design Decisions

1. **Framework, not application.** GroundUp produces NuGet packages. It never runs on its own — a consuming application wires up the modules it needs.

2. **SDK-first.** All business logic and security enforcement lives in the service layer. Services work identically whether called from a REST controller or directly as an SDK. Controllers are disposable HTTP adapters.

3. **Multi-tenancy at the repository layer.** `BaseTenantRepository` automatically filters all queries by the current tenant (from `ITenantContext`). Tenant isolation is enforced transparently — consuming code doesn't need to remember to filter.

4. **OperationResult, not exceptions.** Business logic errors return `OperationResult.Fail(...)` instead of throwing exceptions. Exceptions are reserved for infrastructure/cross-cutting errors (e.g., `NotFoundException` for missing entities, mapped to HTTP status codes by middleware).

5. **Opt-in behaviors.** Audit fields (`IAuditable`), soft delete (`ISoftDeletable`), and tenant scoping (`ITenantEntity`) are interfaces that entities implement to opt in. Nothing is forced on all entities.

6. **Strict layering, enforced by tests.** Project references alone do not stop a layer from reaching too far: an API project sees repository interfaces through its reference to the services project. `LayerRulesTests` (unit tests, `tests/GroundUp.Tests.Unit/Architecture`) checks the assemblies each compiled project actually uses, method bodies included, and fails when a rule is broken. Known exceptions would be listed in `LayerRulesAllowlist`, each with the story that removes it; the list is empty since GU-82. An entry that no longer occurs also fails the test, so the list only shrinks. A new project under `src` must be given its layer in the test, or the test fails.

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
| `auth.session.token-lifetime-minutes` | 2 to 120 | 15 | How long a sign-in token is valid. Also the inactivity timeout: a user who makes no request for this long must sign in again. |
| `auth.session.absolute-lifetime-minutes` | 10 to 1440 | 480 | The longest a session can last from the user's last real sign-in, however active they are. Must be at least the token lifetime. |

- Both can be set at the **System** level and per **tenant**. A tenant value can only **shorten** the System value: a longer one is rejected when it is written, and if the System value is later lowered below an existing tenant value, the System value applies.
- A change applies from the next token issue or refresh. Existing tokens keep their expiry, but the absolute limit is checked against the current value at every refresh and tenant switch, so shortening it takes effect without waiting.
- The session is counted from the identity provider's `auth_time` (when the user really authenticated), so a silent single sign-on does not restart it. Every login request carries `max_age` with the absolute lifetime, so Keycloak asks for credentials when its own session is older. When a session has reached the limit the API returns `401` with `errorCode: REAUTH_REQUIRED`; the client then starts `POST /auth/login?forceLogin=true`.
- No token expires later than the user's last sign-in plus the absolute lifetime.
- `GET /auth/me` returns `issuedAt`, `expiresAt` and `authTime` (UTC) from the current token, so a browser client can show the session's times without reading the HttpOnly cookie.
- Only a SuperAdmin can change the System values. A TenantAdmin (or a user with `settings.manage.tenant`) can change their own current tenant's values. See **Settings authorization** below.

**Settings authorization (GU-77).** Enforced in the settings services, so it applies to the API and to SDK callers alike.

| Operation | Rule | Mechanism |
|---|---|---|
| Write or delete a value at the System level (or any level above Tenant) | SuperAdmin | `ISettingsAuthorizer`, called inside `SettingsService` |
| Write or delete a value at the Tenant level | SuperAdmin, or `settings.manage.tenant` in that tenant while it is the current tenant (TenantAdmin passes through the permission bypass) | `ISettingsAuthorizer` |
| Write or delete a value at a level below Tenant (Group, Case, User) | SuperAdmin, or the Tenant rule for the tenant an `ISettingScopeOwnerResolver` reports; with no resolver, SuperAdmin only | `ISettingsAuthorizer` |
| Create, update, delete levels, groups, definitions | SuperAdmin | `[RequiresRole]` on `ISettingsAdminService` |
| List settings, groups, definitions | `settings.read` | `[RequiresPermission]` on the interfaces |
| Read one effective value, list levels, ensure a definition | open (login, seeders and the app read these with no user) | none |

- **Why two mechanisms.** The value rules depend on the call's arguments (level and scope), which an attribute cannot see. The static rules use the framework's usual attributes and the `AddAuthorized` proxy.
- **Registration.** Applications register both services with `AddAuthorized<ISettingsService, SettingsService>()` and `AddAuthorized<ISettingsAdminService, SettingsAdminService>()`. With the auth module registered, the app refuses to start if either is missing. Without the auth module, settings are not restricted (there are no users to check).
- **First-run setup.** While setup is incomplete, the wizard's keys (`SettingsSetupAllowlist`) can be written at the System level with no user. After setup completes this no longer applies.
- **Secrets** are masked in every read for every user. There is no way to read a secret value through the API.
- **Errors.** No user: 401. Not allowed: 403. A caller without rights also gets 403 for an unknown key, so the API does not reveal which keys exist.
- Object-level security (for example, groups that see only some documents) is a separate, future design in the Security module. `ISettingsAuthorizer` and `ISettingScopeOwnerResolver` are the extension points it can plug into for lower settings levels.

**Revoking access when a membership ends (GU-71).** This is the one place that states the guarantee.

- When a user leaves a tenant, every GroundUp token they hold for that tenant and issued before the leave is rejected: the request is treated as unauthenticated (401 on protected endpoints) and the token cannot be refreshed. Tokens for the user's other tenants keep working. A token issued after the user rejoins works.
- The user's cached permissions for that tenant are evicted at the same time.
- **Guarantee:** immediate on the application instance that handled the leave. Other instances accept such a token until it expires, at most the token lifetime (default 15 minutes, maximum 120), until the caching module (Phase 15, GU-47) replaces the in-memory `ITokenRevocationStore` with a shared one. A restart forgets revocations; the same token-lifetime bound applies.
- **Production guard:** in the Production environment the app refuses to start with the in-memory store unless `GroundUp:Auth:AllowInMemoryTokenRevocationStore` is `true`. Set it only for a single-instance deployment.
- Every path that ends access calls `TenantAccessRevoker` (`GroundUp.Auth.Services.Membership`) after the change is saved: leaving a tenant (`IMembershipService`), an admin removing a member or a role (`IMemberManagementService`, `IUserRoleService`), revoking an invitation, and merging a pending user record into the signed-in user. A new path that ends access must call it too. User deactivation is not built yet.

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

**Returning to the app after sign-in (GU-87).** `POST /auth/login`, `/auth/signup`, `/auth/invitations/accept` and `/auth/join-links/join` take an optional `returnUrl` query parameter. It is stored on the flow state and never read from the callback's query string.
- **Rules** (`ReturnUrlRules`, in the services so SDK callers get the same check): a path on this site only. It must start with a single `/` (not `//` or `/\`), contain no `\`, `#` or control character, be at most 2048 characters, and not be the callback path or under it. Anything else is refused with `400 INVALID_RETURN_URL` before any flow state or cookie is created. There is no allowlist for other domains.
- **Outcome.** The callback answers `302` with a relative `Location` (the browser stays on the host it called) and adds one of these to the return URL's query string, replacing any earlier values:

| Outcome | Added |
|---|---|
| Signed in with a current tenant | `result=signed_in` (and `alreadyMember=true` after an invitation or join link used by an existing member) |
| Signed in, organizations to choose from | `result=tenant_picker` |
| Signed in, no organization yet | `result=no_tenant` |
| Any error once the flow state is found (including a state cookie mismatch, a used, expired or failed flow, and the flow handlers' codes) | `error=CODE` |
| The identity provider reported an error (`?error=access_denied`) | `error=IDP_ERROR&idpError=access_denied` |

- The redirect never carries the token or the error message; the cookie is set before it. A state cookie mismatch is still refused; the state is only read to find the return URL.
- **Without a return URL** (and when the state is unknown) the callback answers JSON as before. The one change for JSON clients: an identity-provider error now gives `400 IDP_ERROR` instead of "Missing code or state", and the flow state is marked failed.
- **Identity-provider errors** mark the flow failed only when the request carries the matching state cookie and the flow is still pending, so a state token alone cannot end someone else's sign-in. The provider's `error` is passed on only if it matches `^[a-z_]{1,64}$`; `error_description` is logged (up to 200 characters) and never returned.
- Logout is unchanged: it returns to `/`, because Keycloak needs each post-logout address registered.

## Members, Invitations and Join Links (GU-68)

**Membership status.** `UserTenant.Status` is `Active`, `Inactive` or `Pending`. A pending membership is created when someone is invited; it becomes `Active` when they accept. Only roles held through an `Active` membership count for permissions. Each role grant records its `Source` (`Manual`, `Invitation`, `JoinLink`, `DefaultRole`, `Provisioning`), so the same role can be held for more than one reason and removing one grant leaves the others.

**Role assignment rules.** Every role grant and removal goes through `IUserRoleService` (or its internal writer, which the flows use after the same checks):

| Role | Who can grant or remove it |
|---|---|
| SuperAdmin | a SuperAdmin |
| TenantAdmin | a TenantAdmin of that tenant, or a SuperAdmin |
| Any other role | a TenantAdmin of that tenant or a SuperAdmin; or a holder of `auth.roles.assign`, while the current tenant is the role's tenant, who also holds every permission the role grants (GU-96, `PERMISSION_NOT_HELD`) |

Applications can add rules with `IRoleAssignmentRule`. The last TenantAdmin of a tenant cannot be removed (`LAST_ADMIN`), and nor can the last SuperAdmin (`LAST_SUPERADMIN`). Removing a member needs the right to remove each role they hold.

**Admin endpoints** (under `/api`, so the host and token tenant must match):

| Endpoint | Permission |
|---|---|
| `GET /api/members`, `DELETE /api/members/{userId}` | `auth.members.read`, `auth.members.manage` |
| `POST`/`DELETE /api/members/{userId}/roles/{roleId}` | the role rules above |
| `POST`/`GET /api/invitations`, `POST /api/invitations/{id}/resend`, `.../revoke` | `auth.invitations.send`, plus the right to grant the invitation's role |
| `POST`/`GET /api/join-links`, `POST /api/join-links/{id}/regenerate`, `.../revoke` | `auth.joinlinks.manage`, plus the right to grant the link's role |

**Public endpoints** (no sign-in needed): `POST /auth/invitations/lookup` and `/auth/invitations/accept`, `POST /auth/join-links/lookup` and `/auth/join-links/join`. Each takes `{ token, forceLogin }`. Lookup returns whether the token is usable, the organization and role names, or a reason (`not_found`, `revoked`, `expired`, `accepted`, `used_up`). Accept and join return the identity provider URL; the sign-in comes back through `/auth/callback`.

**Tokens.** Invitation and join link tokens are random and stored only as a hash. An invitation token is only ever sent by email; the API never returns it. A join link token is returned once, on create and on regenerate. Resend and regenerate replace the token, so the old link stops working.

**Invitations.**
- Settings: `auth.invitations.lifetime-hours` (1 to 720, default 168; System and tenant levels), `auth.invitations.accept-url` (the client's accept page with `{token}` and optional `{tenantSlug}`; invitations cannot be sent while it is empty, `INVITATION_URL_NOT_CONFIGURED`) and `auth.invitations.require-verified-email` (default false).
- The invitation is accepted only by a user whose identity provider email matches the invited email (`INVITATION_EMAIL_MISMATCH`). By default the invitation link counts as proof of the email; with `require-verified-email` the provider must also have verified it (`EMAIL_NOT_VERIFIED`).
- If the invited email has no account in the identity provider, accept opens the registration page with the email filled in. If someone else is signed in, `forceLogin` makes the provider ask for credentials.
- A revoked, expired or used invitation returns `410` (`INVITATION_NO_LONGER_VALID` or `INVITATION_ALREADY_USED`). Revoking an invitation removes the membership still waiting on it. An expired invitation keeps its pending membership until it is resent or revoked.
- When the invitee signs in with an account whose record differs from the pending one, the pending record is merged into it (`User.MergedIntoUserId`) and kept for auditing.

**Join links.** Anyone with the link can join the tenant with the link's role. A link can have an expiry and a maximum number of uses; the last use is counted atomically, so concurrent joins never exceed the maximum. A revoked, expired or used-up link returns `410 JOIN_LINK_NO_LONGER_VALID`. People who already joined keep their membership when a link is revoked. An active member who uses a link is signed in and nothing changes (no use is counted, no role is added). A former or pending member becomes active and gets the link's role; a pending member keeps the invitation's roles too.

**Both flows:**
- A link opened on another tenant's host returns `403 INVITATION_WRONG_ORGANIZATION`.
- Enterprise tenants are refused (`422 ENTERPRISE_NOT_SUPPORTED`) until GU-69.
- If the person who created the invitation or link has since lost the right to grant its role, acceptance fails with `403`.
- Invitation emails go through `IInvitationEmailSender`. The default `LoggingInvitationEmailSender` sends nothing: in Development it logs the accept link (so you can test without email), elsewhere it logs a warning without the link. Register a real sender until the Notifications module provides one (GU-64).
- `AddGroundUpAuth` registers `IMemberManagementService`, `IInvitationService` and `IJoinLinkService` with `AddAuthorized`. If an application replaces one of them without `AddAuthorized`, the app refuses to start, because the permission attributes would have no effect.

## Roles (GU-95)

**Endpoints** (under `/api`, current tenant only; every role id of another tenant is `404`):

| Endpoint | Permission |
|---|---|
| `GET /api/roles`, `GET /api/roles/{id}` | `auth.roles.read` |
| `POST /api/roles` (`{ name, description, roleType }`), `PUT /api/roles/{id}` (`{ name, description }`) | `auth.roles.write` |
| `DELETE /api/roles/{id}` | `auth.roles.delete` |
| `GET /api/roles/default` | `auth.roles.read` |
| `PUT /api/roles/{id}/default`, `DELETE /api/roles/default` | `auth.roles.write`, plus the right to grant the role (set only) |

Each role in a response has `isDefault`. The list is ordered by name and has the usual pagination headers.

**Rules:**
- Names are trimmed and unique within the tenant ignoring case (a unique index on `lower("Name")`, so two racing requests cannot both win): `409 ROLE_NAME_TAKEN`.
- `roleType` is `Application` (the default) or `Workspace`. `System` is refused with `400`.
- The built-in TenantAdmin role (`IsSystem`) keeps its name exactly, not even a change of case, and cannot be deleted or made the default role: `409 ROLE_IS_BUILT_IN`. Its description can change.
- Only an unused role can be deleted: `409 ROLE_IN_USE`, with the reasons in `errors` (members hold it, pending or expired invitations or active join links grant it, or it is the default role). Revoke or reassign those first. A role's policy links go with it. A grant made while the delete runs makes the delete fail; the database refuses to delete a role that is still referenced.
- Roles of the System tenant (SuperAdmin) are not reachable through this API.
- Create, update, delete and default-role changes publish `EntityCreatedEvent`, `EntityUpdatedEvent` and `EntityDeletedEvent` of `RoleDto` when an event bus is registered.

**Default role.** Each tenant can have one default role (`Tenant.DefaultRoleId`). People who join the default tenant by signing in or signing up, and former members who rejoin, get it with source `DefaultRole`. It replaces the `auth.application.default-role` setting, which held a role name; there is no System-wide default role any more.

**Check tenant roles by permission, not by name.** Roles can be renamed, so application code should check permissions (`[RequiresPermission]`, `IPermissionService`). Only the built-in names (`TenantAdmin`, `SuperAdmin`) are fixed.

`AddGroundUpAuth` registers `IRoleService` with `AddAuthorized`, like the services above.

## Policies and Permissions (GU-96)

A role grants permissions through policies: Role → Policy → Permission. A **permission** is a key defined by the module that checks it (`auth.members.read`, `settings.read`); the catalogue is global and read-only from a tenant. A **policy** is a tenant's named set of permissions. A role can have several policies, and a policy can be used by several roles. TenantAdmin and SuperAdmin need no policies: they pass every permission check.

**Endpoints** (under `/api`, current tenant only; another tenant's policy or role is `404`):

| Endpoint | Permission |
|---|---|
| `GET /api/permissions` (grouped by module) | `auth.permissions.read` |
| `GET /api/policies`, `GET /api/policies/{id}` | `auth.permissions.read` |
| `POST /api/policies` (`{ name, description, permissionKeys? }`), `PUT /api/policies/{id}` (`{ name, description }`), `PUT /api/policies/{id}/permissions` (`{ permissionKeys }`, replaces the set), `DELETE /api/policies/{id}` | `auth.permissions.write` |
| `GET /api/roles/{id}/policies`, `GET /api/roles/{id}/permissions` | `auth.roles.read` |
| `POST /api/roles/{id}/policies/{policyId}` (201, or 200 when already attached), `DELETE /api/roles/{id}/policies/{policyId}` | `auth.roles.write` |

`GET /api/roles/{id}/permissions` lists each key with the policies that grant it; for TenantAdmin it returns `allPermissions: true`. `GET /auth/me` also returns the caller's `permissions` in the token's tenant, or `allPermissions: true` for a SuperAdmin or a TenantAdmin of that tenant. Clients use it to hide actions; the server still checks every request.

**Rules:**
- Policy names are trimmed and unique within the tenant ignoring case (`409 POLICY_NAME_TAKEN`, enforced by a unique index).
- Keys must exist in the catalogue. **System-only keys** (`auth.roles.assign-system`, `auth.tenants.write`, `auth.tenants.delete`, in `PermissionKeysAuth.SystemOnly`) are not listed and cannot be added to a tenant policy. Both give `400`.
- A policy used by a role cannot be deleted (`409 POLICY_IN_USE`, naming the roles). Detach it first. The database refuses the delete too, so a role attached while a delete runs makes it fail.
- The built-in TenantAdmin role's policies cannot be changed (`409 ROLE_IS_BUILT_IN`).

**Escalation guard.** A caller who is neither SuperAdmin nor TenantAdmin of the current tenant can only grant permissions they hold themselves, measured in the current tenant:
- adding permissions to a policy (only the keys being added are checked; keeping or removing keys is not)
- creating a policy with permissions
- attaching a policy to a role (every permission of the policy)
- granting a role to a member, creating an invitation or join link for it, or making it the default role (every permission of the role). The acceptance of an invitation or join link re-checks it for its creator.

A refusal is `403 PERMISSION_NOT_HELD` with the missing keys in `errors`. Removing permissions, detaching policies and removing roles are never refused by the guard. Grants the system makes itself (the TenantAdmin of a new organization, the default role at sign-in) are not checked.

**Cache.** Resolved permissions are cached per user and tenant (`PermissionCacheTtlMinutes`). Every change to a policy's permissions, a role's policies, or a policy delete bumps the tenant's permission version (`PermissionCacheVersions`), so every user of the tenant gets the change on their next request, without signing in again. This does not need an event bus. It holds for the instance that made the change; other instances see it when their cached sets expire, until the distributed cache (GU-47). Granting or removing a role evicts that user's entry, as before.

Changes publish `EntityCreatedEvent`/`EntityUpdatedEvent`/`EntityDeletedEvent` of `PolicyDto`, and created and deleted events of `RolePolicyDto` and `PolicyPermissionDto`, when an event bus is registered.

`AddGroundUpAuth` registers `IPolicyService` with `AddAuthorized`, like the services above.
