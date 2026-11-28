# Design: Policies and Permissions API (GU-96)

Requirements: `requirements.md` in this folder.

## Overview

```
PermissionsController (/api/permissions) ─┐
PoliciesController    (/api/policies)   ──┼─ IPolicyService [RequiresPermission]       GroundUp.Auth.Services
RolesController       (/api/roles/{id}/policies, /permissions)
                                          └─ IRoleService (+4 methods) [RequiresPermission]
        ├─ IPolicyRepository (+ list/detail/usage/replace)   tenant-filtered
        ├─ IRoleRepository   (attach/detach return the link; + GetPermissionGrantsAsync)
        ├─ IPermissionRepository (+ ListAllAsync, GetByKeysAsync)
        ├─ PermissionHoldGuard   (escalation: does the caller hold these keys?)
        ├─ PermissionCacheVersions (per-tenant version, bumped after each change)
        ├─ IUnitOfWork, IEventBus? (optional)
RoleGrantPolicy  → PermissionHoldGuard (granting a role needs its permissions)
PermissionService → PermissionCacheVersions (cached sets carry the version)
AuthController.Me → IPermissionService.GetPermissionSummaryAsync
```

## 1. Data (one migration, `AddPolicyNameIndexAndRestrictPolicyDelete`)

| Change | Why |
|---|---|
| Unique index `IX_AuthPolicies_TenantId_LowerName` on (`TenantId`, `lower("Name")`), with the same duplicate check as the roles migration (`RAISE EXCEPTION 'Duplicate policy name % in tenant %'`) | Requirement 2.1 |
| FK `AuthRolePolicies.PolicyId → AuthPolicies.Id` changes from `CASCADE` to `RESTRICT` | Requirement 2.3: a role attached while a delete runs makes the delete fail, instead of the link (and the role's permissions) silently going |

`AuthRolePolicies.RoleId` keeps `CASCADE` (an unused role takes its links with it). `AuthPolicyPermissions.PolicyId` keeps `CASCADE` (an unused policy takes its permission links). `Down` drops the index and restores `CASCADE`.

## 2. Core types (`GroundUp.Auth.Core`)

`PermissionKeysAuth.SystemOnly` (an `IReadOnlySet<string>`): `auth.roles.assign-system`, `auth.tenants.write`, `auth.tenants.delete`.

`AuthErrorCodes`: `PolicyNameTaken = "POLICY_NAME_TAKEN"`, `PolicyInUse = "POLICY_IN_USE"`, `PermissionNotHeld = "PERMISSION_NOT_HELD"`.

DTOs (records):

| DTO | Fields |
|---|---|
| `PermissionModuleDto` | `Module`, `List<PermissionDto> Permissions` (sorted by key) |
| `EntityRefDto` | `Id`, `Name` |
| `TenantPolicyDto` (list item) | `Id`, `Name`, `Description`, `List<string> PermissionKeys`, `int RoleCount` |
| `PolicyDetailDto` | `Id`, `Name`, `Description`, `List<PermissionDto> Permissions`, `List<EntityRefDto> Roles` |
| `CreatePolicyDto` (changed) | `Name`, `Description`, `List<string>? PermissionKeys = null` |
| `UpdatePolicyDto` (new) | `Name`, `Description` |
| `SetPolicyPermissionsDto` (new) | `List<string> PermissionKeys` |
| `RolePermissionsDto` | `RoleId`, `bool AllPermissions`, `List<RolePermissionDto> Permissions` |
| `RolePermissionDto` | `Key`, `Name`, `Module`, `List<EntityRefDto> GrantedBy` |
| `PermissionSummaryDto` | `bool AllPermissions`, `List<string> Permissions` |
| `RoleGrantDto` (repository result) | `PermissionKey`, `PolicyId`, `PolicyName` |
| `PolicyPermissionChangesDto` (repository result) | `List<PolicyPermissionDto> Added`, `List<PolicyPermissionDto> Removed` |

Validators: `CreatePolicyDtoValidator` gains "at most 200 keys, each 1 to 200 characters"; new `UpdatePolicyDtoValidator` (name, description) and `SetPolicyPermissionsDtoValidator` (list not null, same key rules).

## 3. Repositories

`IPermissionRepository` (global):
- `ListAllAsync()` → all permissions, ordered by module then key.
- `GetByKeysAsync(IReadOnlyCollection<string> keys)` → the permissions found (exact keys).

`IPolicyRepository` (tenant-filtered, as today):
- `ListAsync(PaginationParams)` → `PaginatedData<TenantPolicyDto>` ordered by name (`PermissionKeys` and `RoleCount` in the same query).
- `GetDetailAsync(Guid policyId)` → `PolicyDetailDto`, or 404. `Roles` lists the roles of the policy's tenant that use it (links to another tenant's role can exist only in hand-edited data).
- `ReplacePermissionsAsync(Guid policyId, IReadOnlyCollection<Guid> permissionIds)` → `PolicyPermissionChangesDto`: deletes the links not in the set and adds the missing ones, in one `SaveChanges`. 404 for another tenant's policy.
- `DeleteAsync` override: a foreign key refusal (a role attached in between) → `409 CONFLICT`, as in `RoleRepository`.
- `AddAsync`/`UpdateAsync` already turn the unique index violation into 409; the service maps it to `POLICY_NAME_TAKEN`.

