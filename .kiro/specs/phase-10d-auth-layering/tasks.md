# Tasks: Auth Layering (GU-81)

Execute one top-level task at a time. After each: build, run tests against the baselines, commit, stop for review.

Baselines: unit 1664 pass / 12 known failures (+1 flaky property test, GU-73); integration 364 pass / 48 known failures; sample 22/22.

- [x] 1. Tests that pin today's state
  - [x] 1.1 `LayerRulesTests` and `LayerRulesAllowlist` (design section 4); allowlist includes today's auth pairs (`Auth.Api` → `Auth.Data.Abstractions`, `Data.Abstractions`; `Auth.Services` → `Auth.Data.Postgres`, EF Core, `Npgsql`) and GU-82's pairs. The allowlist matched the actual violations exactly; removing one entry made the test fail with "GroundUp.Auth.Api uses GroundUp.Auth.Data.Abstractions"
  - [x] 1.2 `IdentityBootstrapServiceIntegrationTests` (own Postgres container): one ordered scenario covering the four queries before and after, first provisioning (user, membership, SuperAdmin role rows), idempotent repeat with no duplicates, and 409 on a different display name. Green against today's code. Only `CreateService` may change in task group 3
  - Results: unit 1667 / 12 known; integration 365 / 48 known; sample 22/22

- [ ] 2. Controller and middleware call services only
  - [ ] 2.1 `MyTenantDto`, `IMembershipService.GetMyTenantsAsync` and `IsActiveMemberAsync`
  - [ ] 2.2 `IAuthSessionService.SetTenantForExternalUserAsync`
  - [ ] 2.3 `CreateOrganizationRequestDtoValidator`, `IOrganizationProvisioningService.CreateForCurrentUserAsync`
  - [ ] 2.4 Thin `AuthController` and `HostTokenReconciliationMiddleware`; remove the `Auth.Api` allowlist pairs
  - [ ] 2.5 Unit tests; controller tests assert the unchanged JSON shapes

- [ ] 3. Services do not use the database
  - [ ] 3.1 `IUniqueConstraintDetector` (Data.Abstractions) and `PostgresUniqueConstraintDetector` (Data.Postgres)
  - [ ] 3.2 `IIdentityBootstrapRepository` (Auth.Data.Abstractions), `IdentityBootstrapRepository` (Auth.Repositories), registration
  - [ ] 3.3 `IdentityBootstrapService` on the repository; remove the dead message check in `NewOrganizationFlowHandler`
  - [ ] 3.4 Remove `Auth.Services` → `Auth.Data.Postgres`; remove the `Auth.Services` allowlist pairs
  - [ ] 3.5 Unit tests; the integration test from 1.2 passes unchanged

- [ ] 4. Documentation and Jira
  - [ ] 4.1 CLAUDE.md anti-patterns and steering rules name the architecture tests; ARCHITECTURE.md layer section
  - [ ] 4.2 Jira: GU-81 summary; GU-82 points at the allowlist
