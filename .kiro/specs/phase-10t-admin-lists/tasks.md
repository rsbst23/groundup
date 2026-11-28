# Tasks: Admin Lists and Permission Refusals (GU-98)

## Gap analysis (requirements + design)

Findings, and how the tasks handle them:

1. **Breaking signatures.** The list methods on `IMemberManagementService`, `IInvitationService`, `IJoinLinkService` and the three repository interfaces change from `PaginationParams` to `FilterParams`. SDK callers that pass a `PaginationParams` stop compiling. This is accepted before 1.0, and the API guide notes it. HTTP callers are unaffected.
2. **Existing `FORBIDDEN` assertions.** `RolesApiTests` (line 397) and `HostTokenReconciliationIntegrationTests` (line 253) expect `FORBIDDEN`. Where the 403 comes from the proxy's permission check, they change to `PERMISSION_REQUIRED`; refusals from code (`RoleGrantPolicy`, tenant checks) stay `FORBIDDEN`. `AuthorizationInterceptorTests` loses its four exception-path tests.
3. **Status sort order.** Sorting by `status` uses the enum's numeric order, not the alphabetical order of the labels. The design keeps this; the API guide states it.
4. **Members status filter.** The base query only includes Active and Pending members, so `filters[status]=Removed` returns an empty page. This is correct and has a test.
5. **Page-back and placeholder data.** The pages keep the previous rows while loading (`keepPreviousData`). The page-back rule only reads real data (`!isPlaceholderData`), so it does not jump while a page loads.
6. **Tenant switch.** The URL state stays on a tenant switch. A page number past the end in the new tenant falls back through the page-back rule.
7. **Expired test data.** The integration tests set `ExpiresAt` in the past directly in the database, because the API refuses a past expiry.
8. **Proxy cost.** Checking every permission instead of stopping at the first costs at most one extra cached lookup per missing key. No change is needed.
9. **Registration check coverage.** The full test run builds every `AddAuthorized` service (members, roles, policies, invitations, join links, settings), so an existing interface that broke the rule would fail the run.

## 98a: Backend (branch `phase-10/admin-lists`)

- [x] 1. List pipeline
  - [x] 1.1 `ExpressionHelper`: split each builder into resolve + core; add public `LambdaExpression` selector overloads for exact, contains, starts/ends with, range, multi-value and sorting
  - [x] 1.2 `ListQueryOptions<T>`, `ListFieldMap<T>`, `QueryableListExtensions` (`ApplyListFilters`, `ApplyListOrder`, `ToPagedListAsync`)
  - [x] 1.3 `BaseRepository`: `ApplyFilters` and sorting through the extensions; new `GetAllAsync<TResult>(filterParams, queryShaper, projection, options)`; `BaseTenantRepository` override with the tenant shaper
  - [x] 1.4 Unit tests: `QueryableListExtensionsTests`, selector overloads (including a conditional selector), the new base overload (projection, tenant filter); existing `ExpressionHelper` and repository tests still pass
  - [x] 1.5 Build, run all suites, commit, stop for review
  - _Requirements: 1, 2.2, 2.4–2.7_

- [x] 2. The three lists
  - [x] 2.1 `InvitationRepository` and `JoinLinkRepository`: `EffectiveStatus(now)`, `Projection(now)`, field maps, `ListAsync(tenantId, FilterParams)` on `ToPagedListAsync`
  - [x] 2.2 `UserTenantRepository.GetMembersAsync(tenantId, FilterParams)` on `ToPagedListAsync`, with the roles query unchanged
  - [x] 2.3 Repository interfaces, the three services and interfaces, and the three controllers take `FilterParams`
  - [x] 2.4 `EffectiveStatus` agrees with the projection: checked in the integration tests (every status filter returns only rows showing that status), since both are private and the check is only meaningful in SQL
  - [x] 2.5 Integration tests per endpoint: each sort field both ways, search, status filter (Expired, Used up, Removed members = empty), an unknown field ignored, paging across ties, the default order, tenant isolation
  - [x] 2.6 Build, run all suites, commit, stop for review
  - _Requirements: 2, 3_

- [x] 3. Permission refusals and docs
  - [x] 3.1 `ErrorCodes.PermissionRequired`, `ErrorCodes.RoleRequired`
  - [x] 3.2 `AuthorizationInterceptor`: a shared check returning the refusal; `Fail(message, 403, code, errors)`; remove the exception and synchronous paths
  - [x] 3.3 `AddAuthorized`: the registration check for attributed methods' return types
  - [x] 3.4 Unit tests: missing keys exactly, roles listed, unauthenticated `FORBIDDEN`, the registration check names the method; remove the exception-path tests
  - [x] 3.5 Integration tests: a 403 body with `PERMISSION_REQUIRED` and `["auth.members.read"]`; update the proxy-refusal assertions (gap 2)
  - [x] 3.6 Docs: the API guide (the `FilterParams` query on the three endpoints, the allowed fields, the status order, the two codes, the signature change); ARCHITECTURE (the list pipeline, the refusal codes, the `OperationResult`-only rule for attributed methods)
  - [x] 3.7 Build, run all suites, commit, stop for review; PR 98a info
  - _Requirements: 4, 8.1, 8.2, 8.4_

## 98b: Web (new branch from main after 98a merges)

- [x] 4. List building blocks
  - [x] 4.1 `features/list/listSearch.ts` (zod schema, `parseSort`, `nextSort`), `debounce` + `useDebouncedValue`, `lastPageWithRows`
  - [x] 4.2 `api/paging.ts` `listPath`; `listMembers`, `listInvitations`, `listJoinLinks` take a `ListState`; query keys carry the state
  - [x] 4.3 `features/list/ListTable.tsx` (sortable headers, search, status select, paging)
  - [x] 4.4 Node tests for 4.1 and 4.2; update the existing API tests
  - [x] 4.5 Build, lint, test, commit, stop for review
  - _Requirements: 5_

- [ ] 5. Pages, RequirePermission and docs
  - [ ] 5.1 `RequirePermission`; `refusalText` in `permissions.ts`; `ErrorPanel` shows it for `PERMISSION_REQUIRED` and `ROLE_REQUIRED`
  - [ ] 5.2 Members, Invitations and Join links: `validateSearch` on the routes, `ListTable`, `RequirePermission`, the page-back rule
  - [ ] 5.3 Node tests: `refusalText`; the route search schemas
  - [ ] 5.4 README rule; TESTING.md Part 5: sorting, searching and filtering on each page, Carol's refusal message on Members
  - [ ] 5.5 Build, lint, test, commit, stop for review; PR 98b info
  - _Requirements: 6, 7, 8.3, 8.4_
