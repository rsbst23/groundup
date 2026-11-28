# Design: Role Member Counts, Role Members, Permission Editor at Scale (GU-97d)

## 97d-1: Backend

### 1. Member counts

- `TenantRoleDto` gains `int ActiveMemberCount = 0, int PendingMemberCount = 0` after `PolicyCount` (defaults keep existing constructions compiling).
- A new `RoleCountsDto(int PolicyCount, int ActiveMemberCount, int PendingMemberCount)` replaces `IRoleRepository.CountPoliciesAsync` with `GetCountsAsync(roleId)`; `TenantRoleDto.From(role, isDefault, counts)`. `RoleService` get, update, get-default and set-default use it.
- The count expressions, shared by the list projection, the sort fields and `GetCountsAsync` (one static method per status):
  ```csharp
  r => userTenants.Count(ut => ut.TenantId == r.TenantId && ut.Status == status
         && userRoles.Any(ur => ur.UserId == ut.UserId && ur.TenantId == r.TenantId && ur.RoleId == r.Id))
  ```
  Counting memberships (unique per user and tenant) counts each person once, whatever their sources. Indexes used: `UserTenant (UserId, TenantId)` / `TenantId`, `UserRole.RoleId`.
- Field map: `activeMemberCount`, `pendingMemberCount` (sort).

### 2. Role members

- `IUserTenantRepository.GetMembersAsync(tenantId, filterParams, roleId = null)`: with a role id the base query adds `userRoles.Any(ur => ur.UserId == ut.UserId && ur.TenantId == tenantId && ur.RoleId == roleId)`. Everything else (field map, the roles query for the page) is shared with the members list.
- `IMemberManagementService.ListRoleMembersAsync(roleId, filterParams)` with `[RequiresPermission(MembersRead, RolesRead)]`: TENANT_REQUIRED without a tenant; 404 when the role is not in the current tenant (checked through `IRoleRepository.GetByIdAsync`, tenant-scoped; the service gets the repository if it does not have it); then the repository call.
- `RolesController`: `GET /api/roles/{id:guid}/members` (`[FromQuery] FilterParams`), pagination headers like the other lists. The controller takes `IMemberManagementService` too.

### 3. Paged permission catalogue

- `IPermissionRepository.ListCatalogAsync(FilterParams)` on the projected base overload: shaper `q => q.Where(p => !SystemOnly.Contains(p.Key))`; field map sort `name`, `key`, `module`, filter `module`, search `Name`, `Key`, `Description`; default order `Module` then `Key` (`DefaultOrder = q => q.OrderBy(p => p.Module).ThenBy(p => p.Key)`), tie-breaker `Id`. `ListModulesAsync()`: distinct modules of the non-System-only permissions, sorted.
- `IPolicyService.GetCatalogAsync()` becomes `ListCatalogAsync(FilterParams)` (returns `PaginatedData<PermissionDto>`) and `GetModulesAsync()`; both `[RequiresPermission(PermissionsRead)]` with the tenant check as today.
- `PermissionsController`: `GET /api/permissions` (`[FromQuery] FilterParams`, paged, headers) and `GET /api/permissions/modules`. `PermissionModuleDto` is no longer returned (removed if nothing else uses it).

### 4. Tests and docs (97d-1)

- Integration: counts (a member holding the role through two sources counted once; Pending counted apart; Inactive and other tenants left out; sort by each count); `GET /api/roles/{id}/members` (fields, status filter, search, 404 for another tenant's role, 403 `PERMISSION_REQUIRED` naming `auth.members.read` for a role reader without it); the paged catalogue (search by key and description, module filter, sort, System-only keys absent) and the modules list.
- Unit: the service checks (tenant, 404) and the counts in `TenantRoleDto.From`.
- ARCHITECTURE.md: the counts (on read, no stored counters, why), `GET /api/roles/{id}/members`, the paged catalogue and modules; the table rows. API guide 15.7 and 15.1 step 2 (the catalogue is now paged).

## 97d-2: Web

### 5. Roles table

Two columns after Policies: **Active** (sort `activeMemberCount`) and **Pending** (sort `pendingMemberCount`). `TenantRoleDto` type gains both counts.

### 6. Members section on the role page

- URL state: the role route's search is `listSearchSchema` (the roles list's view, for **‹ Roles**) plus `members: listSearchSchema` (nested; the router serializes it), so the section has its own page, sort, search and status. Changing it uses `navigate({ search: (prev) => ({ ...prev, members: … }), replace })`.
- `RoleMembersSection`: `ListTable` over `listRoleMembers(roleId, state)` (`['roles', tenantId, 'role', roleId, 'members', state]`), columns Name (sort `displayName`), Email (sort `email`), Status (sort `status`; filter Active/Pending), Other roles (chips), Actions (**Remove from role**, `PermissionButton(auth.roles.assign)`, `ConfirmDialog`). Wrapped so it shows "You need the permission auth.members.read to see the members" instead when the session lacks it (no call).
- **Add member**: `PermissionButton` (needs `auth.roles.assign`; the dialog also needs `auth.members.read`). `AddMemberDialog` uses MUI `Autocomplete` with options from `listMembers({ page: 1, q })` (debounced 300 ms with `useDebouncedCallback`, `pageSize` 25, at least one character), each option showing name and email; people already holding the role are disabled with "has the role". Confirm calls `addMemberRole`.
- After add or remove: invalidate `['roles']` (counts, the section) and `['members']`, refresh the session; a removed row leaves the section at once (`withoutPageItems`).

