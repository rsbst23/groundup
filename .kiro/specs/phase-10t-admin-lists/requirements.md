# Requirements: Admin Lists and Permission Refusals (GU-98)

## Context

The Members, Invitations and Join links pages (GU-90) page through their lists but cannot sort, filter or search. Their repositories build the lists by hand (`GetMembersAsync`, the invitation and join link lists) with a fixed order, so they skip the base repository's `FilterParams` pipeline (`ApplyFilters`, `ExpressionHelper.ApplySorting`, count, paging). The controllers take `PaginationParams`.

When the authorization proxy (`AuthorizationInterceptor`) refuses a call, it returns a bare `Forbidden()` (code `FORBIDDEN`, no detail), so a user sees "403 Forbidden" without knowing which permission is missing.

What exists: `FilterParams` (exact, contains, starts/ends with, min/max, multi-value, `SearchTerm`, `SortBy` as `"field"` or `"field desc"`, comma-separated); `ExpressionHelper` resolves dotted paths case-insensitively and ignores unknown sort columns; `BuildSearchPredicate` searches every string property of the entity. Invitation and join link lists show a computed status (Expired for a Pending invitation past its expiry; Expired or Used up for an Active link), which is not the stored column.

## Decisions (with Rob, Jira comment 10206)

1. A new base repository overload `GetAllAsync(filterParams, queryShaper, projection)`, built on a shared list pipeline that all three lists use (see Requirement 1.1). Filters, sorting, count and paging run on the entity; the projection only shapes the page's rows. The members list still loads the page's roles in a second query.
2. Callers use the DTO's field names (`email`, `status`, `roleName`, `createdAt`). Each repository maps an allowlist of them to entity paths (`email` → `User.Email`). Unknown fields are ignored.
3. Search fields per list: members email and display name; invitations email; join links role name.
4. New error code `PERMISSION_REQUIRED` with the missing keys in `errors` and a message naming them. (`[RequiresPermission]` only has "require all"; there is no "require any" mode to report.) Role checks get `ROLE_REQUIRED`. Other 403s keep `FORBIDDEN`. The web app shows keys for now; localized labels come with GU-99.
5. Web: a shared list component (sortable headers, search box with a 300 ms debounce, status filter, paging), state in the URL. No generic filter builder.
6. A `RequirePermission` wrapper shows "You need the permission … to see this page" without calling the API. A 403 that still arrives shows the missing permission from the response. Used by Members, Invitations, Join links and future admin pages; a README rule says so.
7. Two PRs: **98a** backend, **98b** web.

## Scope

**In scope:** the base overload, the three repositories and controllers, the proxy refusal codes, the web list component, the three pages, `RequirePermission`, tests, docs.

**Out of scope:**
- Localized permission labels (GU-99).
- The roles and policies lists (GU-97 will use the list component and the overload).
- A generic filter builder UI, saved views, column choosers.
- Unauthenticated calls through the proxy: they keep today's refusal (`FORBIDDEN`).

## Requirements

### Requirement 1: Shared list pipeline and base overload (98a)

