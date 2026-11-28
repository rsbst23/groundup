# Requirements: Roles, Policies and Permissions Pages (GU-97)

## Context

The roles API (GU-95) and the policies and permissions API (GU-96) exist; the web app has a placeholder at `/roles`. Testers create the Member role and its policy from Swagger (API guide 15.1, steps 1 and 2).

What exists:
- **Web app:** `ListTable` with the view in the URL (`useListView`, `usePageBack`, `listSearchSchema`), `RequireTenant`, `RequirePermission`, `PermissionButton`, `ErrorPanel` (names the missing permission for `PERMISSION_REQUIRED`), `ConfirmDialog`, `RoleSelect`, `listRoles` (up to 100, for pickers).
- **API:**
  - `GET /api/roles` takes `PaginationParams` (by name unless `sortBy`, passed through to the entity) and returns `TenantRoleDto` (`id, name, description, roleType, tenantId, isSystem, isDefault`).
  - `GET /api/policies` takes `PaginationParams` (by name, `sortBy` ignored) and returns `TenantPolicyDto` (`id, name, description, permissionKeys, roleCount`).
  - Also: role get, create, update and delete; default role get, set and clear; role policies (list, attach, detach); role permissions (`allPermissions`, or each key with `grantedBy`); policy get (`PolicyDetailDto` with permissions and roles), create, update, set permissions, delete; `GET /api/permissions` (catalogue by module, `PermissionDto` with `key, name, description, module`).

## Decisions (with Rob, Jira comment 10208)

1. Layout A (mockups reviewed): `/roles`, `/roles/$roleId`, `/policies`; navigation entries Roles and Policies.
2. Every table is filterable, sortable and pageable: the roles and policies lists move to `FilterParams` with field maps. ARCHITECTURE.md states that every list endpoint takes `FilterParams`.
3. A tenant has no default role unless one is set. The Roles page shows it and sets or clears it.
4. TenantAdmin (built-in): marked, name locked, no delete, "All permissions" instead of policies.
5. Refusals inline through `ErrorPanel`.
6. Permissions shown by name with the key (localized labels: GU-99).
7. `RequirePermission`: `auth.roles.read` on the roles pages, `auth.permissions.read` on policies; write actions use `PermissionButton`.
8. A TESTING.md part for the 403 → 200 check without signing in again.
9. Three PRs: **97a** backend, **97b** roles pages, **97c** policies page and TESTING.md.

Already done: the role pickers use the roles API (GU-90). The accept-URL step of 15.1 is the Development seed (its page is GU-91).

## Scope

**In scope:** the two list endpoints on `FilterParams`, a policy count on the role list, the three pages, their API functions, tests, TESTING.md, the API guide pointer.

**Out of scope:** localized permission labels (GU-99); System tenant roles (SuperAdmin), which the API does not expose; assigning roles to members (the Members page has it); role types other than Application and Workspace.

## Requirements

### Requirement 1: List endpoints (97a)

1. `GET /api/roles` and `GET /api/policies` take `[FromQuery] FilterParams`; the service and repository list methods take `FilterParams`. Callers sending only paging get the same results as today.
2. Allowed fields (DTO names, case-insensitive; others ignored):

   | List | Sort | Filter | Search | Default order |
   |---|---|---|---|---|
   | Roles | `name`, `roleType`, `policyCount` | `roleType`, `isSystem` | name, description | name |
   | Policies | `name`, `roleCount`, `permissionCount` | none | name, description | name |

