# Design: Admin Lists and Permission Refusals (GU-98)

## Overview

98a moves the list pipeline out of `BaseRepository` into a public `IQueryable` extension with an optional field map, rebuilds the three admin lists on it, switches their endpoints to `FilterParams`, and makes the authorization proxy say which permission or role is missing. 98b adds a URL-driven list component, rebuilds the three pages on it, and adds `RequirePermission`.

## 98a: Backend

### 1. List pipeline (`GroundUp.Repositories`)

New files:

```csharp
// ListQueryOptions.cs
public sealed class ListQueryOptions<TEntity>
{
    /// Allowlisted fields by DTO name. Null: names are entity property paths (today's behaviour).
    public ListFieldMap<TEntity>? Fields { get; init; }
    /// Used when SortBy is empty or every sort field was dropped.
    public Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? DefaultOrder { get; init; }
    /// Appended to every order so pages are stable (for example l => l.Id).
    public Expression<Func<TEntity, Guid>>? TieBreaker { get; init; }
}

// ListFieldMap.cs
public sealed class ListFieldMap<TEntity>
{
    public ListFieldMap<TEntity> Sort<TValue>(string name, Expression<Func<TEntity, TValue>> selector);
    public ListFieldMap<TEntity> Filter<TValue>(string name, Expression<Func<TEntity, TValue>> selector);
    public ListFieldMap<TEntity> SortAndFilter<TValue>(string name, Expression<Func<TEntity, TValue>> selector);
    public ListFieldMap<TEntity> Search(params Expression<Func<TEntity, string?>>[] fields);
    // internal: TryGetSort(name), TryGetFilter(name), SearchFields; names compared case-insensitively
}

// QueryableListExtensions.cs
public static class QueryableListExtensions
{
    public static IQueryable<T> ApplyListFilters<T>(this IQueryable<T> query, FilterParams filters, ListQueryOptions<T>? options = null);
    public static IQueryable<T> ApplyListOrder<T>(this IQueryable<T> query, string? sortBy, ListQueryOptions<T>? options = null);
    public static Task<PaginatedData<TResult>> ToPagedListAsync<T, TResult>(
        this IQueryable<T> query, FilterParams filters, Expression<Func<T, TResult>> projection,
        ListQueryOptions<T>? options = null, CancellationToken cancellationToken = default);
}
```

Selectors are expressions, not paths, so a field can be a navigation (`ut => ut.User.Email`), a subquery (the role name), or a computed value (the shown status).

**Filters with a map.** For each key in `Filters`, `ContainsFilters`, `StartsWithFilters`, `EndsWithFilters`, `MinFilters`/`MaxFilters` and `MultiValueFilters`: look the key up with `TryGetFilter`; drop it when missing; otherwise build the predicate on the selector. Search: when `SearchTerm` is set, OR a null-safe, lower-cased `Contains` over `SearchFields`. With no map, the code calls today's name-based `ExpressionHelper` builders and `BuildSearchPredicate`.

**`ExpressionHelper` refactor.** Each name-based builder currently resolves the path (`FindPropertyPath`) and then builds on the member expression and `PropertyInfo.PropertyType`. I split each into "resolve" and a private core that takes `(ParameterExpression, Expression body, Type type)`. New public overloads take a `LambdaExpression selector` and call the core with `selector.Parameters[0]`, `selector.Body` and `selector.ReturnType`. Value parsing (`ConvertTo`) is shared, so enums accept `2` or `Revoked` (`Enum.Parse`). An unparsable value behaves as it does today: the filter is skipped (`AlwaysTrue`).

**Sorting with a map.** Split `SortBy` on commas; each part is `name` or `name desc`; drop unknown names; apply `OrderBy`/`ThenBy` (or the `Descending` forms) with the selector, built with `Expression.Call(typeof(Queryable), …, Expression.Quote(selector))`. When nothing is left, use `DefaultOrder`. Then append `TieBreaker` if set. With no map, `ApplySorting(query, sortBy)` runs as today.

**`ToPagedListAsync`.** Runs filters → order → `CountAsync` → `Skip`/`Take` → `Select(projection)` → `ToListAsync`, and returns `PaginatedData<TResult>`. Paging values come from `FilterParams` (the `MaxPageSize` guard already lives in `PaginationParams`).

### 2. Base repository

