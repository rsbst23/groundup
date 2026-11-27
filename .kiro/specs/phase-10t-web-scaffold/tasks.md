# Tasks: Sample Web App Scaffold, API Client and Request Inspector (GU-86)

Execute one top-level task at a time. After each: build and test (npm and, where the API changes, all three .NET suites), commit, stop for review.

Baselines (main after #52): unit 1939 / 0; integration 525 / 0; sample 22/22.

Requirements: `requirements.md`. Design: `design.md`. One PR (branch `phase-10/web-scaffold`).

## Gap analysis (before tasks)

Found while checking the requirements and design against the code:
- **API down at startup.** The root loader waits for `/auth/me`. If the API is not running, the whole app would fail with an unhandled error. Add a root `errorComponent` that shows the `ErrorPanel` and says to start the sample API (task 3.4).
- **Top-level navigation through the proxy.** Keycloak returns with a full-page GET to `/auth/callback`; the proxy must handle navigations, not only `fetch` calls, before Vite's SPA fallback. Verify by hand in task 1 and in TESTING.md Part 1.
- **`.gitignore`** has no Node entries: add `node_modules/`, `dist/`, `.env`, `*.local` (keep `.env.example`).
- **Devtools.** TanStack Query and Router devtools load only in development (`import.meta.env.DEV`, lazy import), so `npm run build` does not ship them.
- **Inspector duration** (Requirement 6.1) was not in the design's store shape; each entry records start time and duration.
- **CSRF on sign-in** (Requirement 3.2): sign-in is a full-page redirect, which reloads the app and empties the in-memory token. Recorded in the README so a later client-side sign-in does not miss it.
- **The API is unchanged apart from `/auth/me`.** No HTTPS redirection, CORS or forwarded-headers changes are needed: the sample has none, and the proxy keeps Host.

## Tasks

- [x] 1. Project, tooling and skeleton (design: Versions, Project layout, Dev server and proxy)
  - [x] 1.1 `samples/GroundUp.Sample.Web`: `package.json` with exact versions, `.nvmrc`, `engines`, `.env.example`, tsconfig files (strict; a separate `tsconfig.test.json` gives the tests Node types), `eslint.config.js`, `index.html`, Vitest config; `.gitignore` entries
  - [x] 1.2 `vite.config.ts`: router plugin (test files ignored as routes), React plugin, port 5173 strict, allowed hosts, proxy for the five prefixes (`src/proxiedPrefixes.ts`) with Host kept; chunk warning limit 800 kB (MUI makes the main chunk about 550 kB, 175 kB gzipped)
  - [x] 1.3 Providers (`main.tsx`, `theme.ts`, `queryClient.ts` stub, `router.ts`), root route with `AppLayout` and `NavMenu`, Home, the placeholder routes, not-found page; devtools in development only; `routeTree.gen.ts` generated and committed
  - [x] 1.4 `/data` validates `?page=` with a zod 4 schema passed straight to `validateSearch`: types check and a bad value falls back to 1, so no adapter and no zod 3
  - [x] 1.5 `routes.test.ts`: no route starts with a proxied prefix (11 tests)
  - [x] 1.6 Check: build, lint (0 problems) and tests pass. With the API running: Home and placeholder routes load (and reload) through port 5173; `/health` and `/swagger` are proxied; `POST /auth/login` through the proxy gives `redirect_uri=http://localhost:5173/auth/callback` (Host kept); a browser-style GET of `/auth/callback` reaches the API (400 without a code), not the app

- [x] 2. API client (design: API client)
  - [x] 2.1 `api/errors.ts` (`ApiError`, `parseErrorBody` for every body shape) and `api/operationResult.ts` (`isOperationResult`, `unwrap`)
  - [x] 2.2 `api/csrf.ts` (`getCsrfToken` with shared pending fetch, 401 means no token, `clearCsrfToken`; a clear during a fetch discards the fetched token)
  - [x] 2.3 `api/inspector.ts` (ring buffer of 100, subscribe and snapshot, masking at record time, start time and duration)
  - [x] 2.4 `api/client.ts` (`apiFetch`: JSON, cookies, correlation ID, CSRF attach and single retry, inspector records including the CSRF fetch, `raw` option, headers in the result; aborts rethrown unchanged)
  - [x] 2.5 Tests: `errors.test.ts`, `csrf.test.ts`, `inspector.test.ts`, `client.test.ts`, with a shared fetch mock in `api/testing.ts` (53 tests in all, including the 11 route tests)
  - [x] 2.6 Check: build, lint (0 problems), tests (53 / 0)

- [ ] 3. Session, components and the `/auth/me` change (design: Server state, Components, API change)
  - [ ] 3.1 API: `AuthController.Me` returns `issuedAt`, `expiresAt`, `authTime`; `AuthControllerTests` for present, missing and unparseable claims
  - [ ] 3.2 `api/types.ts`, `api/auth.ts` (`getMe`, `getMyTenants`); `features/session/sessionQuery.ts` (`sessionQueryOptions` with 401 as null, `myTenantsQueryOptions`, `useSession`, `onSessionChanged`); `queryClient.ts` retry rules and the 401 handler; root loader `ensureQueryData`
  - [ ] 3.3 `ErrorPanel` (with the `REAUTH_REQUIRED` action), `SessionBanner` (countdown), `RequestInspector` drawer with badge, clear and copy
  - [ ] 3.4 Root `errorComponent` for an unreachable API; Home links (Swagger, Keycloak admin, both manuals, current host) and the Checks card (`GET /api/members`, `POST /auth/refresh`)
  - [ ] 3.5 `keycloak/realm.json`: port 5173 in redirect URIs, web origins and post-logout URIs
  - [ ] 3.6 Tests: `sessionQuery.test.ts` (401 gives null; `onSessionChanged` clears CSRF and invalidates)
  - [ ] 3.7 Check: npm build, lint, test; .NET build and all three suites

- [ ] 4. Documentation and the full check
  - [ ] 4.1 `README.md`: start-up steps, URL, folder layout and rules (no `fetch` in pages, feature folders, no route under a proxied prefix, add new API paths to the proxy, CSRF and sign-in reload)
  - [ ] 4.2 `TESTING.md`: how to use it, state table, Before You Start (including the three Keycloak URIs for an existing realm), Part 1 (design: Testing manual), parts to come
  - [ ] 4.3 ARCHITECTURE.md: the `/auth/me` session fields; one line on the sample web app under the sample section; the API guide's Before You Start points to the web app manual
  - [ ] 4.4 Run TESTING.md Part 1 by hand against the running API and Keycloak; record the result here
  - [ ] 4.5 Full check (npm build, lint, test; .NET suites); push; give the PR compare URL, title and description
  - [ ] 4.6 Jira: GU-86 comment; Done after merge