3. Every order ends with the id as a tie-breaker.
4. `TenantRoleDto` gains `policyCount` (the role's policies in the tenant), used by the list and the detail page. TenantAdmin reports 0 (it needs none).
5. The roles list keeps its System-tenant behaviour (an empty page) and its `isDefault` flag.
6. ARCHITECTURE.md: every list endpoint takes `FilterParams` and declares its fields; the roles and policies fields join the GU-98 table. The API guide notes the query.

### Requirement 2: Roles page (97b)

1. `/roles` lists the tenant's roles with `ListTable`: name (links to the role), type, policy count, description, actions. Sortable by name, type and policies; search on name and description; the view in the URL.
2. Badges: **built-in** on TenantAdmin, **default** on the default role.
3. **Default role** box above the table: "No default role. New members who join by signing in get no role." or "Default role: Member", with **Change** (a picker of the non-built-in roles) and **Clear** (with confirmation). Set needs `auth.roles.write`; the API also requires the right to grant the role.
4. **New role**: name (required, up to the API's limit), description, type (Application by default, or Workspace). Needs `auth.roles.write`.
5. **Delete** (not on TenantAdmin) with confirmation naming the role. `ROLE_IN_USE` shows its reasons. Needs `auth.roles.delete`.
6. The page is wrapped in `RequireTenant` and `RequirePermission(auth.roles.read)`.

### Requirement 3: Role detail page (97b)

1. `/roles/$roleId` shows the role's name, description and type, and a link back to the list (keeping its view).
2. **Rename**: name and description in a dialog; TenantAdmin's name field is read-only (its description can change). `ROLE_NAME_TAKEN` and `ROLE_IS_BUILT_IN` inline. Needs `auth.roles.write`.
3. **Policies**: the attached policies, each with **Detach** (confirmation). **Attach policy** opens a picker of the tenant's policies not yet attached. `PERMISSION_NOT_HELD` (the escalation guard) is shown inline. Needs `auth.roles.write`; listing policies for the picker needs `auth.permissions.read` (the button explains when it is missing).
4. **Effective permissions**: each permission by name with its key and the policies that grant it. For TenantAdmin: "All permissions" and no policy section.
5. An unknown or another tenant's role id shows "Role not found" with a link to the list.
6. After a change the role, its policies and its permissions reload; the roles list reloads too (policy count).

### Requirement 4: Policies page (97c)

1. `/policies` lists the tenant's policies with `ListTable`: name, description, permission count, role count, actions. Sortable by name, permissions and roles; search on name and description; the view in the URL.
2. **New policy** and **Edit**: a dialog with name, description and the permission catalogue grouped by module as checkboxes (name and key). Saving an edit updates the name and description and replaces the permission set (two calls; a failure of the second is shown and the dialog stays open). `POLICY_NAME_TAKEN`, unknown or System-only keys (400) and `PERMISSION_NOT_HELD` inline. Needs `auth.permissions.write`.
3. **Delete** with confirmation. `POLICY_IN_USE` shows the roles using it. Needs `auth.permissions.write`.
4. The edit dialog also lists the roles using the policy (links to them).
5. The page is wrapped in `RequireTenant` and `RequirePermission(auth.permissions.read)`.

### Requirement 5: Shared (97b, 97c)

1. Navigation: **Roles** and **Policies** entries (the Roles placeholder is replaced).
2. Query keys: `['roles', tenantId, …]` and `['policies', tenantId, …]`; a change invalidates both areas where counts or permissions can change (roles, policies, the role detail, and the session, because the signed-in user's own permissions may change).
3. A change that affects the signed-in user's permissions refreshes the session (`onSessionRefreshed`), so buttons enable or disable at once.
4. `RoleSelect` and the Members page's picker keep using `listRoles`.

### Requirement 6: Tests and docs

1. Unit tests (97a): the field maps (sorting, search, unknown fields), `policyCount`.
2. Integration tests (97a): each allowed sort and search on both endpoints against Postgres, an unknown field ignored, tenant isolation, paging only = today's order.
3. Web tests (97b, 97c): the API functions' requests and mappings, the permission grouping and checkbox state helpers, the "attachable policies" helper, the routes' search schemas.
4. TESTING.md (97c): a part for roles and policies from the web app (the API guide's 15.1 steps 1 and 2), the TenantAdmin limits, the refusals, and the 403 → 200 check with Carol in a second browser. The API guide's 15.1 points to it.
