# Tasks: Policies and Permissions API (GU-96)
Execute one top-level task at a time. After each: build, run all three .NET suites, commit, stop for review.
Baselines (main after #57): unit 2074 / 0; integration 578 / 0; sample 22/22; web 125 / 0.
Requirements: `requirements.md`. Design: `design.md`. Two PRs on branch `phase-10/policies-api`: PR 1 = tasks 1 to 3, PR 2 = tasks 4 and 5.

## Gap analysis (before tasks)
- **Version race.** A request that starts loading permissions before a change commits could cache the old set under the new version. `GetUserPermissionsAsync` SHALL read the version **before** loading from the database and store the set with that version; a bump made meanwhile makes the entry stale, so the next request reloads. The service bumps **after** the change is saved.
- **Guard cost.** Checking each key through `HasPermissionAsync` repeats the SuperAdmin and TenantAdmin queries per key. `PermissionHoldGuard` uses `GetPermissionSummaryAsync` once (the same call `/auth/me` uses): `AllPermissions` passes, otherwise the cached set is checked. The exemption stays the same.
- **Existing invitations and join links.** The acceptance re-check now includes the guard, so an invitation or join link created by someone who lacks a permission of its role stops working. Today no role has policies (nothing could attach them), so no existing link is affected; ARCHITECTURE.md says so.
- **Existing GU-68 tests.** The roles they grant have no policies, so the guard passes. Tests that need the guard to refuse are new.
- **Concurrent set-permissions calls** on one policy can hit the (PolicyId, PermissionId) unique index: `ReplacePermissionsAsync` returns `409 CONFLICT`; the service passes it through ("The policy was changed at the same time. Try again.").
- **`CreatePolicyDto`'s validator is not registered** in DI; register it with the two new ones.
- **`AuthController`'s constructor changes** (adds `IPermissionService`): `AuthControllerTests` builds it directly and must be updated.
- **Permission cache tests** read the cached value as `HashSet<string>` (`PermissionServiceTests`, `PermissionServiceTenantAdminTests`); they move to `CachedPermissions`.
- **`/auth/me` tenant.** `JwtTenantResolutionMiddleware` sets the tenant context from `tid` on every path, so `/auth/me` resolves permissions in the token's tenant.
- **`auth.permissions.write`'s description** ("Assign and revoke permissions from policies") now also covers creating and deleting policies. The constant's text is updated; the seeder only inserts missing keys, so existing rows keep the old text (cosmetic).
- **A policy that already holds a system-only key** (only possible with SQL): setting its permissions without that key removes it; keeping it is refused like any system-only key. Documented, not special-cased.

## Tasks
- [x] 1. Data, DTOs, repositories (design 1, 2, 3)
  - [x] 1.1 `PermissionKeysAuth.SystemOnly`; `AuthErrorCodes` (`POLICY_NAME_TAKEN`, `POLICY_IN_USE`, `PERMISSION_NOT_HELD`); the new DTOs; `CreatePolicyDto.PermissionKeys`; validators (`CreatePolicyDto` keys, `UpdatePolicyDto`, `SetPolicyPermissionsDto`)
  - [x] 1.2 Migration `AddPolicyNameIndexAndRestrictPolicyDelete` (duplicate check, expression index, `RolePolicy.PolicyId` to `RESTRICT`; `Down`); `RolePolicyConfiguration`
  - [x] 1.3 `IPermissionRepository.ListAllAsync` / `GetByKeysAsync`; `IPolicyRepository.ListAsync` / `GetDetailAsync` / `ReplacePermissionsAsync` and the `DeleteAsync` override; `IRoleRepository.AssignPolicyAsync` / `RemovePolicyAsync` return the link, `GetPermissionGrantsAsync`
  - [x] 1.4 Tests: `PolicyIndexMigrationTests`; repository tests (list with keys and role counts, detail lists only same-tenant roles, replace adds and removes and reports both, another tenant's policy 404, FK refusal → 409, permission grants for a role, attach returns 201 then 200 with the same link); validator unit tests
  - [x] 1.5 Check: build, unit 2085 / 0, integration 600 / 0, sample 22/22. Found: with the link tracked in the same context, EF refuses a restricted delete itself (InvalidOperationException) before the database does; a request's context does not track it, so the repository catches only the database's refusal and the test clears the tracker
- [x] 2. Cache versioning and the escalation guard (design 4, 5, 8 summary)
  - [x] 2.1 `PermissionCacheVersions` (singleton), `CachedPermissions`; `PermissionService` stores and checks the version (read before loading); `GetPermissionSummaryAsync` on `IPermissionService`
  - [x] 2.2 `PermissionHoldGuard`; `RoleGrantPolicy` checks the role's keys on Assign for roles of kind Other, only on the `auth.roles.assign` path (admins skip it); a role whose permissions cannot be read is refused. `RoleGrantPolicy` builds the guard from the permission service and now takes `IRoleRepository`; `PermissionService` takes the versions as an optional last parameter (a private instance when built directly)
  - [x] 2.3 Handlers' log text; registration (singleton, scoped guard)
  - [x] 2.4 Unit tests: stale version reloads, a bump during a load leaves the entry stale, summary cases, guard cases, `RoleGrantPolicy` cases; existing cache tests moved to `CachedPermissions`
  - [x] 2.5 Check: build, unit 2108 / 0, integration 600 / 0, sample 22/22
- [x] 3. Services (design 6)
  - [x] 3.1 `IPolicyService` / `PolicyService` (catalogue, list, get, create, update, set permissions, delete; transactions, bumps, events)
  - [x] 3.2 `IRoleService` / `RoleService`: get policies, attach, detach, permissions
  - [x] 3.3 Registration (`AddAuthorized<IPolicyService, PolicyService>()`, validators); the startup check includes `IPolicyService`
  - [x] 3.4 Unit tests (`PolicyServiceTests`, `RoleServiceTests` additions, attributes)
  - [x] 3.5 Check: build, unit 2173 / 0, integration 600 / 0, sample 22/22; push; PR 1 info. Found: `BaseRepository`/`BaseTenantRepository.UpdateAsync` copy every mapped DTO field onto the entity, so an update resets `CreatedAt`/`CreatedBy` (seen on a role rename through the sample app); outside GU-96, raised with Rob
- [ ] 4. Controllers, `/auth/me`, integration tests (design 7, 8, 9)
  - [ ] 4.1 `PermissionsController`, `PoliciesController`, `RolesController` additions
  - [ ] 4.2 `AuthController.Me` async with `permissions` and `allPermissions`; `AuthControllerTests`
  - [ ] 4.3 `PoliciesApiTests`, `RolePoliciesApiTests` (design 9 integration list, including the cache and guard cases and `/auth/me`)
  - [ ] 4.4 Check: build, all three suites
- [ ] 5. Docs, full check, PR 2
  - [ ] 5.1 ARCHITECTURE.md "Policies and permissions"; the API guide (15.1 policy, the no-sign-in check); web TESTING.md note if `/auth/me` output is shown there
  - [ ] 5.2 Full check (and the web checks); push; PR 2 info
  - [ ] 5.3 Jira: GU-96 comment; Done after merge
