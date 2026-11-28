# Design: Roles API (GU-95)

Requirements: `requirements.md` in this folder.

## Overview

```
RolesController (/api/roles)            GroundUp.Auth.Api
   └─ IRoleService  [RequiresPermission] GroundUp.Auth.Services (AddAuthorized proxy)
        ├─ IRoleRepository  (+ GetUsageAsync)          tenant-filtered
        ├─ ITenantRepository (+ Get/SetDefaultRoleIdAsync)
        ├─ RoleGrantPolicy  (may the caller grant this role?)
        ├─ IUnitOfWork       (delete in one transaction)
        └─ IEventBus?        (optional)
DefaultRoleAssigner → ITenantRepository.GetDefaultRoleIdAsync (replaces the setting)
```

## 1. Data (one migration, `AddRoleNameIndexAndTenantDefaultRole`)

| Change | Why |
|---|---|
| Unique index `IX_AuthRoles_TenantId_LowerName` on (`TenantId`, `lower("Name")`) | Requirement 2: unique names ignoring case, also under races |
| `AuthTenants.DefaultRoleId uuid NULL`, FK to `AuthRoles(Id)`, `ON DELETE RESTRICT`, index | Requirement 5 |
| FK `AuthUserRoles.RoleId → AuthRoles.Id` changes from `CASCADE` to `RESTRICT` | Requirement 4.3: a grant made while a delete runs must make the delete fail, not be deleted with the role |

