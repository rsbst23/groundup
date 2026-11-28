# Tasks: Roles API (GU-95)

Execute one top-level task at a time. After each: build, run all three .NET suites (and the web checks if the web app changes), commit, stop for review.

Baselines (main after #56): unit 2010 / 0; integration 539 / 0; sample 22/22; web 125 / 0.

Requirements: `requirements.md`. Design: `design.md`. One PR (branch `phase-10/roles-api`).

## Gap analysis (before tasks)

- **Nothing relies on the `UserRoles → Roles` cascade.** No code deletes roles today (searched for removals and `DeleteAsync` on roles), so changing it to `RESTRICT` breaks no path.
- **Deleting a tenant with a default role.** `AuthTenants.DefaultRoleId → AuthRoles` is `RESTRICT` and `AuthRoles.TenantId → AuthTenants` already is, so a script that deletes a tenant's roles must clear `DefaultRoleId` first. The API guide's "Resetting a Test User" SQL gets `UPDATE "AuthTenants" SET "DefaultRoleId" = NULL WHERE "Id" IN (...)` before deleting roles (task 4).
- **The System tenant's roles** (SuperAdmin) are seeded in the System tenant; the list filter must exclude them even when the current tenant is the System tenant (design 2).
- **Existing duplicate names.** The seeders create one TenantAdmin per tenant and one SuperAdmin; tests create roles with unique names. The migration's check protects real databases only.
- **The setting's tests.** `DefaultTenantAuthFlowFixture`, `DefaultRoleAssignerTests`, the flow handler tests and `DefaultAuthSettingsSeederTests` reference the setting; they move to `Tenant.DefaultRoleId` (task 3).

## Tasks

- [x] 1. Data (design 1, 2)
  - [x] 1.1 `Tenant.DefaultRoleId` (+ navigation), configurations (`TenantConfiguration`, `UserRoleConfiguration` to `Restrict`, `RoleConfiguration` notes the expression index)
  - [x] 1.2 Migration `AddRoleNameIndexAndTenantDefaultRole`: duplicate check, expression index, column and FK, FK change; `Down`
  - [x] 1.3 `RoleUsageDto`; `IRoleRepository.GetUsageAsync`; `ITenantRepository.GetDefaultRoleIdAsync` / `SetDefaultRoleIdAsync`. Moved to task 2: hiding the System tenant's roles is a service check (with the tenant filter they are visible only when the current tenant is the System tenant). Found: `TenantRepository.UpdateAsync` copies every `TenantDto` field onto the row, and `TenantDto` has no `DefaultRoleId`, so any tenant update would have cleared the default role; the update now keeps it (as it keeps `ParentTenantId`)
  - [x] 1.4 Tests: `RoleIndexMigrationTests` (duplicates in one tenant stop with the name and tenant; same names in two tenants apply; a case-only duplicate is refused by the index; a held role and the default role cannot be deleted; `Down`); `RoleUsageRepositoryTests` (unused, every usage kind counted and the non-counting statuses skipped, another tenant's role 404, default role set/read/clear, unknown tenant 404, a tenant update keeps the default role)
  - [x] 1.5 Check: build, unit 2010 / 0, integration 551 / 0, sample 22/22

- [x] 2. Service, controller, registration (design 3, 4, 6)
  - [x] 2.1 `AuthErrorCodes` (`ROLE_NAME_TAKEN`, `ROLE_IN_USE`, `ROLE_IS_BUILT_IN`); `TenantRoleDto`
  - [x] 2.2 `IRoleService` / `RoleService` (rules, delete transaction, events). The System tenant: list empty, role ids 404, create and clear-default 403. Found: `BaseTenantRepository.DeleteAsync` does not catch `DbUpdateException`, so a delete stopped by the `RESTRICT` foreign key threw; `RoleRepository.DeleteAsync` now returns 409 for it (the service maps it to `ROLE_IN_USE`). The invitation reason says "pending or expired", because expired invitations are counted
  - [x] 2.3 `RolesController`; `AddAuthorized<IRoleService, RoleService>()` and the two validators; the startup check includes it
  - [x] 2.4 Unit tests (`RoleServiceTests`: tenant required for every method, System tenant, each rule, reasons, transaction order, events, no event bus, attributes)
  - [x] 2.5 Check: build, unit 2073 / 0, integration 551 / 0, sample 22/22

- [x] 3. Default role on joining (design 5) and the API tests
  - [x] 3.1 `DefaultRoleAssigner` reads `Tenant.DefaultRoleId` (no `ISettingsService`, no tenant-context switch); the seeder no longer ensures the setting. Found: `CreateRoleDto.RoleType` defaulted to `System` (enum value 0) when omitted, which the API refuses; it now defaults to `Application`, as agreed
  - [x] 3.2 Tests that used the setting: `DefaultRoleAssignerTests` rewritten; the flow handler tests' setting stubs removed (the test double reports no default role); `DefaultAuthSettingsSeederTests` (10 definitions); `DefaultTenantAuthFlowFixture` sets `Tenant.DefaultRoleId`
  - [x] 3.3 `RolesApiTests` (design 7 integration list). Through HTTP a tenantless caller gets `403 FORBIDDEN` from the authorization proxy before the service's `TENANT_REQUIRED` (as for the members API)
  - [x] 3.4 Check: build, unit 2074 / 0, integration 578 / 0, sample 22/22

- [ ] 4. Docs, full check, PR
  - [ ] 4.1 ARCHITECTURE.md "Roles"; the API guide (15.1, 6.1, the old setting's SQL, the reset SQL clears `DefaultRoleId`); BUILD_PHASES.md 10e
  - [ ] 4.2 Full check; push; PR info
  - [ ] 4.3 Jira: GU-95 comment; Done after merge