`IRoleRepository`:
- `AssignPolicyAsync` returns `OperationResult<RolePolicyDto>`: 201 with the new link, 200 with the existing one. `RemovePolicyAsync` returns the removed link. Existing callers use only `Success`/`StatusCode`, so they keep compiling.
- `GetPermissionGrantsAsync(Guid roleId)` → `List<RoleGrantDto>` (one query over role policies → policies → permissions). **System-level by role id**: `RoleGrantPolicy` runs in sign-in callbacks; the caller checks the role's tenant first (the role DTO it holds).

## 4. Cache versioning (`PermissionCacheVersions`)

A singleton in `GroundUp.Auth.Services`, a `ConcurrentDictionary<Guid, long>`:
- `Current(tenantId)` → the version (0 when never bumped).
- `Bump(tenantId)` → increments.

`PermissionService.GetUserPermissionsAsync` caches `CachedPermissions(long Version, HashSet<string> Keys)` under the existing key `permissions:{userId}:{tenantId}`. A cached entry whose version differs from `Current(tenantId)` is reloaded. The existing per-user evictions (`UserRoleChangedHandler`, `TenantAccessRevoker`) keep working unchanged, because the key is unchanged.

`Bump` is called by the services after a change is saved (Requirement 5.2: no event bus needed):
- `PolicyService.SetPermissionsAsync`, `CreateAsync` (when it adds keys), `DeleteAsync`
- `RoleService.AttachPolicyAsync`, `DetachPolicyAsync`

The version lives in memory per instance, like the cache. On restart everything reloads anyway. Other instances still see changes after the TTL (GU-47).

`RolePolicyChangedHandler` and `PolicyPermissionChangedHandler` stay log-only; their "cache will refresh at TTL expiry" text changes to say the service bumps the tenant version.

## 5. Escalation guard (`PermissionHoldGuard`)

Internal, scoped:

```csharp
Task<OperationResult> CheckHoldsAsync(Guid callerUserId, IEnumerable<string> keys, CancellationToken ct);
```
- Empty `keys` → success.
- Otherwise each key goes through `IPermissionService.HasPermissionAsync(caller, key)`. That already gives SuperAdmin and the current tenant's TenantAdmin every key (Requirement 4's exemption), and resolves everyone else from their cached set in the **current tenant**.
- Missing keys → `403 PERMISSION_NOT_HELD`, message "You can only grant permissions you hold yourself.", `errors` = the missing keys, sorted.

Uses:

| Where | Keys checked |
|---|---|
| `PolicyService.CreateAsync` with keys | all given keys |
| `PolicyService.SetPermissionsAsync` | keys added (in the request, not already in the policy) |
| `RoleService.AttachPolicyAsync` | all keys of the policy |
| `RoleGrantPolicy.CheckAsync`, change `Assign`, role kind Other | all keys of the role (`GetPermissionGrantsAsync`), after the GU-68 rule passed |

`RoleGrantPolicy` skips the guard for SuperAdmin and TenantAdmin roles (only admins may grant them, and admins hold everything) and for `Remove`. It already requires the current tenant to be the role's tenant for the `auth.roles.assign` path, which is the one the guard affects. So "held" is always measured in the role's tenant, including the acceptance re-check (the callbacks point the context at the tenant first). Grants made by the system (`IRoleGrantWriter` for provisioning and the default role) do not go through `RoleGrantPolicy` and are unchanged.

## 6. Services

```csharp
public interface IPolicyService
{
    [RequiresPermission(PermissionKeysAuth.PermissionsRead)]
    Task<OperationResult<List<PermissionModuleDto>>> GetCatalogAsync(CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.PermissionsRead)]
    Task<OperationResult<PaginatedData<TenantPolicyDto>>> ListAsync(PaginationParams paging, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.PermissionsRead)]
    Task<OperationResult<PolicyDetailDto>> GetAsync(Guid policyId, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.PermissionsWrite)]
    Task<OperationResult<PolicyDetailDto>> CreateAsync(CreatePolicyDto request, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.PermissionsWrite)]
    Task<OperationResult<PolicyDetailDto>> UpdateAsync(Guid policyId, UpdatePolicyDto request, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.PermissionsWrite)]
    Task<OperationResult<PolicyDetailDto>> SetPermissionsAsync(Guid policyId, SetPolicyPermissionsDto request, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.PermissionsWrite)]
    Task<OperationResult> DeleteAsync(Guid policyId, CancellationToken ct = default);
}
```