- `BaseRepository.ApplyFilters` (protected virtual, no overrides exist) becomes `query.ApplyListFilters(filterParams)`, and the sorting step becomes `ApplyListOrder`. The existing `GetAllAsync` keeps its in-memory `_mapToDto` mapping, so its behaviour does not change.
- New: `protected virtual Task<OperationResult<PaginatedData<TResult>>> GetAllAsync<TResult>(FilterParams, Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryShaper, Expression<Func<TEntity, TResult>> projection, ListQueryOptions<TEntity>? options = null, CancellationToken = default)`. It runs `DbSet.AsNoTracking()`, then the shaper, then `ToPagedListAsync`.
- `BaseTenantRepository` overrides it with `ComposeTenantShaper(queryShaper)`, like the existing overload.

### 3. The three lists

Each repository builds its map and options inside the list method, because the computed status needs `now`:

| Repository | Base query | Default order | Tie-breaker |
|---|---|---|---|
| `UserTenantRepository.GetMembersAsync` | tenant, Active or Pending | `User.Email` | `UserId` |
| `InvitationRepository.ListAsync` | tenant | `CreatedAt desc` | `Id` |
| `JoinLinkRepository.ListAsync` | tenant | `CreatedAt desc` | `Id` |

The fields come from Requirement 2.1. Notes:
- `roleName` is the correlated subquery the projections already use (`Set<Role>().Where(r => r.Id == x.RoleId).Select(r => r.Name).FirstOrDefault()`), because neither entity has a `Role` navigation.
- The computed status lives in one static expression per repository, `EffectiveStatus(now)`. The map uses it, and the projection keeps its inline copy. A unit test checks that both give the same value for every state.
- Projections: `Project(IQueryable)` becomes `Projection(DateTime now)`, returning `Expression<Func<T, TDto>>`. The other callers use `query.Select(Projection(now))`.
- Members: `ToPagedListAsync` projects to `{ UserId, Email, DisplayName, Status }`. The roles query and the grouping stay as they are.

Signatures change from `PaginationParams` to `FilterParams` on `IUserTenantRepository.GetMembersAsync`, `IInvitationRepository.ListAsync`, `IJoinLinkRepository.ListAsync`, the three service interfaces and implementations, and the three controllers (`[FromQuery] FilterParams`). Query binding is the same as on the sample's `TodoItemsController`: `?sortBy=email desc&searchTerm=ali&filters[status]=Pending&pageNumber=2&pageSize=25`. Roles and policies stay on `PaginationParams` (GU-97).

### 4. Permission refusals

- `ErrorCodes` (Core, next to `Forbidden`) gains `PermissionRequired = "PERMISSION_REQUIRED"` and `RoleRequired = "ROLE_REQUIRED"`. They belong in Core because the attributes and the settings services live there.
- `AuthorizationInterceptor`:
  - One `CheckAsync(requiresPermission, requiresRole)` returns `Refusal? (code, message, errors)`:
    - unauthenticated → `FORBIDDEN` (unchanged);
    - role → `ROLE_REQUIRED`, errors = all listed roles, message "You need the role X." or "You need one of the roles X, Y.";
    - permission → check every key (not stopping at the first), errors = the keys not held, message "You need the permission X." or "You need the permissions X, Y.".
  - The generic and non-generic paths return `OperationResult<T>.Fail(message, 403, code, errors)` / `OperationResult.Fail(...)`.
  - The `ForbiddenAccessException` paths and the synchronous path are removed. `Invoke` throws `InvalidOperationException` if an attributed method does not return an `OperationResult` (it cannot happen after the registration check).
- `AddAuthorized<TInterface, TImplementation>` checks `typeof(TInterface).GetMethods()` once at registration (the same method set the interceptor reads). For any method with either attribute whose return type is not `Task<OperationResult>` or `Task<OperationResult<T>>`, it throws `InvalidOperationException("{Interface}.{Method} has [RequiresPermission] or [RequiresRole] but returns {Type}; it must return Task<OperationResult> or Task<OperationResult<T>>.")`.
- The settings services already refuse in code with `FORBIDDEN` from `ISettingsAuthorizer`; those stay as they are (Requirement 4.4).

### 5. Tests (98a)

Unit tests (`GroundUp.Tests.Unit`) run the pipeline on the in-memory `TestDbContext` the other repository unit tests use (logic only); SQL translation is covered by the Postgres integration tests:
- `QueryableListExtensionsTests`: map filters for each dictionary type, unknown keys dropped, search over the named fields only, multi-field sort and `desc`, default order, tie-breaker, enum by number and name, unparsable values skipped, no map = today's behaviour.
- `ExpressionHelper` selector overloads, including a conditional selector.
- `EffectiveStatus` agrees with the projections (invitations, join links).
- `AuthorizationInterceptorTests`: `PERMISSION_REQUIRED` with exactly the missing keys, `ROLE_REQUIRED` with all roles, unauthenticated stays `FORBIDDEN`, the registration check throws for a bad interface and names the method. Existing exception-path tests are removed.

