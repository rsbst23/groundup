# Requirements: Roles API (GU-95)

## Context

A tenant cannot create or manage its roles except with SQL (the API guide's 15.1). What exists:
- the `Role` entity (`Name`, `Description`, `RoleType`, `TenantId`, `IsSystem`), `RoleDto`, `CreateRoleDto` (name, description, role type), `UpdateRoleDto` (name, description) and their validators
- `IRoleRepository` (a `BaseTenantRepository`, so reads and writes are limited to the current tenant), with `GetByNameAsync` and `FindByIdAnyTenantAsync`
- the permission keys `auth.roles.read`, `auth.roles.write`, `auth.roles.delete` (TenantAdmin passes every permission check)
- `RoleKinds` (SuperAdmin, TenantAdmin, other) and `RoleGrantPolicy` (who may grant which role) from GU-68

What is missing: a service, endpoints, and rules for names, built-in roles and deletion.

The default role for people who join through the default tenant is the `auth.application.default-role` **setting**, which holds a role **name** (System and tenant levels) and is read by `DefaultRoleAssigner`. Members' roles, invitations and join links already refer to roles by id.

Role names are also in tokens (the `role` claims) and in `[RequiresRole("...")]` checks.

## Decisions (with Rob)

1. Role names are unique per tenant, ignoring case.
2. Roles created through the API are `Application` (the default) or `Workspace`; `System` is refused.
3. Deleting a role in use is refused (`409 ROLE_IN_USE`, saying what uses it). A "remove its assignments too" option may come later.
4. **Rename freely.** The name-based default-role setting is replaced by a **per-tenant default role stored by id** (`Tenant.DefaultRoleId`), set through the roles API. Applications should check tenant-defined roles by permission, not by name.
5. The built-in TenantAdmin role cannot be renamed or deleted (its description can be edited). Roles of the System tenant (SuperAdmin) are not reachable from a tenant.
6. The service publishes created, updated and deleted events. No permission cache needs clearing: create and rename do not change any permission, and delete is only allowed when nobody holds the role.

## Scope

**In scope:** `IRoleService` and `RolesController`; the unique-name index and `Tenant.DefaultRoleId` (one migration); the default role endpoints; `DefaultRoleAssigner` reading the tenant's default role; removing the old setting; tests; docs.

**Out of scope:**
- **Policies and permissions** (which permissions a role grants): GU-96. A role created here grants nothing until GU-96.
- **Web pages:** GU-97.
- **Assigning roles to members:** exists (`/api/members/{userId}/roles/{roleId}`, GU-68).
- **"Remove assignments and delete":** later, if needed.
- **A System-wide default role for every tenant:** dropped with the name-based setting (Decision 4). Each tenant chooses its own.

## Requirements

### Requirement 1: Service and endpoints

1. `IRoleService` in `GroundUp.Auth.Services`, registered by `AddGroundUpAuth` with `AddAuthorized`, with `[RequiresPermission]` on the interface methods. The startup check that refuses unprotected services SHALL include it.
2. `RolesController` at `/api/roles` (under `/api`, so the host and token tenant must match), all for the **current tenant** (no current tenant: `403 TENANT_REQUIRED`):

| Endpoint | Permission | Result |
|---|---|---|
| `GET /api/roles` (paged, `PaginationParams`) | `auth.roles.read` | `PaginatedData<RoleDto>`, pagination headers |
| `GET /api/roles/{id}` | `auth.roles.read` | `RoleDto`; 404 for a role of another tenant |
| `POST /api/roles` with `CreateRoleDto` | `auth.roles.write` | 201 `RoleDto` |
| `PUT /api/roles/{id}` with `UpdateRoleDto` | `auth.roles.write` | `RoleDto` |
| `DELETE /api/roles/{id}` | `auth.roles.delete` | 200 |
| `GET /api/roles/default` | `auth.roles.read` | the tenant's default `RoleDto`, or `data: null` |
| `PUT /api/roles/{id}/default` | `auth.roles.write` | makes it the tenant's default role |
| `DELETE /api/roles/default` | `auth.roles.write` | no default role |

3. `RoleDto` SHALL also say whether the role is the tenant's default (`IsDefault`), so a list shows it without a second call.
4. The service works the same way when called directly (SDK), with the same rules and permission attributes.

### Requirement 2: Names

1. Names are trimmed, 1 to 200 characters (existing validators), and unique within the tenant ignoring case. A duplicate on create or rename SHALL return `409 ROLE_NAME_TAKEN`, also when two requests race (a unique index, not only a check).
2. The index SHALL be on (`TenantId`, lower(`Name`)). The migration SHALL fail clearly, not silently rename, if existing data already has duplicates (a check in the migration with a message naming the tenant and name).

### Requirement 3: Role types and built-in roles

1. Create accepts `Application` and `Workspace`; `System` SHALL be refused with `400`.
2. A built-in role (`IsSystem`, the TenantAdmin role) SHALL NOT be renamed or deleted: `409 ROLE_IS_BUILT_IN`. Its description may change.
3. Roles of the System tenant (SuperAdmin) are never returned or changed through this API, even for a SuperAdmin whose current tenant is the System tenant: the API manages application tenants' roles. (`404` for such an id.)
4. Role type cannot be changed after creation (`UpdateRoleDto` has no type).

### Requirement 4: Deleting a role

1. Delete SHALL be refused with `409 ROLE_IN_USE` while the role is:
   - held by anyone in the tenant (any membership status, any source), or
   - the role of a pending invitation (Pending or Expired, not yet revoked or accepted), or
   - the role of an active join link, or
   - the tenant's default role.
2. The error's `errors` list SHALL say which (for example "Held by 3 members", "Used by 1 pending invitation", "The tenant's default role").
3. When nothing uses it, the role and its policy links are deleted. The check and the delete happen in one transaction, so a role granted in between is not deleted from under its holder (a foreign key or a re-check inside the transaction).

### Requirement 5: The tenant's default role

1. `Tenant.DefaultRoleId` (nullable) SHALL hold the default role for that tenant. A foreign key to the role with no cascade (deleting the default role is refused by Requirement 4).
2. Setting the default SHALL require that the caller may grant that role (`RoleGrantPolicy`, as for assigning it to a member), so nobody makes a role the default that they could not hand out themselves.
3. The default role SHALL NOT be TenantAdmin or any built-in role: `409 ROLE_IS_BUILT_IN`. Making new joiners administrators is never automatic.
4. `DefaultRoleAssigner` SHALL read `Tenant.DefaultRoleId` for the tenant being joined (instead of the setting). When it is empty, nobody gets a role; when the role no longer exists (not possible through the API, but possible in data), it is logged and skipped as today.
5. The `auth.application.default-role` setting definition SHALL no longer be seeded or read. An existing definition and values in a database are left in place and ignored; the docs say how to remove them.
6. Behaviour for joiners is otherwise unchanged (GU-68): the role is granted with source `DefaultRole` on joining and rejoining the default tenant.

### Requirement 6: Events and caches

1. Create, update (including description-only), delete and default-role changes SHALL publish events through `IEventBus` (the existing `EntityCreatedEvent<RoleDto>`, `EntityUpdatedEvent<RoleDto>`, `EntityDeletedEvent<RoleDto>`), when an event bus is registered; the auth module does not register one itself (as for `TenantAccessRevoker`).
2. No permission cache is cleared (Decision 6). A renamed role's new name reaches its holders' tokens at their next token refresh or sign-in.

### Requirement 7: Tests

1. Unit tests (NSubstitute): each rule (type refused, built-in protection, default role rules, in-use reasons), events published, and the permission attributes on the interface.
2. Integration tests (real database):
   - tenant isolation: another tenant's role is 404 for get, update, delete and set-default
   - create, duplicate name in any case, a concurrent duplicate (exactly one succeeds), rename to a free name and to a taken one
   - System-tenant roles not listed or reachable
   - delete refused for each in-use reason, and allowed when unused (policy links removed)
   - default role: set, read, cleared; refused for TenantAdmin and for a role the caller may not grant; `RoleDto.IsDefault`; a simple sign-up into the default tenant gets the tenant's default role (the GU-68 tests move from the setting to `Tenant.DefaultRoleId`)
   - permissions: 403 without the permission, a TenantAdmin passes, no current tenant gives `TENANT_REQUIRED`
3. The migration on a database with existing roles (and the duplicate check).

### Requirement 8: Documentation

1. ARCHITECTURE.md: the roles endpoints and rules; the default role per tenant; "check tenant roles by permission, not by name" (names are in tokens and change at the next refresh after a rename).
2. The API guide: Part 15.1 creates the Member role with `POST /api/roles` instead of SQL; the default-role steps use `PUT /api/roles/{id}/default`; how to remove the old setting's rows.
3. BUILD_PHASES.md 10e: the tenant default role exists (GU-69 uses it for SSO auto-join).
