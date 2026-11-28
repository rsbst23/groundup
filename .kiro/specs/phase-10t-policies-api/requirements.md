# Requirements: Policies and Permissions API (GU-96)

## Context

A role grants permissions through policies: Role → `RolePolicy` → Policy → `PolicyPermission` → Permission. What exists:
- the entities and junctions. `Policy` is a tenant entity (`Name`, `Description`, `TenantId`) with no built-in flag and no unique name. `Permission` is global (`Key` unique, `Name`, `Description`, `Module`).
- `PolicyDto`, `CreatePolicyDto` (+ validator), `PermissionDto`, `RolePolicyDto`, `PolicyPermissionDto`. There is no update DTO for policies.
- `IPolicyRepository` (tenant-filtered, with `GetPermissionsForPolicyAsync`, `AssignPermissionAsync`, `RemovePermissionAsync`), `IPermissionRepository` (global), and `IRoleRepository.GetPoliciesForRoleAsync` / `AssignPolicyAsync` / `RemovePolicyAsync`. None of them publish events.
- the permission keys `auth.permissions.read` ("View permission definitions") and `auth.permissions.write` ("Assign and revoke permissions from policies"), seeded but not used by any service yet.
- `PermissionService`: SuperAdmin and the current tenant's TenantAdmin pass every check by role name, before the cache. Other users' permission keys are resolved through their roles' policies and cached per user and tenant (`permissions:{userId}:{tenantId}`) for `PermissionCacheTtlMinutes`.
- `RolePolicyChangedHandler` and `PolicyPermissionChangedHandler` only log. Nothing evicts cached permissions when a policy changes, so a change shows only after the TTL.
- No tenant has policies. TenantAdmin has none (it needs none). The System tenant's `FullAccess` policy belongs to SuperAdmin.
- Deleting a policy cascades to its role links, which silently removes its permissions from every role using it.
- `RoleGrantPolicy` (GU-68) lets a holder of `auth.roles.assign` grant any role other than SuperAdmin and TenantAdmin, whatever permissions that role grants.

## Decisions (with Rob)

1. No built-in policies in tenants, so there is nothing to protect. The TenantAdmin role's policies cannot be changed (`409 ROLE_IS_BUILT_IN`): it already passes every check.
2. Policy names are unique per tenant, ignoring case (`409 POLICY_NAME_TAKEN`). Policies can be renamed.
3. Deleting a policy that any role uses is refused (`409 POLICY_IN_USE`).
4. Permission keys, no new ones: the catalogue and policy reads `auth.permissions.read`; creating, changing and deleting policies and their permissions `auth.permissions.write`; attaching policies to roles `auth.roles.write`; reading a role's policies and permissions `auth.roles.read`.
5. **Escalation guard.** A caller who is not TenantAdmin or SuperAdmin can only add a permission they hold, and only attach a policy whose permissions they all hold. Removing is always allowed.
6. The same guard applies to **granting roles**: granting a role needs all of that role's permissions. This changes GU-68: `auth.roles.assign` alone is no longer enough.
7. **Cache.** A per-tenant permissions version is part of the cache key. Any policy, role-policy or policy-permission change bumps it, so the tenant's users reload on their next request. Same instance only; other instances see it after the TTL until GU-47.
8. The catalogue (`GET /api/permissions`) lists every key grouped by module, unpaged. **System-only keys** are hidden and cannot be added to a tenant policy: `auth.roles.assign-system`, `auth.tenants.write`, `auth.tenants.delete`. To revisit later if needed.
9. A role's permissions (`GET /api/roles/{id}/permissions`) list each key with the policies granting it. For TenantAdmin the answer is "all permissions".
10. `GET /auth/me` also returns the caller's permissions in the current tenant, so the web app can hide actions the user cannot take.

## Scope