- The expression index is created with `migrationBuilder.Sql` (EF's `HasIndex` cannot express `lower(...)`); the configuration documents it.
- Before creating the index, the migration runs a check that raises an error naming the first duplicate (`DO $$ ... RAISE EXCEPTION 'Duplicate role name % in tenant %' ...`), so a database with duplicates stops with a clear message instead of an index error (Requirement 2.2).
- `Role ↔ Tenant` becomes a two-way relationship (`Role.TenantId` and `Tenant.DefaultRoleId`); `Tenant.DefaultRole` navigation is configured with `HasOne().WithMany().HasForeignKey(DefaultRoleId)` and no inverse collection.
- `RolePolicy` keeps `CASCADE` (a deleted unused role takes its policy links with it). Invitations and join links already `RESTRICT`.

Down: drops the index and the column, restores `CASCADE`.

## 2. Repositories

`IRoleRepository` (tenant-filtered, as today):

```csharp
Task<OperationResult<RoleUsageDto>> GetUsageAsync(Guid roleId, CancellationToken ct = default);
```
`RoleUsageDto(int Holders, int PendingInvitations, int ActiveJoinLinks, bool IsTenantDefault)`:
- `Holders`: `UserRoles` with the role id (any membership status, any source)
- `PendingInvitations`: `Invitations` with the role id and status Pending or Expired
- `ActiveJoinLinks`: `JoinLinks` with the role id and status Active
- `IsTenantDefault`: the tenant's `DefaultRoleId` equals the id

`ITenantRepository`:

```csharp
Task<OperationResult<Guid?>> GetDefaultRoleIdAsync(Guid tenantId, CancellationToken ct = default);
Task<OperationResult> SetDefaultRoleIdAsync(Guid tenantId, Guid? roleId, CancellationToken ct = default);
```
Both bypass the tenant filter (tenants are not tenant entities) and use `ExecuteUpdateAsync` for the set.

`TenantDto` is not changed: the default role is read through these two methods only, so the many `TenantDto` call sites stay as they are.

`RoleRepository.GetAllAsync` gets a query shaper that excludes roles of the System tenant (`AuthRoleNames.SystemTenantId`); `GetByIdAsync` returns 404 for them (Requirement 3.3). With the tenant filter this matters only when the current tenant is the System tenant.

Duplicate names: `BaseRepository.AddAsync`/`UpdateAsync` already turn a unique violation into `409` (`ErrorCodes.Conflict`); the service maps that 409 to `ROLE_NAME_TAKEN`. A delete that hits the new `RESTRICT` foreign key comes back as a failed `DeleteAsync` (the repository's `DbUpdateException` handling); the service maps it to `ROLE_IN_USE`.

## 3. Service

```csharp
public interface IRoleService
{
    [RequiresPermission(PermissionKeysAuth.RolesRead)]
    Task<OperationResult<PaginatedData<TenantRoleDto>>> ListAsync(PaginationParams paging, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.RolesRead)]
    Task<OperationResult<TenantRoleDto>> GetAsync(Guid roleId, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.RolesWrite)]
    Task<OperationResult<TenantRoleDto>> CreateAsync(CreateRoleDto request, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.RolesWrite)]
    Task<OperationResult<TenantRoleDto>> UpdateAsync(Guid roleId, UpdateRoleDto request, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.RolesDelete)]
    Task<OperationResult> DeleteAsync(Guid roleId, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.RolesRead)]
    Task<OperationResult<TenantRoleDto?>> GetDefaultAsync(CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.RolesWrite)]
    Task<OperationResult<TenantRoleDto>> SetDefaultAsync(Guid roleId, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.RolesWrite)]
    Task<OperationResult> ClearDefaultAsync(CancellationToken ct = default);
}
```

**`TenantRoleDto`** (new, in `GroundUp.Auth.Core.Dtos`): `RoleDto` plus `IsDefault`. The requirements put `IsDefault` on `RoleDto`; it is a separate DTO instead, because `RoleDto` is the Mapperly entity mapping used by every role read in the module, and `IsDefault` is not an entity field. The API response has the same shape the requirement describes.

`RoleService` (internal, sealed), every method:
1. Requires a current tenant (`TenantContext.TenantId != Guid.Empty`), else `403 TENANT_REQUIRED`.
2. Validates the request with the existing FluentValidation validators (create: name, description, type; update: name, description); names are trimmed first.

Rules:

| Method | Rule | Failure |
|---|---|---|
| Create | `RoleType` is `Application` or `Workspace` | `400 VALIDATION_FAILED` |
| Create / Update | name free in the tenant (the index decides) | `409 ROLE_NAME_TAKEN` |
| Update | built-in role: only the description may change (any different name, even a change of case, is refused, because code checks `TenantAdmin` by name) | `409 ROLE_IS_BUILT_IN` |
| Delete | built-in role | `409 ROLE_IS_BUILT_IN` |
| Delete | `GetUsageAsync` is all zero/false, inside the transaction; the FK backs it up | `409 ROLE_IN_USE` with `errors` such as "Held by 3 members", "Used by 1 pending invitation", "Used by 2 active join links", "The tenant's default role" |
| SetDefault | not built-in | `409 ROLE_IS_BUILT_IN` |
| SetDefault | `RoleGrantPolicy` allows the caller to assign it (as `RoleChange.Assign`, source `DefaultRole`) | the policy's own result (403 with its code) |
| Get, Update, Delete, SetDefault | the role is in the current tenant (the tenant-filtered read) | `404` |

**Delete transaction.** `IUnitOfWork.ExecuteInTransactionAsync`: read the usage, refuse if used, delete. A grant that commits between the read and the delete makes the delete hit the `RESTRICT` foreign key; the transaction then reports a failure, which the service turns into `ROLE_IN_USE` ("A member was given this role while it was being deleted. Try again."). So no grant is ever deleted with a role.

**Events** (Requirement 6), after the change is saved, through `IEventBus?` (null when no bus is registered): `EntityCreatedEvent<RoleDto>`, `EntityUpdatedEvent<RoleDto>` (also for set and clear default, with the role), `EntityDeletedEvent<RoleDto>` (`EntityId`). Tenant and user ids come from the current context, as for the other auth events.

**Registration.** `AddGroundUpAuth` adds `services.AddAuthorized<IRoleService, RoleService>()`. `SettingsAuthorizationStartupValidator`'s list gains `IRoleService`.

## 4. Controller

`RolesController : BaseController`, `[Route("api/roles")]`, thin: each action calls the service and returns `ToActionResult`. `GET /api/roles` sets the pagination headers like `MembersController`. `GET /api/roles/default` returns `OperationResult<TenantRoleDto?>` (200 with `data: null` when none). Routes: `default` is matched before `{id:guid}` (the guid constraint keeps them apart).

## 5. Default role on joining

`DefaultRoleAssigner.AssignAsync(userId, tenantId)`:
1. `ITenantRepository.GetDefaultRoleIdAsync(tenantId)`. None → nothing (debug log).
2. `IRoleRepository.FindByIdAnyTenantAsync(roleId)`; missing, or of another tenant (only possible in hand-edited data) → warning, skip.
3. `IRoleGrantWriter.GrantAsync(userId, roleId, tenantId, RoleAssignmentSource.DefaultRole)` as today.

The `ISettingsService` dependency and `SettingKey` are removed. `DefaultAuthSettingsSeeder` no longer ensures the `auth.application.default-role` definition. Existing rows are left alone; the API guide gives the SQL to delete them:
```sql
DELETE FROM "SettingValues" WHERE "SettingDefinitionId" IN (SELECT "Id" FROM "SettingDefinitions" WHERE "Key" = 'auth.application.default-role');
DELETE FROM "SettingDefinitions" WHERE "Key" = 'auth.application.default-role';
```

## 6. Error codes

`AuthErrorCodes`: `RoleNameTaken = "ROLE_NAME_TAKEN"`, `RoleInUse = "ROLE_IN_USE"`, `RoleIsBuiltIn = "ROLE_IS_BUILT_IN"`. Messages in plain English (no resource strings exist yet).

## 7. Tests

**Unit** (`RoleServiceTests`, NSubstitute repositories):
- no current tenant → `TENANT_REQUIRED` for every method
- create: `System` refused; validator failures; repository 409 → `ROLE_NAME_TAKEN`; created event
- update: a built-in role with any different name (including a change of case only) refused; built-in with the same name and a new description allowed; 409 → `ROLE_NAME_TAKEN`; updated event
- delete: built-in refused; each usage reason produces its message and they combine; unused deletes and publishes; a failed delete (FK) → `ROLE_IN_USE`
- set default: built-in refused; policy refusal passed through; success sets and publishes; clear
- the interface's attributes (reflection: each method has the expected permission)

**Unit** (`DefaultRoleAssignerTests`, rewritten): no default → no grant; default of another tenant → skipped with a warning; default → grant with source `DefaultRole`.

**Integration** (`RolesApiTests`, real database, the existing auth fixture):
- list (paged, headers, `isDefault`), get, create, rename
- tenant isolation: another tenant's role id is 404 for get, update, delete, set-default; it is not listed
- duplicate name in another case → 409; 10 concurrent creates of one name → exactly one 201
- built-in TenantAdmin: rename 409, description change 200, delete 409, set-default 409
- delete refused for: a holder, a pending invitation, an active join link, the default role (each message); allowed when unused, and its policy links are gone
- the FK: a role with a `UserRoles` row inserted directly cannot be deleted (simulates the race)
- default: set, get, clear; a member without `auth.roles.write` → 403; a role the caller may not grant → refused
- permissions: a plain member without `auth.roles.read` → 403; TenantAdmin passes; no tenant → 403 `TENANT_REQUIRED`
- simple sign-up and rejoin into the default tenant get the tenant's default role (`DefaultTenantAuthFlowFixture` sets `Tenant.DefaultRoleId` instead of the setting)

**Migration** (`RoleIndexMigrationTests`, Testcontainers): applying to a database with two roles named `Member` and `member` in one tenant fails with the message; the same names in two tenants apply fine.

## 8. Docs

- ARCHITECTURE.md, in the GU-68 section: a "Roles" part (endpoints, rules, the default role per tenant, check tenant roles by permission not by name).
- API guide: 15.1 creates Member with `POST /api/roles`; 6.1 also sets the default role with `PUT /api/roles/{id}/default` (optional); the SQL to remove the old setting.
- BUILD_PHASES.md 10e: `Tenant.DefaultRoleId` exists.

## Files

New: `IRoleService.cs`, `RoleService.cs`, `TenantRoleDto.cs`, `RoleUsageDto.cs`, `RolesController.cs`, the migration, tests (`RoleServiceTests`, `RolesApiTests`, `RoleIndexMigrationTests`). Changed: `Role`/`Tenant` entities and configurations, `UserRoleConfiguration`, `IRoleRepository`/`RoleRepository`, `ITenantRepository`/`TenantRepository`, `DefaultRoleAssigner`, `DefaultAuthSettingsSeeder`, `AuthErrorCodes`, `AuthServiceCollectionExtensions`, `SettingsAuthorizationStartupValidator`, tests that used the setting, docs. About 30 files, one PR.

Task groups:
1. Data: entities, configurations, migration (with the duplicate check), repository methods; migration test.
2. Service, controller, registration, error codes; unit tests.
3. Default role on joining (assigner, seeder, fixtures); integration tests.
4. Docs, full check, PR.
