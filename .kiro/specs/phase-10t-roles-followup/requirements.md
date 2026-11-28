# Requirements: Role Member Counts, Role Members, Permission Editor at Scale (GU-97d)

## Context

GU-97 (PRs #65 to #67) built `/roles`, `/roles/$roleId` and `/policies`. Reviewing them, Rob asked for member counts on the roles list, a members section on the role page, and a permission editor that works with hundreds or thousands of permissions (the dialog's two-column checkbox grid is hard to read and does not scale).

What exists:
- `GET /api/roles` (field map: sort `name`, `roleType`, `policyCount`), `TenantRoleDto` with `policyCount`, counted in the list query.
- `GET /api/members` (`FilterParams`; sort email, displayName, status; search email and display name), `POST`/`DELETE /api/members/{userId}/roles/{roleId}` (the role rules: who may grant TenantAdmin, the escalation guard, the last admin).
- `GET /api/permissions`: the whole catalogue grouped by module (System-only keys left out), used only by the policy dialog.
- `GET /api/policies/{id}` (permissions and roles), `PUT /api/policies/{id}` (name, description), `PUT /api/policies/{id}/permissions` (replaces the set).
- Indexes on `UserRole.RoleId`, `UserRole.TenantId`, `UserTenant (UserId, TenantId)`.

## Decisions (with Rob, Jira comment 10209)

1. Roles list: two sortable columns, **Active** and **Pending** members, each counting a person once. Counted on read in the list query; no stored counters for now.
2. Role page: a **Members** section (sort, search, paging) with **Remove from role** and **Add member** (a member search picker).
3. Permission editor: mockup B on its own page `/policies/$policyId`: available permissions (server-side search, module filter, sorting, paging) on the left, the policy's permissions on the right, Add and Remove, unsaved changes marked, Save and Discard.
4. Two PRs: **97d-1** backend, **97d-2** web.

## Requirements

### Requirement 1: Member counts on roles (97d-1)

1. `TenantRoleDto` gains `activeMemberCount` and `pendingMemberCount`: the distinct users who hold the role in the tenant (any source) and whose membership of the tenant is Active, or Pending. Inactive memberships are not counted.
2. Both are computed in the roles list query and in the single-role reads (get, update, default), like `policyCount`.
3. The roles list's field map gains sort fields `activeMemberCount` and `pendingMemberCount`.
4. No counters are stored; ARCHITECTURE.md notes the choice (counted on read with indexes; a cache or summary table only if measured slow).

### Requirement 2: Role members (97d-1)

1. `GET /api/roles/{id}/members` takes `FilterParams` and returns a page of `TenantMemberDto` (the members list's row: name, email, status, every role with its sources) for the Active and Pending members holding the role.
2. Field map: sort `email`, `displayName`, `status`; filter `status`; search email and display name. Default order email, tie-breaker user id. Other fields ignored.
3. It needs `auth.roles.read` and `auth.members.read` (it lists members). A role of another tenant (or none) is 404; TENANT_REQUIRED without a tenant.
4. Adding and removing use the existing member role endpoints (no new write endpoint).

### Requirement 3: Paged permission catalogue (97d-1)

1. `GET /api/permissions` takes `FilterParams` and returns a page of `PermissionDto`. Field map: sort `name`, `key`, `module`; filter `module`; search name, key and description. Default order module then key, tie-breaker id. System-only keys are never listed.
2. `GET /api/permissions/modules` returns the module names (sorted) for the filter.
3. This replaces the grouped response (a breaking change; the web app is the only caller). Permissions unchanged (`auth.permissions.read`, a current tenant).

### Requirement 4: Roles list and role page (97d-2)

1. The roles table gains **Active** and **Pending** columns (sortable).
2. The role page gains a **Members** section: a `ListTable` (name, email, status, other roles; sort, search, status filter, paging) whose view is kept apart from the roles list's view in the URL (`m…` params or a separate key; the design decides).
3. **Remove from role** on each row (confirmation), through `DELETE /api/members/{userId}/roles/{roleId}`; refusals inline (`LAST_ADMIN`, the role rules). Needs `auth.roles.assign`.
4. **Add member**: a search-as-you-type picker over `GET /api/members?searchTerm=…` (debounced, up to 25 results, people already holding the role marked), then `POST /api/members/{userId}/roles/{roleId}`. Needs `auth.roles.assign` and `auth.members.read`; refusals inline (`PERMISSION_NOT_HELD`, TenantAdmin rules).
5. For TenantAdmin the section shows its members too (adding and removing follow the API's rules).
6. After a change the role (counts), its members, the members list and the session reload; a removed row leaves the table at once.

### Requirement 5: Permission editor page (97d-2)

1. `/policies/$policyId` replaces the edit dialog (the policies table's names link to it; creating a policy asks for name and description, then opens the page). The list's view is kept for **‹ Policies**.
2. Header: name and description with **Edit** (a small dialog), the roles using it (links), **Delete**.
3. **Available** (left): a table over the paged catalogue with search (debounced), a module filter, sorting by name, key, module, and paging. Each row: name, key, module, and **Add** (or "In policy" when it is in the working set). **Add all shown** adds the current page's rows.
4. **In this policy** (right): the working set (the saved permissions plus unsaved changes) with its own search and paging (client-side: it is the policy's own set), each row with **Remove**. Unsaved additions are marked **new**; unsaved removals stay listed, struck through, with **Undo**.
5. **Save** sends the whole set (`PUT /api/policies/{id}/permissions`); **Discard** restores the saved set. Both are disabled without changes. Leaving the page with unsaved changes asks for confirmation.
6. Refusals inline (`PERMISSION_NOT_HELD` names the keys, 400 for unknown keys). Read-only without `auth.permissions.write` (no Add, Remove, Save).
7. The old dialog is removed; the policies table keeps Delete.

### Requirement 6: Tests and docs

1. Unit and integration tests: the counts (sources counted once, Inactive left out, tenant isolation), the role members endpoint (fields, permissions, 404), the paged catalogue (fields, System-only keys hidden, modules).
2. Web tests: the new API functions, the working-set helpers (add, remove, undo, discard, changed keys), the role members view's URL state.
3. TESTING.md Part 6 updated (counts, members section, the editor page); the API guide's 15.7 and ARCHITECTURE.md (role members endpoint, paged catalogue, counts on read).
