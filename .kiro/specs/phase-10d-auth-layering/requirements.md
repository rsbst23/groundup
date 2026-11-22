# Requirements: Auth Layering (GU-81)

## Context

The framework rules say controllers and middleware call services only, services depend on repository interfaces only, and provider-specific code lives only in `Data.Postgres` projects. The service layer is the SDK surface: an application that uses GroundUp without HTTP calls services directly, so any rule that lives in a controller, a middleware or a repository shortcut is skipped for it. GU-71 found this for leaving a tenant. A review of the auth module (backlog review, GU-81) found the remaining cases below.

`GroundUp.Auth.Api` has no direct reference to `GroundUp.Auth.Data.Abstractions`; it sees the repository interfaces through `GroundUp.Auth.Services`, so the compiler does not catch this.

## Scope

**In scope (decided):** the auth module (`GroundUp.Auth.Api`, `GroundUp.Auth.Services`), and architecture tests for every module.

**Out of scope:** `GroundUp.Services` → `GroundUp.Data.Postgres` (7 files: settings, bootstrap, setup). Moved to GU-82. The architecture tests allowlist those files by name until GU-82 removes them.

## Requirements

### Requirement 1: Controllers and middleware call services only

1. `AuthController.SetTenant` SHALL NOT resolve the user itself. For an identity-provider principal it SHALL pass the provider subject to `IAuthSessionService`, which resolves the GroundUp user (same 404 `User not found.` when there is none).
2. `AuthController.GetMyTenants` SHALL get the tenant list from `IMembershipService`. The rules for the list (active memberships only, enterprise tenants shown as `switchable: false` with `subdomain`, standard tenants with `current`) SHALL live in the service. The JSON response SHALL not change.
3. `AuthController.CreateOrganization` SHALL NOT load the user or validate the name. `IOrganizationProvisioningService` SHALL check the user exists and is active (401 when not) and SHALL validate the organization name with a FluentValidation validator in `GroundUp.Auth.Core` (required after trimming, at most 200 characters; 400 with the same messages).
4. `HostTokenReconciliationMiddleware` SHALL ask `IMembershipService` whether the user is an active member of the host tenant. Its responses (409 `TENANT_SWITCH_REQUIRED`, 403 `FORBIDDEN`, 401 `REAUTH_REQUIRED`) SHALL not change.
5. After the change `AuthController` and every class in `GroundUp.Auth.Api` SHALL have no dependency on a repository interface, a `DbContext` or `IUnitOfWork`.
6. All existing responses, status codes and error codes SHALL stay the same. Existing tests SHALL pass with only constructor and mock changes.

### Requirement 2: Services do not use the database directly

1. `IdentityBootstrapService` SHALL use repository interfaces instead of `AuthDbContext`, keeping its behavior: role lookup, idempotent retry (same email and display name → existing user, `AlreadyExisted = true`), 409 on a different display name or a unique-constraint violation, and the user + membership + SuperAdmin role written in one transaction with existence checks before each insert.
2. Postgres error detection (SQL state 23505) SHALL move out of `GroundUp.Auth.Services` into the data layer.
3. `NewOrganizationFlowHandler` SHALL NOT detect unique violations by matching exception messages (forbidden by the framework rules). `IUserRepository.AddAsync` already returns 409 instead of throwing, so the message check is dead code and SHALL be removed; the existing re-read after a failed add stays.
4. The project reference `GroundUp.Auth.Services` → `GroundUp.Auth.Data.Postgres` SHALL be removed. The sample app and tests that need `Auth.Data.Postgres` reference it themselves.

### Requirement 3: Architecture tests

1. Tests SHALL fail when:
   - a type in any `*.Api` assembly depends on a repository interface (`I*Repository`), a `DbContext`, or `IUnitOfWork`;
   - a type in any `*.Services` assembly depends on a type in a `*.Data.Postgres` assembly or on a `DbContext`;
   - a type in any `*.Repositories` or `*.Services` assembly depends on `Npgsql`.
2. The tests SHALL inspect method bodies, not only signatures (the middleware resolves a repository with `GetRequiredService<T>()`).
3. Known exceptions SHALL be listed by type name in one allowlist, each with a comment naming GU-82. A new violation, or an allowlisted type that no longer violates, SHALL fail the test (so the list shrinks as GU-82 lands).
4. The tests run with the unit tests, no database needed.

### Requirement 4: Tests and documentation

1. Unit tests for every moved operation in its new service, and controller tests reduced to HTTP mapping.
2. An integration test against real Postgres for the first-admin provisioning (today it has none: the wizard integration tests replace `IIdentityBootstrapService` with a fake): first call creates user, membership and SuperAdmin role; a repeat with the same input is idempotent; a different display name returns 409; the SuperAdmin queries (`HasSuperAdminAsync`, `GetSuperAdminUserIdAsync`, `HasSuperAdminRoleAsync`, `HasSystemTenantAsync`) return the right answers.
3. Baselines SHALL not get worse: unit 1664 / 12 known failures; integration 364 / 48 known; sample 22/22.
4. CLAUDE.md "Anti-Patterns" and the steering rules SHALL mention the architecture tests as the enforcement of the layer rules.