`IRoleService` gains:

```csharp
[RequiresPermission(PermissionKeysAuth.RolesRead)]  Task<OperationResult<List<PolicyDto>>> GetPoliciesAsync(Guid roleId, CancellationToken ct = default);
[RequiresPermission(PermissionKeysAuth.RolesWrite)] Task<OperationResult<RolePolicyDto>> AttachPolicyAsync(Guid roleId, Guid policyId, CancellationToken ct = default);
[RequiresPermission(PermissionKeysAuth.RolesWrite)] Task<OperationResult> DetachPolicyAsync(Guid roleId, Guid policyId, CancellationToken ct = default);
[RequiresPermission(PermissionKeysAuth.RolesRead)]  Task<OperationResult<RolePermissionsDto>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default);
```

Every method: current tenant required (`403 TENANT_REQUIRED`); the System tenant gives empty lists and 404 for ids (as `RoleService`).

**`PolicyService`** (internal, sealed):
- Catalogue: `ListAllAsync`, drop `SystemOnly`, group by module (sorted).
- Keys (create, set): trimmed, distinct (ordinal). Resolved with `GetByKeysAsync`. Unknown or system-only → `400 VALIDATION_FAILED` with those keys in `errors`.
- Create: validate → resolve keys → guard (all keys) → in one transaction `AddAsync` then `ReplacePermissionsAsync` → bump if keys were added → events (`EntityCreatedEvent<PolicyDto>`, one `EntityCreatedEvent<PolicyPermissionDto>` per link) → `201 PolicyDetailDto`. A 409 from `AddAsync` → `POLICY_NAME_TAKEN`.
- Update: tenant-filtered read (404) → validate → `UpdateAsync` → 409 → `POLICY_NAME_TAKEN` → `EntityUpdatedEvent<PolicyDto>`.
- Set permissions: read the detail (404) → resolve keys → guard (added keys only) → `ReplacePermissionsAsync` → bump when anything changed → created/deleted events per link → the new detail.
- Delete: read the detail (404). If `Roles` is not empty → `409 POLICY_IN_USE` with `errors` = "Used by role 'Editor'." per role. Otherwise `DeleteAsync`; a 409 (a role attached meanwhile) → `POLICY_IN_USE` ("A role was given this policy while it was being deleted. Try again."). Then bump, `EntityDeletedEvent<PolicyDto>`.