1. The list pipeline is a public extension on `IQueryable<TEntity>` in `GroundUp.Repositories` (option 1, chosen with Rob, because `InvitationRepository` and `JoinLinkRepository` do not inherit a base repository). Repositories that do not inherit the base call it on their own query.
2. `BaseRepository` gains a protected `GetAllAsync<TResult>(FilterParams, queryShaper, projection, options)` returning `OperationResult<PaginatedData<TResult>>`. It and the existing `GetAllAsync` run through the same pipeline.
3. Order of operations: shaper → filters → search → sorting → count → paging → projection. The projection is translated by EF (runs in SQL), not applied in memory.
4. `BaseTenantRepository` applies the tenant filter in the new overload the same way as in the existing one (before the caller's shaper).
5. Callers can pass a field map (decision 2) and search fields (decision 3). Without them, field names are entity property paths and search covers every string property, as today.
6. The existing `GetAllAsync` overloads behave as before.
7. Paging keeps the `MaxPageSize` guard.

### Requirement 2: Field allowlists (98a)

1. Each list declares the fields it accepts for sorting and filtering, by DTO name, case-insensitive:

   | List | Sort | Filter | Search |
   |---|---|---|---|
   | Members | `email`, `displayName`, `status` | `status` | email, display name |
   | Invitations | `email`, `roleName`, `status`, `expiresAt`, `createdAt` | `status`, `roleId` | email |
   | Join links | `roleName`, `status`, `useCount`, `expiresAt`, `createdAt` | `status`, `roleId` | role name |

2. A field outside the allowlist, in `SortBy` or any filter dictionary, is dropped without an error.
3. `status` filters and sorts on the status the list shows (the computed one for invitations and join links), so "Expired" finds Pending invitations past their expiry.
4. Status values are accepted as the enum's number or name (`2` or `Revoked`).
5. Without `SortBy` (or when every sort field is dropped) the order stays as today: members by email; invitations and join links newest first.
6. Every order ends with the row's id as a tie-breaker so pages do not overlap or skip rows.
7. Search is case-insensitive substring matching on the listed fields only.

### Requirement 3: Lists and endpoints (98a)

1. `GET /api/members`, `GET /api/invitations` and `GET /api/join-links` take `[FromQuery] FilterParams` instead of `PaginationParams`. Existing callers sending only `pageNumber` and `pageSize` get the same results.
2. The three repositories build their lists through the new overload (Requirement 1). Their DTOs do not change.
3. The members list still loads the page's roles (with sources) in one extra query for the page's users.
4. The service interfaces' list methods take `FilterParams`; permission attributes are unchanged.

### Requirement 4: Permission refusals (98a)

1. `ErrorCodes` (or `AuthErrorCodes`, decided in the design) gains `PERMISSION_REQUIRED` and `ROLE_REQUIRED`.
2. When a `[RequiresPermission]` check fails, the proxy returns 403 with `PERMISSION_REQUIRED`, `errors` holding every listed key the user lacks (all are required), and a message naming them.
3. When a `[RequiresRole]` check fails: 403 with `ROLE_REQUIRED`, all the listed roles in `errors` (any one would do), and a message saying "one of" when there are several.
4. Every other 403 keeps `FORBIDDEN`.
5. Every method with `[RequiresPermission]` or `[RequiresRole]` must return `Task<OperationResult>` or `Task<OperationResult<T>>`. `AddAuthorized` throws at registration when an attributed method returns anything else, naming the method. The proxy's `ForbiddenAccessException` paths for other return types are removed.
6. Messages are plain English, like the other framework messages (the framework has no resource files yet; localization comes with GU-99).

### Requirement 5: Shared list component (98b)

1. A component for paged tables: sortable column headers (click toggles ascending, descending), a search box (300 ms debounce, resets to page 1), an optional status filter (a select, "All" by default, resets to page 1) and paging (25 per page).
2. Its state (`page`, `sort`, `q`, `status`) lives in the URL search params, so a refresh, the back button and a copied link restore the view. Missing params use the defaults; invalid values fall back to the defaults.
3. It builds the query string `FilterParams` expects (`pageNumber`, `pageSize`, `sortBy`, `searchTerm`, the status filter).
4. Query keys include the list's state so each view is cached separately.
5. Keyboard: headers are buttons with `aria-sort`; the search box has a label.

### Requirement 6: Pages (98b)

1. Members, Invitations and Join links use the list component with the columns and filters of Requirement 2.
2. Actions (remove, add role, resend, revoke, regenerate) keep working and reload the current view, not page 1.
3. When a change empties the current page (for example revoking the last row on page 3 under a status filter), the page moves back to the last page that has rows.

### Requirement 7: RequirePermission (98b)

1. `RequirePermission` takes one key, reads the session's permissions (`can`), and renders its children only when the user has it.
2. Otherwise it shows "You need the permission `auth.members.read` to see this page", and the page's queries do not run.
3. `ErrorPanel` shows `PERMISSION_REQUIRED` and `ROLE_REQUIRED` refusals as "You need the permission …" with the keys from `errors`, alongside the existing status, code and correlation id.
4. Members, Invitations and Join links use it. README gains the rule: every admin page wraps its content in `RequirePermission`.

### Requirement 8: Tests and docs

1. Unit tests: the overload's order of operations and projection, allowlist dropping, number and name status values, the computed status filter, the tie-breaker, the default order, the refusal codes (all, any, role, non-`OperationResult`).
2. Integration tests (Testcontainers): sorting, filtering, search and paging on each list against Postgres, including a dropped unknown field, the computed Expired status and tenant isolation; a 403 response carrying `PERMISSION_REQUIRED` and the missing key.
3. Web tests (Node): the URL state parser and query builder, the debounce, the refusal text, the page-back rule.
4. TESTING.md: a section for sorting, searching and filtering on each page, and Carol's refusal message on Members. The API guide notes the `FilterParams` query and the new codes. ARCHITECTURE notes the overload and the refusal codes.