**In scope:** a permissions catalogue endpoint; policy endpoints (CRUD and the policy's permissions); role-policy endpoints; a role's permissions; the escalation guard for policies, role policies and role grants; cache versioning; the policy name index (one migration); permissions in `/auth/me`; tests; docs.

**Out of scope:**
- **Creating, changing or deleting permissions** from a tenant: the catalogue stays with seeders (and SuperAdmin tooling later).
- **Web pages:** GU-97.
- **Cross-instance cache invalidation:** GU-47.
- **The N+1 queries in permission resolution:** a later performance story, if needed.
- **System tenant policies** (SuperAdmin's `FullAccess`): not reachable through this API.

## Requirements

### Requirement 1: Services and endpoints

1. The policy service and the role-policy methods live in `GroundUp.Auth.Services`, registered by `AddGroundUpAuth` with `AddAuthorized`, with `[RequiresPermission]` on the interface methods. The startup check that refuses unprotected services SHALL include each new interface.
2. All endpoints are under `/api` (the host and token tenant must match) and work on the **current tenant**. When the current tenant is the System tenant, lists are empty and ids are 404, as for roles.
3. The services work the same way when called directly (SDK), with the same rules and permission attributes.

| Endpoint | Permission | Result |
|---|---|---|
| `GET /api/permissions` | `auth.permissions.read` | the catalogue grouped by module, without system-only keys |
| `GET /api/policies` (paged) | `auth.permissions.read` | policies, each with its permission keys and how many roles use it; pagination headers |
| `GET /api/policies/{id}` | `auth.permissions.read` | the policy, its permissions and the roles using it (id and name) |
| `POST /api/policies` | `auth.permissions.write` | 201; name, description, and optionally its permission keys |
| `PUT /api/policies/{id}` | `auth.permissions.write` | rename or change the description |
| `PUT /api/policies/{id}/permissions` | `auth.permissions.write` | replaces the policy's permissions with the given keys |
| `DELETE /api/policies/{id}` | `auth.permissions.write` | 200; refused while in use |
| `GET /api/roles/{id}/policies` | `auth.roles.read` | the role's policies |
| `POST /api/roles/{id}/policies/{policyId}` | `auth.roles.write` | attaches; already attached is success |
| `DELETE /api/roles/{id}/policies/{policyId}` | `auth.roles.write` | detaches; not attached is 404 |
| `GET /api/roles/{id}/permissions` | `auth.roles.read` | each key with the policies granting it, or "all permissions" for TenantAdmin |

Setting a policy's permissions replaces the whole set (the add and remove of the story in one call, suited to a checkbox list).

### Requirement 2: Policies

1. Names are trimmed, 1 to 200 characters, and unique within the tenant ignoring case: `409 POLICY_NAME_TAKEN` on create or rename, also when two requests race (a unique index). The migration SHALL stop with a clear message naming the duplicate when existing data has duplicates.
2. Permission keys given to a policy SHALL exist in the catalogue and not be system-only; otherwise `400 VALIDATION_FAILED` listing the keys. Keys are matched exactly; duplicates in the request are ignored.
3. Deleting a policy used by a role SHALL return `409 POLICY_IN_USE`, naming the roles in `errors`. An unused policy is deleted with its permission links.
4. A policy of another tenant SHALL be `404` for every operation and SHALL NOT be listed.

### Requirement 3: Role policies

1. Only policies of the role's tenant can be attached (another tenant's policy: `404`).
2. The TenantAdmin role's policies SHALL NOT be changed: `409 ROLE_IS_BUILT_IN`. Its `GET .../policies` returns an empty list and `GET .../permissions` says "all permissions".
3. A role's permissions are the union of its policies' permissions, each listed with the names of the policies that grant it.

### Requirement 4: Escalation guard

The guard applies to a caller who is neither SuperAdmin nor a TenantAdmin of the current tenant. "Holds" means the caller's own permissions in the current tenant, as `PermissionService` resolves them.

1. Setting a policy's permissions (or creating a policy with permissions): every key **added** (not already in the policy) SHALL be held by the caller. Keys being kept or removed are not checked.
2. Attaching a policy to a role: every permission of the policy SHALL be held by the caller.
3. Granting a role (`RoleGrantPolicy`, change Assign): every permission of the role SHALL be held by the caller, in addition to the GU-68 rules. This covers granting a member a role, creating an invitation or join link, setting the default role, and the re-check made when an invitation or join link is accepted.
4. Removing permissions, detaching policies and removing roles are not checked by the guard.
5. A refusal SHALL return `403` with code `PERMISSION_NOT_HELD` and the keys the caller lacks in `errors`.
6. Grants the system makes on its own (provisioning the TenantAdmin, the default role at sign-in) are unchanged.

### Requirement 5: Cache

1. Every change to a policy's permissions, a role's policies, or a policy delete SHALL bump the tenant's permissions version, so on the same instance every user of that tenant gets the new permissions on their next request, without signing in again.
2. This SHALL work without an event bus (the bump is not left to event handlers).
3. The SuperAdmin and TenantAdmin bypasses are unchanged.

### Requirement 6: Events

Changes SHALL publish, when an event bus is registered: `EntityCreatedEvent` / `EntityUpdatedEvent` / `EntityDeletedEvent<PolicyDto>`, `EntityCreatedEvent` / `EntityDeletedEvent<RolePolicyDto>` and `EntityCreatedEvent` / `EntityDeletedEvent<PolicyPermissionDto>` (one per permission added or removed). Tenant and user ids come from the current context.

### Requirement 7: Permissions in `/auth/me`

1. `GET /auth/me` SHALL add `permissions` (the caller's permission keys in the token's tenant, sorted) and `allPermissions` (true for SuperAdmin, and for TenantAdmin of the token's tenant).
2. Without a tenant in the token, `permissions` is empty; `allPermissions` is true only for SuperAdmin.
3. The existing fields are unchanged.

### Requirement 8: Tests

1. Unit tests (NSubstitute): every rule (names, unknown and system-only keys, in use, TenantAdmin protection, each guard case and the admin bypass), the version bump, events, and the permission attributes on the interfaces.
2. Integration tests (real database, through HTTP):
   - the catalogue hides system-only keys
   - policy CRUD and permissions; duplicate name in another case; 10 concurrent creates of one name give exactly one 201
   - tenant isolation for policies and role policies
   - delete refused while in use, allowed when unused
   - TenantAdmin role policies refused
   - the guard: a member with `auth.permissions.write` cannot add a key they lack; a member with `auth.roles.write` cannot attach a policy granting more than they hold; a member with `auth.roles.assign` cannot grant a role granting more than they hold; TenantAdmin passes
   - **cache:** a member gains a permission when a policy changes and loses it when it is removed, without signing in again, with no event bus involved
   - `/auth/me` permissions for a member, a TenantAdmin and a tenantless session
   - existing GU-68 tests still pass, adjusted where callers granted roles with permissions they lack

### Requirement 9: Docs

1. ARCHITECTURE.md: the endpoints, the escalation rule (and the change to role grants), system-only keys, and the cache guarantee (same instance now; other instances after the TTL until GU-47).
2. The API guide: give Part 15's Member role a policy through the API, and a short check that a member gains a permission without signing in again.
