# Tasks: Roles, Policies and Permissions Pages (GU-97)

## Gap analysis (requirements + design)

1. **Breaking signatures.** `IRoleService.ListAsync`, `IPolicyService.ListAsync` and `IPolicyRepository.ListAsync` take `FilterParams` instead of `PaginationParams` (as GU-98 did for the other lists). HTTP callers are unaffected; the API guide notes it.
2. **Sort names on the roles list.** Today `sortBy` goes straight to entity paths (any `Role` property). With the field map only `name`, `roleType` and `policyCount` sort; `Name desc` still works (names ignore case). Existing tests that sort by another property would change; none found so far, checked in task 1.
3. **`isDefault` in the projection** replaces the service's separate lookup; the existing test "the default marked" must still pass.
4. **Deleting the default role** returns `ROLE_IN_USE` ("it is the default role") from the API; the page shows the reasons, and the default box offers **Clear**.
5. **Index route path.** `roles.index.tsx` is `/roles/` in the typed route tree; `NavMenu` and links use the path the router generates. Checked when the route tree is regenerated (task 2).
6. **Invalid role id in the URL.** `/api/roles/{id:guid}` returns a plain 404 for a non-GUID; `apiFetch` gives an `ApiError` with status 404, so the page shows "Role not found" for any 404.
7. **Pickers are capped at 100** (roles and policies), as the role picker already is. Noted in the page code and TESTING.md.
8. **Default role read.** `GET /api/roles/default` returns `data: null` when none is set; the API function returns `null`.
9. **Escalation guard on policies.** `PUT /api/policies/{id}/permissions` and attaching a policy can return `PERMISSION_NOT_HELD`; both dialogs show it.

## 97a: Backend (branch `phase-10/roles-pages`)

- [x] 1. Roles and policies lists on `FilterParams`
  - [x] 1.1 `TenantRoleDto.PolicyCount` (default 0), `TenantRoleDto.From(…, policyCount)`
  - [x] 1.2 `IRoleRepository.ListAsync(FilterParams)` and `CountPoliciesAsync(roleId)`; `RoleRepository` on the projected base overload with the field map (`isDefault`, `policyCount` in the query)
  - [x] 1.3 `IPolicyRepository.ListAsync(FilterParams)` on `ToPagedListAsync` with the field map
  - [x] 1.4 Services and controllers take `FilterParams`; `RoleService` get, update and set-default fill `PolicyCount`
  - [x] 1.5 Unit and integration tests (sorts both ways, search, `roleType` and `isSystem` filters, unknown fields ignored, `isDefault`, `policyCount`, default order, tenant isolation)
  - [x] 1.6 ARCHITECTURE.md (the `FilterParams` rule, the two rows, `policyCount`), API guide 15.7
  - [x] 1.7 Build, run all suites, commit, stop for review; PR 97a info
  - _Requirements: 1, 6.1, 6.2_

## 97b: Roles pages (new branch after 97a merges)

- [x] 2. Roles list page
  - [x] 2.1 Types, `roleTypeLabel`, `api/roles.ts` (page, get, create, update, delete, default get/set/clear, role policies and permissions), `listAllPolicies` in `api/policies.ts`
  - [x] 2.2 `rolesQuery.ts` (keys, options, `useRoleActions`)
  - [x] 2.3 Routes `roles.index.tsx` and `roles.$roleId.tsx` (placeholder for the detail), the old placeholder removed, `NavMenu`
  - [x] 2.4 `RolesPage` with `ListTable`, `DefaultRoleBox`, `CreateRoleDialog`, delete
  - [x] 2.5 Node tests (API functions, labels, route schemas)
  - [x] 2.6 Build, lint, test, commit, stop for review
  - _Requirements: 2, 5_

- [x] 3. Role detail page
  - [x] 3.1 `RoleDetailPage`: header, `EditRoleDialog`, delete, policies (`AttachPolicyDialog`, detach), effective permissions, TenantAdmin view, not found
  - [x] 3.2 `attachablePolicies` helper and its tests
  - [x] 3.3 Build, lint, test, commit, stop for review; PR 97b info
  - _Requirements: 3, 5_

## 97c: Policies page (new branch after 97b merges)

- [ ] 4. Policies page
  - [ ] 4.1 `api/policies.ts` (page, get, create, update, set permissions, delete, catalogue), `policiesQuery.ts`
  - [ ] 4.2 Route `policies.tsx`, `NavMenu` entry, `PoliciesPage` with `ListTable` and delete
  - [ ] 4.3 `PolicyDialog` with the catalogue by module; `permissionChoices.ts` (`toggleKey`, `moduleState`, `changedKeys`)
  - [ ] 4.4 Node tests (API functions, helpers, route schema)
  - [ ] 4.5 Build, lint, test, commit, stop for review
  - _Requirements: 4, 5_

- [ ] 5. TESTING.md and the API guide
  - [ ] 5.1 TESTING.md Part 6 (roles and policies, TenantAdmin limits, refusals, the 403 → 200 check, sorting and search); 5.1 uses Part 6; the "Parts to Come" table
  - [ ] 5.2 API guide 15.1 points to Part 6
  - [ ] 5.3 Commit, stop for review; PR 97c info
  - _Requirements: 6.4_