**`RoleService`** additions:
- All four: the tenant-filtered role read (404 for another tenant's role).
- TenantAdmin (`IsSystem`): get policies → empty list; get permissions → `AllPermissions = true`, empty list; attach or detach → `409 ROLE_IS_BUILT_IN`.
- Attach: policy read through `IPolicyRepository.GetDetailAsync` (404 for another tenant's policy, which also covers `AssignPolicyAsync`'s own check) → guard (all the policy's keys) → `AssignPolicyAsync` → when 201, bump and `EntityCreatedEvent<RolePolicyDto>`.
- Detach: `RemovePolicyAsync` (404 when not attached) → bump → `EntityDeletedEvent<RolePolicyDto>`.
- Permissions: `GetPermissionGrantsAsync`, grouped by key, with key name and module from the catalogue, sorted by key.

**`RoleGrantPolicy`** gains `IRoleRepository` and `PermissionHoldGuard` (both scoped; `RoleGrantPolicy` is scoped).

Registration in `AddGroundUpAuth`: `PermissionCacheVersions` singleton; `PermissionHoldGuard` scoped; `AddAuthorized<IPolicyService, PolicyService>()`; validators for `UpdatePolicyDto`, `SetPolicyPermissionsDto` and `CreatePolicyDto` (if not registered yet); the startup check gains `IPolicyService`.

## 7. Controllers (`GroundUp.Auth.Api`)

- `PermissionsController` `[Route("api/permissions")]`: `GET`.
- `PoliciesController` `[Route("api/policies")]`: `GET` (pagination headers), `GET {id:guid}`, `POST`, `PUT {id:guid}`, `PUT {id:guid}/permissions`, `DELETE {id:guid}`.
- `RolesController` gains `GET {id:guid}/policies`, `POST {id:guid}/policies/{policyId:guid}`, `DELETE {id:guid}/policies/{policyId:guid}`, `GET {id:guid}/permissions`.

All thin: call the service, `ToActionResult`.

## 8. `/auth/me`

`IPermissionService.GetPermissionSummaryAsync(Guid userId)` → `PermissionSummaryDto`:
- SuperAdmin → `AllPermissions = true`, empty list.
- No current tenant → `false`, empty.
- TenantAdmin of the current tenant → `true`, empty.
- Otherwise → `false`, the cached set sorted.

`AuthController.Me` becomes async, takes `IPermissionService` and `ITenantContext` is already hydrated from the token's `tid`. It adds `permissions` and `allPermissions` to the existing anonymous object. A token whose `sub` is not a GroundUp user id (a pending-selection identity-provider token) gets `false` and an empty list without a lookup.

## 9. Tests

**Unit:**
- `PolicyServiceTests`: no tenant / System tenant; catalogue hides system-only keys and groups; create (validation, unknown and system-only keys, guard, 409 → name taken, events, bump only with keys); update; set permissions (only added keys guarded, nothing changed → no bump, events per link); delete (in use with role names, FK race → in use, unused deletes and bumps).
- `RoleServiceTests` additions: TenantAdmin cases; attach (other tenant's policy 404, guard, already attached → no bump or event); detach; permissions grouping.
- `PermissionHoldGuardTests`: empty, all held, missing sorted, admin passes through `HasPermissionAsync`.
- `RoleGrantPolicyTests` additions: assign needs the role's keys; admins and Remove skip it.
- `PermissionServiceTests` additions: a stale version reloads; summary for each case.
- Attribute tests for `IPolicyService` and the new `IRoleService` methods.

**Integration** (`PoliciesApiTests`, `RolePoliciesApiTests`, real database, the auth fixture):
- catalogue without system-only keys
- policy CRUD; set permissions; duplicate name in another case 409; 10 concurrent creates of one name → one 201
- tenant isolation (policies and role policies)
- delete in use (role names) and unused; the FK race, simulated as for roles: a `RolePolicy` row inserted directly for a role of **another tenant**, which `GetDetailAsync` does not list (it lists only the policy's tenant's roles) but the `RESTRICT` key still protects → `409 POLICY_IN_USE`, the policy and the row remain
- TenantAdmin attach/detach 409; its permissions say all
- guard: `auth.permissions.write` without key X cannot add X; `auth.roles.write` cannot attach a policy with keys they lack; `auth.roles.assign` cannot grant a role with keys they lack (`/api/members/{id}/roles/{roleId}` → 403 `PERMISSION_NOT_HELD`); TenantAdmin passes
- cache: a member calls an endpoint needing key X → 403; admin adds X to a policy of the member's role → the member's next call → 200; admin removes it → 403. The sample registers an event bus, but no handler evicts on policy changes, so passing shows the service's version bump does the work
- `/auth/me`: member list, TenantAdmin `allPermissions`, tenantless

**Migration** (`PolicyIndexMigrationTests`): duplicate names in one tenant stop with the message; the same names in two tenants apply; `Down`.

## 10. Docs

- ARCHITECTURE.md "Policies and permissions" (endpoints, escalation rule and the role grant change, system-only keys, cache guarantee).
- The API guide: Part 15.1 also creates a policy with `auth.members.read`, attaches it to Member; a check that Carol can then list members without signing in again.

## Files

New: `IPolicyService`, `PolicyService`, `PermissionHoldGuard`, `PermissionCacheVersions`, `CachedPermissions`, the DTOs above, two validators, `PermissionsController`, `PoliciesController`, the migration, tests. Changed: `PermissionKeysAuth`, `AuthErrorCodes`, `CreatePolicyDto` (+ validator), `IPolicyRepository`/`PolicyRepository`, `IRoleRepository`/`RoleRepository`, `IPermissionRepository`/`PermissionRepository`, `RolePolicyConfiguration`, `PermissionService`/`IPermissionService`, `RoleGrantPolicy`, `IRoleService`/`RoleService`, `RolesController`, `AuthController`, the two log-only handlers, `AuthServiceCollectionExtensions`, `SettingsAuthorizationStartupValidator`, docs. About 45 files: two PRs (data and services; controllers, `/auth/me`, integration tests and docs).

Task groups:
1. Data: migration, repository methods, DTOs, error codes, system-only keys; repository and migration tests.
2. Cache versioning, guard, `RoleGrantPolicy`; unit tests.
3. `PolicyService`, `RoleService` additions, registration; unit tests. (PR 1 ends here.)
4. Controllers, `/auth/me`; integration tests.
5. Docs, full check, PR 2.
