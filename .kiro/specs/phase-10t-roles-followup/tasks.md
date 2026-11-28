# Tasks: Role Member Counts, Role Members, Permission Editor at Scale (GU-97d)

## Gap analysis (requirements + design)

1. **Callers of the grouped catalogue.** `PolicyService.GetCatalogAsync` is the only producer; its unit tests and the `GET /api/permissions` integration test (grouped by module, System-only keys hidden) are rewritten for the paged list. The web dialog that used it is removed in 97d-2.
2. **System tenant.** `GET /api/roles/{id}/members` on the System tenant answers 404, like the other role-id endpoints.
3. **Pending counts include invitees.** An invitation creates a pending membership holding the invited role, so the Pending column counts open invitations to that role. Intended; TESTING.md says so.
4. **Nested search params.** The router writes the nested views (`members`, `available`) as JSON in the query string. Invalid or old values are dropped by the schemas (tested), so a hand-edited link still opens.
5. **Your own role.** Removing the signed-in user from a role (for example TenantAdmin) on the role page refreshes the session like the Members page does; the API's last-admin rule still refuses the last TenantAdmin.
6. **`useBlocker`.** The router version in use (1.170) exports `useBlocker`; its options are checked when the page is written (task 4).
7. **Large policies.** The right side holds the policy's whole set in the browser; the saved names come from `GET /api/policies/{id}`, which already returns every permission of the policy. Fine for thousands of keys; noted in the design.
8. **Catalogue cache.** The catalogue is global (not per tenant), so its query keys have no tenant id; it is read-only from a tenant.

## 97d-1: Backend (branch `phase-10/roles-followup`)

- [x] 1. Member counts and role members
  - [x] 1.1 `TenantRoleDto` counts, `RoleCountsDto`, `IRoleRepository.GetCountsAsync` (replaces `CountPoliciesAsync`), the list projection and sort fields, `RoleService` reads
  - [x] 1.2 `IUserTenantRepository.GetMembersAsync(…, roleId)`, `IMemberManagementService.ListRoleMembersAsync`, `GET /api/roles/{id}/members`
  - [x] 1.3 Unit and integration tests (counts, sources once, Inactive and other tenants out, sorting; role members fields, 404, the two permissions)
  - [x] 1.4 Build, run all suites, commit, stop for review
  - _Requirements: 1, 2, 6.1_

- [x] 2. Paged permission catalogue and docs
  - [x] 2.1 `IPermissionRepository.ListCatalogAsync`, `ListModulesAsync`; `IPolicyService.ListCatalogAsync`, `GetModulesAsync`; `PermissionsController` (`GET /api/permissions`, `/modules`)
  - [x] 2.2 Tests (search by key and description, module filter, sorting, System-only keys hidden, modules); the old grouped tests rewritten
  - [x] 2.3 ARCHITECTURE.md (counts on read, the role members endpoint, the catalogue rows), API guide 15.1 and 15.7
  - [x] 2.4 Build, run all suites, commit, stop for review; PR 97d-1 info
  - _Requirements: 3, 6.1_

## 97d-2: Web (new branch after 97d-1 merges)

- [x] 3. Roles table and the members section
  - [x] 3.1 Types and API functions (`listRoleMembers`, counts); the Active and Pending columns
  - [x] 3.2 The role route's nested `members` view; `RoleMembersSection` (`ListTable`, remove from role)
  - [x] 3.3 `AddMemberDialog` (debounced Autocomplete over the members list)
  - [x] 3.4 Node tests; build, lint, test, commit, stop for review
  - _Requirements: 4_

- [ ] 4. Policy editor page
  - [ ] 4.1 API functions (`listPermissionCatalog`, `getPermissionModules`); `ListTable` `filters` slot
  - [ ] 4.2 Routes `policies.index.tsx` and `policies.$policyId.tsx`; `PolicyInfoDialog`; the policies table links; `PolicyDialog` removed
  - [ ] 4.3 `workingSet.ts` and `PolicyEditorPage` (available, in this policy, save, discard, the leave prompt, read-only, not found)
  - [ ] 4.4 Node tests; build, lint, test, commit, stop for review
  - _Requirements: 5_

- [ ] 5. TESTING.md
  - [ ] 5.1 Part 6 updated (counts, the members section, the editor page)
  - [ ] 5.2 Commit, stop for review; PR 97d-2 info
  - _Requirements: 6.3_
