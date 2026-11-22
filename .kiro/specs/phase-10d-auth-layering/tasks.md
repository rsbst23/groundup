# Tasks: Auth Layering (GU-81)

Execute one top-level task at a time. After each: build, run tests against the baselines, commit, stop for review.

Baselines: unit 1664 pass / 12 known failures (+1 flaky property test, GU-73); integration 364 pass / 48 known failures; sample 22/22.

- [x] 1. Tests that pin today's state
  - [x] 1.1 `LayerRulesTests` and `LayerRulesAllowlist` (design section 4); allowlist includes today's auth pairs (`Auth.Api` → `Auth.Data.Abstractions`, `Data.Abstractions`; `Auth.Services` → `Auth.Data.Postgres`, EF Core, `Npgsql`) and GU-82's pairs. The allowlist matched the actual violations exactly; removing one entry made the test fail with "GroundUp.Auth.Api uses GroundUp.Auth.Data.Abstractions"
  - [x] 1.2 `IdentityBootstrapServiceIntegrationTests` (own Postgres container): one ordered scenario covering the four queries before and after, first provisioning (user, membership, SuperAdmin role rows), idempotent repeat with no duplicates, and 409 on a different display name. Green against today's code. Only `CreateService` may change in task group 3
  - Results: unit 1667 / 12 known; integration 365 / 48 known; sample 22/22

- [x] 2. Controller and middleware call services only
  - [x] 2.1 `MyTenantDto`, `IMembershipService.GetMyTenantsAsync` and `IsActiveMemberAsync`
  - [x] 2.2 `IAuthSessionService.SetTenantForExternalUserAsync` (returns 404 with new `ErrorCodes.UserNotFound`, so the controller maps only that case to the old `{ error }` body; other 404s from `SetTenantAsync` keep `{ error, errorCode }`)
  - [x] 2.3 `CreateOrganizationRequestDtoValidator`, `IOrganizationProvisioningService.CreateForCurrentUserAsync` (order kept: 401 before 400)
  - [x] 2.4 Thin `AuthController` (no repository dependencies) and `HostTokenReconciliationMiddleware`; `Auth.Api` allowlist pairs removed and the architecture test passes
  - [x] 2.5 Unit tests: logic tests moved to the services (membership list and member check, external-user set-tenant, organization creation, validator); controller tests assert the unchanged JSON property names. Unit 1688 / 12 known; integration 365 / 48; sample 22/22

- [x] 3. Services do not use the database
  - [x] 3.1 `IUniqueConstraintDetector` (Data.Abstractions, takes `Exception` so the abstraction has no EF dependency) and `PostgresUniqueConstraintDetector` (Data.Postgres, over `PostgresErrorHelper`)
  - [x] 3.2 `IIdentityBootstrapRepository` (Auth.Data.Abstractions), `SuperAdminRecord` (Auth.Core), `IdentityBootstrapRepository` (Auth.Repositories), registration in `AddGroundUpAuthPostgres`
  - [x] 3.3 `IdentityBootstrapService` on the repository (decisions and log messages unchanged); dead message check removed from `NewOrganizationFlowHandler` (its collision test now returns the 409 result the repository really produces)
  - [x] 3.4 `Auth.Services` → `Auth.Data.Postgres` reference removed; `Auth.Services` allowlist pairs removed. The whole solution builds. Allowlist now holds only GU-82's three pairs
  - [x] 3.5 Unit tests (service decisions incl. missing role and concurrent conflict, Postgres detector); the integration test from 1.2 passes with only `CreateService` changed. Unit 1698 / 12 known; integration 365 / 48; sample 22/22

- [x] 4. Documentation and Jira
  - [x] 4.1 CLAUDE.md anti-patterns and steering rules name the architecture tests; ARCHITECTURE.md layer section gains the checked rules and the open GU-82 exception, and decision 6 is corrected (it claimed project references alone prevent a controller from reaching a repository)
  - [x] 4.2 Jira: GU-81 summary; GU-82 points at the allowlist
