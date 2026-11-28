# Design: Roles, Policies and Permissions Pages (GU-97)

## Overview

97a moves the roles and policies lists onto the GU-98 list pipeline (projected, with field maps) and adds `policyCount` to `TenantRoleDto`. 97b builds `/roles` and `/roles/$roleId`, 97c `/policies` and the TESTING.md part. All pages follow the GU-98 patterns: `ListTable`, the view in the URL, `RequireTenant` + `RequirePermission`, `PermissionButton`, `ErrorPanel`.

## 97a: Backend

### 1. Roles list

- `TenantRoleDto` gains a last positional parameter `int PolicyCount = 0` (a default, so existing constructions keep compiling). `TenantRoleDto.From(role, isDefault, policyCount = 0)`.
- New `IRoleRepository.ListAsync(FilterParams, CancellationToken)` returning `OperationResult<PaginatedData<TenantRoleDto>>`. `RoleRepository` calls the projected base overload `GetAllAsync<TenantRoleDto>(filterParams, queryShaper: null, projection, options)`, so the tenant filter is the base one. The projection computes, in the same query:
  - `IsDefault`: `Context.Set<Tenant>().Where(t => t.Id == r.TenantId).Select(t => t.DefaultRoleId).FirstOrDefault() == r.Id`
  - `PolicyCount`: `Context.Set<RolePolicy>().Count(rp => rp.RoleId == r.Id)`
- Field map: sort `name` (`r.Name`), `roleType`, `policyCount` (the same count expression); filter `roleType`, `isSystem`; search `r.Name`, `r.Description`. Default order `Name`, tie-breaker `Id`.
- `RoleService.ListAsync(FilterParams)` keeps the TENANT_REQUIRED and System-tenant checks and returns the repository page as it is (the separate default-role lookup goes away).
- `GetAsync` and `UpdateAsync` fill `PolicyCount` with a new `IRoleRepository.CountPoliciesAsync(roleId)`; create returns 0; set-default returns the role's count.

### 2. Policies list

- `IPolicyRepository.ListAsync(FilterParams)`; `PolicyRepository` runs `DbSet.AsNoTracking().Where(p => p.TenantId == tenantId).ToPagedListAsync(filterParams, projection, options)`. The projection builds `TenantPolicyDto` with the sorted keys and the role count, as today.
- Field map: sort `name`, `roleCount` (`p.RolePolicies.Count(rp => rp.Role.TenantId == tenantId)`), `permissionCount` (`p.PolicyPermissions.Count()`); no filters; search `Name`, `Description`. Default order `Name`, tie-breaker `Id`.
- `IPolicyService.ListAsync` and `PoliciesController.List` take `FilterParams`; `IRoleService.ListAsync` and `RolesController.List` too.

### 3. Docs and tests (97a)

- ARCHITECTURE.md "Lists and Permission Refusals": the rule "every list endpoint takes `FilterParams` and declares its fields with a `ListFieldMap`", and rows for roles and policies; the roles section mentions `policyCount`.
- API guide 15.7: one line for `GET /api/roles?sortBy=policyCount desc` and `GET /api/policies?searchTerm=…`.
- Unit: `RoleService.ListAsync` passes the page through and keeps its tenant checks; `TenantRoleDto.From` with a count.
- Integration (`RolesApiTests`, `PoliciesApiTests` or a new `RolePolicyListsApiTests`): each sort both ways, search, `roleType` and `isSystem` filters, an unknown field ignored, `isDefault` and `policyCount` values, paging only = by name, tenant isolation.

## 97b: Roles pages

### 4. Routes and navigation

- The list moves from `routes/roles.tsx` (placeholder) to `routes/roles.index.tsx` (`/roles/`); the detail is `routes/roles.$roleId.tsx` (`/roles/$roleId`). Both are top-level siblings (no layout route), like `invite.$token.tsx`. Both use `validateSearch: listSearchSchema`: the detail page carries the list's view so **Back to roles** returns to the same page, sort and search.
- `NavMenu`: Roles (`/roles`), Policies (`/policies`, 97c).

### 5. API functions (`src/api/roles.ts`, `src/api/policies.ts`)

Roles: `listRolePage(state)` (the table), `listRoles()` (pickers, unchanged), `getRole(id)`, `createRole(dto)`, `updateRole(id, dto)`, `deleteRole(id)`, `getDefaultRole()`, `setDefaultRole(id)`, `clearDefaultRole()`, `getRolePolicies(id)`, `attachPolicy(roleId, policyId)`, `detachPolicy(roleId, policyId)`, `getRolePermissions(id)`.

Policies (97c, `listAllPolicies` already in 97b for the attach picker): `listPolicyPage(state)`, `listAllPolicies()` (up to 100), `getPolicy(id)`, `createPolicy(dto)`, `updatePolicy(id, dto)`, `setPolicyPermissions(id, keys)`, `deletePolicy(id)`, `getPermissionCatalog()`.

Types in `types.ts`: `TenantRoleDto.policyCount`, `CreateRoleDto`, `UpdateRoleDto`, `PolicyDto`, `TenantPolicyDto`, `PolicyDetailDto`, `PermissionDto`, `PermissionModuleDto`, `RolePermissionsDto`, `RolePermissionDto`, `EntityRefDto`, `CreatePolicyDto`, `UpdatePolicyDto`. `labels.ts`: `RoleType` and `roleTypeLabel`.

### 6. Queries (`features/roles/rolesQuery.ts`)