Integration tests (`GroundUp.Tests.Integration`, `AdminApiTestBase`): for each endpoint, sort by each allowed field both ways, search, status filter (including Expired and Used up), an unknown field ignored, paging across ties, and tenant isolation with filters. Plus a 403 body: `errorCode = PERMISSION_REQUIRED`, `errors = ["auth.members.read"]`.

## 98b: Web

### 6. List state in the URL

- `src/features/list/listSearch.ts`: a zod schema for `{ page?: number, sort?: string, q?: string, status?: string }`, used by each route's `validateSearch`. Invalid values are dropped through `.catch(undefined)`. `parseSort("email desc")` returns `{ field, direction }`, and `nextSort(current, field)` cycles ascending → descending → ascending.
- `src/api/paging.ts`: `listPath(path, state, statusParam = 'status')` builds `pageNumber`, `pageSize`, `sortBy`, `searchTerm` and `filters[status]`. `pagedPath` stays for the roles picker.
- API functions (`listMembers`, `listInvitations`, `listJoinLinks`) take a `ListState` in place of `page`. Query keys become `[key, tenantId, state]`.

### 7. `ListTable` component

`src/features/list/ListTable.tsx` takes the columns (`{ field?, label, render }`, where a column with a `field` is sortable), the rows, the total, the current state, `onChange(state)`, an optional status options list, and an optional toolbar slot. It renders MUI `TableSortLabel` (a button with `aria-sort` on the cell), a labelled `TextField` debounced 300 ms by a `useDebouncedValue` hook, a `Select` with "All", and `TablePagination`. Changing the search or the status sets `page` to 1. Pages call `navigate({ search })` with `replace: true` for search typing and a normal push for paging and sorting.

### 8. Pages

- Members, Invitations and Join links read `Route.useSearch()`, pass it to the query, and render `ListTable`. Actions invalidate the list key prefix, so the current view reloads.
- Page-back: when a query returns no items, `page > 1` and `totalRecords > 0`, the page navigates to `ceil(totalRecords / pageSize)` (a pure `lastPageWithRows(totalRecords, pageSize)` helper).

### 9. `RequirePermission` and refusals

- `src/features/access/RequirePermission.tsx`: `<RequirePermission permission={PermissionKeys.MembersRead} purpose="see this page">`. It renders its children only when `can(me, permission)`. Otherwise it shows an info `Alert`: "You need the permission `auth.members.read` to see this page." The children (and so the queries) never mount. It sits inside `RequireTenant`.
- `ErrorPanel`: for `PERMISSION_REQUIRED` or `ROLE_REQUIRED`, it adds the line from `refusalText(error)` ("You need the permission auth.members.read."), built from `errors`, above the existing details. The helper lives in `features/access/permissions.ts` and is tested.
- README: "Every admin page wraps its content in `RequirePermission` with the key its list endpoint needs."

### 10. Tests and docs (98b)

- Node tests: the `listSearch` schema (defaults, invalid values), `parseSort`/`nextSort`, `listPath` (encoding, `filters[status]`), `useDebouncedValue` with fake timers (the existing tests are Node-only, so the debounce lives in a plain `debounce` function and the hook wraps it), `refusalText`, `lastPageWithRows`, and the changed API functions' query strings.
- TESTING.md Part 5 gains a section for sorting, searching and filtering on each page and for Carol's refusal message on Members. API guide: the `FilterParams` query on the three endpoints and the two codes. ARCHITECTURE: the list pipeline and the refusal codes.

## Files (estimate)

- **98a** (about 20 files):
  - Repositories: `ListQueryOptions`, `ListFieldMap`, `QueryableListExtensions`, `ExpressionHelper`, `BaseRepository`, `BaseTenantRepository`.
  - The three repositories, their interfaces, services and controllers.
  - `ErrorCodes`, `AuthorizationInterceptor`, `AuthorizationServiceCollectionExtensions`.
  - Tests.

  I'll split it into task groups so each commit stays reviewable: pipeline; lists; refusals.
- **98b** (about 15 files): the list folder, `paging.ts`, the three API files, the three pages and routes, `RequirePermission`, `ErrorPanel`, `permissions.ts`, tests, docs.