### 7. Policy editor page

- Routes: `routes/policies.tsx` becomes `routes/policies.index.tsx` (`/policies/`), plus `routes/policies.$policyId.tsx`. The editor's search: `listSearchSchema` (the policies list's view, for **‹ Policies**) plus `available: { page, sort, q, module }` (nested; `module` a string up to 100 characters).
- Policies table: the name links to the editor; **New policy** opens `PolicyInfoDialog` (name, description), then navigates to the new policy's editor. The old `PolicyDialog` is deleted.
- Header: name, description, **Edit** (`PolicyInfoDialog` → `updatePolicy`), the roles using it as links, **Delete** (`ConfirmDialog`; `POLICY_IN_USE` shown).
- Working set (`features/policies/workingSet.ts`, pure, tested):
  ```ts
  interface WorkingSet { saved: ReadonlySet<string>; added: ReadonlySet<string>; removed: ReadonlySet<string> }
  add(ws, key)      // a removed saved key is restored; otherwise added
  remove(ws, key)   // an added key is dropped; a saved key goes to removed
  undo(ws, key)     // takes a key out of removed
  current(ws)       // saved − removed + added
  isDirty(ws), changes(ws) → { added: string[], removed: string[] }
  ```
  Names for the right side come from a `Map<key, PermissionDto>` filled from the policy's saved permissions and every catalogue row seen.
- **Available** (left): `ListTable` over `listPermissionCatalog(state)` (`['permissions', 'catalog', state]`, `keepPreviousData`), columns Name (sort `name`), Key (sort `key`, monospace), Module (sort `module`), Actions (**Add**, or a disabled "In policy" when in `current`). The module filter is a select over `getPermissionModules()` placed in `ListTable`'s new `filters` slot (a ReactNode next to the search box; the status select stays as it is). **Add all shown** adds the page's rows not in the policy.
- **In this policy** (right): a client-side table over `current(ws)` plus the removed keys (struck through, **Undo**), sorted by name, with a search box and 25 rows per page (local state, not in the URL); added rows have a **new** chip; each row **Remove**.
- Bar: "N permissions · M unsaved changes (+a, −r)", **Save** (`setPolicyPermissions(id, sorted current)`; on success the saved set becomes current and the policy reloads), **Discard**. Leaving with unsaved changes: `useBlocker({ shouldBlockFn: () => dirty && !window.confirm('Discard the unsaved changes?'), enableBeforeUnload: () => dirty })`.
- Without `auth.permissions.write`: no Add, Remove, Save, Edit, Delete (buttons disabled with the permission named), the right side shows the saved set.
- Not found: "Policy not found" with the back link (as for roles).

### 8. Tests and docs (97d-2)

- Node tests: `listRoleMembers`, `listPermissionCatalog` (query string with `filters[module]`), `getPermissionModules`; `workingSet` (add, remove, undo, re-add a removed key, current, changes, dirty); the two routes' search schemas (nested views dropped when invalid).
- TESTING.md Part 6: 6.1 counts the columns; 6.2 uses the editor page (search a key, module filter, Add, Save, the **new** mark, Discard, the leave prompt); a new step for the members section (add Carol to a role from the role page, remove her); 6.5 removes the permission on the editor page.

## Files (estimate)

- **97d-1** (about 16): `TenantRoleDto`, `RoleCountsDto`, `IRoleRepository`, `RoleRepository`, `RoleService`, `IUserTenantRepository`, `UserTenantRepository`, `IMemberManagementService`, `MemberManagementService`, `RolesController`, `IPermissionRepository`, `PermissionRepository`, `IPolicyService`, `PolicyService`, `PermissionsController`, tests, docs.
- **97d-2** (about 16): types, `roles.ts`, `policies.ts`, `ListTable` (filters slot), `RolesPage`, `RoleDetailPage`, `RoleMembersSection`, `AddMemberDialog`, the two route changes and the new editor route, `PoliciesPage`, `PolicyEditorPage`, `PolicyInfoDialog`, `workingSet.ts`, tests, TESTING.md.