Keys under the existing `rolesKey` (`['roles']`), so every invalidation reaches the pickers too: `['roles', tenantId]` (picker), `['roles', tenantId, 'page', state]`, `['roles', tenantId, 'role', id]`, `['roles', tenantId, 'role', id, 'policies']`, `['roles', tenantId, 'role', id, 'permissions']`, `['roles', tenantId, 'default']`. Policies: `['policies', tenantId, …]`. Catalogue: `['permissions']` (global, `staleTime: Infinity`).

`useRoleActions()`: each mutation invalidates `['roles']` and `['policies']` (counts) and calls `onSessionRefreshed` (the signed-in user's permissions may change). Delete navigates back to the list after success when on the detail page.

### 7. Roles page (`features/roles/RolesPage.tsx`)

- `RequireTenant` > `RequirePermission(auth.roles.read)` > content.
- `DefaultRoleBox`: reads `getDefaultRole`; "No default role. New members who join by signing in get no role." or "Default role: **Member**". **Change** opens a small dialog with `RoleSelect` over the non-built-in roles; **Clear** uses `ConfirmDialog`. Both `PermissionButton(auth.roles.write)`.
- `ListTable` columns: Name (a `Link` to the detail with the list's search; built-in and default chips), Type (`roleTypeLabel`, sort `roleType`), Policies (sort `policyCount`; "All" for TenantAdmin), Description, Actions (Delete, not on TenantAdmin). Search label "Search name or description". No status filter.
- `CreateRoleDialog`: name (required, trimmed, max 200, as the API's validator), description (max 1000), type select (Application, Workspace). On success it navigates to the new role's page.
- Delete: `ConfirmDialog`; `ROLE_IN_USE` shows its reasons through `ErrorPanel` (they are in `errors`).

### 8. Role detail page (`features/roles/RoleDetailPage.tsx`)

- Header: name, built-in chip, description, type; **Rename** (`EditRoleDialog`, the name read-only for TenantAdmin) and **Delete** (not for TenantAdmin).
- **Policies** (not for TenantAdmin): a small table (name, description, **Detach**). **Attach policy**: `PermissionButton(auth.roles.write)`, also disabled with the tooltip "Needs the permission auth.permissions.read" when the session cannot list policies; it opens a dialog with a select of `attachablePolicies(all, attached)` (a pure helper, sorted by name). `PERMISSION_NOT_HELD` shows the keys the user lacks.
- **Effective permissions**: a table of name, key and "Granted by" (policy names). TenantAdmin: an info line "TenantAdmin has all permissions in this organization."
- `404` from `getRole`: "Role not found." with the back link. Other errors: `ErrorPanel`.

## 97c: Policies page

### 9. Policies page (`features/policies/PoliciesPage.tsx`)

- `RequireTenant` > `RequirePermission(auth.permissions.read)`.
- `ListTable` columns: Name (opens the edit dialog), Description, Permissions (count, sort `permissionCount`), Roles (count, sort `roleCount`), Actions (Edit, Delete). Search "Search name or description".
- `PolicyDialog` (create and edit): name, description and the catalogue as checkboxes grouped by module (module heading, then name with the key below). Helpers in `permissionChoices.ts`: `toggleKey(selected, key)`, `moduleState(module, selected)` (all, some, none, for a module-level checkbox), `changedKeys(before, after)`. Edit loads `getPolicy` (permissions and roles); the roles are listed as links. Saving: create sends `{ name, description, permissionKeys }` in one call; edit calls `updatePolicy` only if the name or description changed, then `setPolicyPermissions` only if the keys changed. A failure keeps the dialog open with `ErrorPanel`; the list reloads after either call succeeds.
- Delete: `ConfirmDialog`; `POLICY_IN_USE` lists the roles (from `errors`).

### 10. TESTING.md (97c)

New **Part 6: Roles and Policies (GU-97)**:
1. Alice creates the Member role and makes it the default; TenantAdmin shows built-in, no delete, all permissions.
2. Alice creates "Member basics" with `auth.members.read`, attaches it to Member; the role page lists the permission granted by it.
3. Refusals: a duplicate role name, deleting a role in use (reasons), deleting a policy in use (roles), renaming TenantAdmin (name locked).
4. The 403 → 200 check: Carol (Member) in a private window; Alice removes `auth.members.read` from Member basics; Carol's Members page shows "You need the permission auth.members.read" after a reload, without signing in again; Alice adds it back; Carol's Members page lists the members.
5. Sorting and search on both lists.
The API guide's 15.1 gets a pointer to Part 6, and 5.1 (set up) uses Part 6 instead of Swagger.

### 11. Tests (97b, 97c)

Node tests: every new API function's method, path and body (`admin.test.ts` or `roles.test.ts`, `policies.test.ts`); `roleTypeLabel`; `attachablePolicies`; `toggleKey`, `moduleState`, `changedKeys`; the three routes use `listSearchSchema`.

## Files (estimate)

- **97a** (about 14): `TenantRoleDto`, `IRoleRepository`, `RoleRepository`, `IRoleService`, `RoleService`, `RolesController`, `IPolicyRepository`, `PolicyRepository`, `IPolicyService`, `PolicyService`, `PoliciesController`, ARCHITECTURE.md, the API guide, tests.
- **97b** (about 13): `roles.ts`, `types.ts`, `labels.ts`, `rolesQuery.ts`, `RolesPage`, `RoleDetailPage`, `DefaultRoleBox`, `CreateRoleDialog`, `EditRoleDialog`, `AttachPolicyDialog`, the two routes (and the removed placeholder), `NavMenu`, tests.
- **97c** (about 8): `policies.ts`, `policiesQuery.ts`, `PoliciesPage`, `PolicyDialog`, `permissionChoices.ts`, the route, tests, TESTING.md and the API guide.
