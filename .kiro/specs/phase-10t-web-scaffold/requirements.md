# Requirements: Sample Web App Scaffold, API Client and Request Inspector (GU-86)

## Context

The sample app (`samples/GroundUp.Sample`) is an API with Swagger and no pages. The auth flows are browser journeys (redirects to Keycloak and back, cookies, CSRF, tenant switching), which Swagger cannot follow, so the manual testing guide needs copied tokens and SQL. Epic GU-85 adds a browser app next to the sample API. This story builds its foundation: the project, the API client every page uses, error display, a request inspector and a session banner. Later stories add the pages (GU-87 to GU-97).

The web app may later become **the** sample app that other GroundUp applications copy. So it is a test tool in scope (plain forms and tables are enough), but its structure, libraries and conventions should be good enough to copy.

Facts this relies on:
- The API builds the Keycloak `redirect_uri` from the request's scheme and Host (`AuthFlowService`), and the post-logout URI the same way (`AuthController.Logout`). Behind a proxy that keeps the Host header, Keycloak returns to the web app's origin, so the auth cookie is set there.
- Every auth cookie is HttpOnly. The browser app cannot read `AuthToken`, so it cannot decode the token itself.
- `GET /auth/me` returns `userId`, `email`, `displayName`, `tenantId` and `roles`. It does not return the token's expiry or sign-in time.
- A cookie-authenticated POST, PUT, PATCH or DELETE needs `X-CSRF-Token` from `GET /auth/csrf`, or it gets `403 CSRF_VALIDATION_FAILED` (GU-72).
- Errors come in more than one shape: `OperationResult` (`message`, `errorCode`, `errors`, `statusCode`), the auth controllers' `{ error, errorCode }`, ASP.NET Core `ProblemDetails`, and empty bodies (for example a bare 401).
- The dev Keycloak client allows redirect URIs on ports 5000, 5001, 5117 and 7295 only.

## Scope

**In scope:**
- the web app project, tooling and conventions
- the same-origin dev proxy, and the Keycloak redirect URIs for the web app's port
- the API client (cookies, CSRF, error normalization, correlation IDs)
- TanStack Query setup
- error display, the request inspector, the layout and navigation, the session banner
- a small, additive change to `GET /auth/me` (Requirement 9)
- unit tests for the API client
- run instructions

**Out of scope:**
- **The pages for each area.** Setup, sign-in, members, roles, settings, data and diagnostics come in GU-88 to GU-97. This story adds their navigation entries as placeholders.
- **The post-sign-in redirect** (GU-87). Until it lands, the browser ends on the callback's JSON, served through the web app's origin.
- **Bearer-token mode** (GU-79). The web app uses the cookie only.
- **Production hosting, Docker image, CI.** Development only. CI comes with GU-62.
- **Localization of web app text.** English only; the framework's localization plan (Phase 22) does not cover the web app yet.
- **End-to-end browser tests** (Playwright). Pages are checked by hand.

## Requirements

### Requirement 1: Project and tooling

1. The web app SHALL live in `samples/GroundUp.Sample.Web`. It is not part of `groundup.sln` and is never packed.
2. It SHALL use React, Vite and TypeScript (strict mode), Material UI (MUI) for components, TanStack Query for server state, and TanStack Router with file-based routes (the Vite plugin generates the route tree).
3. Route parameters and search parameters (for example a list page's filter, sort and page) SHALL be typed and validated in the route definition, so a page reads them as typed values and a bookmarked URL restores the same view.
4. Node 22 SHALL be pinned in `.nvmrc` and in `package.json` `engines`. `package-lock.json` SHALL be committed. Dependencies SHALL be pinned to exact versions. TypeScript SHALL be 7 if Vite, Vitest and the lint tooling support it, otherwise the latest 5.x, with the reason in the design.
5. Scripts: `npm run dev` (dev server with the proxy), `npm run build` (type check and production build), `npm test` (Vitest, single run), `npm run lint`.
6. The folder layout SHALL separate the API client, shared components, and one folder per feature area, and SHALL be described in the web app's README so later stories (and copying apps) follow it.
7. No secrets in the repo. Configuration (the API's address) comes from an environment variable with a local default.

### Requirement 2: Same-origin dev proxy

1. The dev server SHALL proxy `/api`, `/auth`, `/setup`, `/health` and `/swagger` to the sample API. The API's address SHALL come from an environment variable, default `http://localhost:5000`.
2. The proxy SHALL keep the browser's Host header, so the API builds the Keycloak `redirect_uri` and post-logout URI on the web app's origin, and the host tenant resolver sees the web app's host.
3. The proxy SHALL pass `X-Correlation-Id`, cookies and `Set-Cookie` through unchanged.
4. The dev server SHALL accept subdomain hosts under the configured dev domain (for example `*.groundup.test`), so the subdomain tests (MANUAL_TESTING.md Part 12) can run through it.
5. `keycloak/realm.json` SHALL allow the web app's callback (`http://localhost:5173/auth/callback`) and post-logout URI (`http://localhost:5173/`). MANUAL_TESTING.md SHALL say how to add them to an existing realm.

### Requirement 3: API client

1. All calls SHALL go through one client. Requests are same-origin and send cookies.
2. **CSRF.** Before the first POST, PUT, PATCH or DELETE, the client SHALL fetch `GET /auth/csrf` and keep the token in memory only. It SHALL send it as `X-CSRF-Token` on every such request. On `403 CSRF_VALIDATION_FAILED` it SHALL fetch a new token and retry once. After sign-in, sign-out or a tenant switch it SHALL drop the token, because the token is tied to the user.
3. **Errors.** Every failed call SHALL produce one error type with: HTTP status, message, error code, the errors list, and the correlation ID. It SHALL read each response shape listed in Context. A network failure or an unreadable body still produces this type, with a plain message.
4. **Success bodies.** The client SHALL unwrap `OperationResult.data` where the endpoint returns an `OperationResult`, and return the body as is otherwise. It SHALL also expose the response headers, so pages can show the pagination headers (X-Total-Count, X-Page-Number, X-Page-Size, X-Total-Pages).
5. **Correlation IDs.** The client SHALL read `X-Correlation-Id` from every response. A caller MAY send its own.
6. Endpoint functions SHALL be typed. DTO types are written by hand for now; generating them from the OpenAPI document is a later decision.

### Requirement 4: Server state

1. TanStack Query SHALL be set up once for the app. It SHALL NOT retry 4xx responses. It MAY retry network errors and 5xx once.
2. The session (`GET /auth/me`) SHALL be one shared query. Anything that changes who is signed in or the current tenant SHALL refresh it.
3. A `401` from any call SHALL mark the session as signed out.

### Requirement 5: Error display

1. One component SHALL show an error: status, message, error code, the errors list, and the correlation ID with a copy button.
2. `401 REAUTH_REQUIRED` SHALL show a "Sign in again" action. Until GU-89 adds the sign-in page, it links to the placeholder.

### Requirement 6: Request inspector

1. A collapsible panel SHALL list the most recent calls (at least 50) with method, URL, status, duration, correlation ID, request and response headers and bodies.
2. Values that must not be shown SHALL be masked: the `Authorization`, `Cookie`, `Set-Cookie` and `X-CSRF-Token` headers, and body fields whose names contain `token`, `password` or `secret` (case-insensitive). A page that has to show a value once (for example a new join link) shows it in the page, not in the inspector.
3. The history SHALL live in memory only and SHALL be cleared on reload. A clear button empties it.

### Requirement 7: Layout and navigation

1. An app bar and a navigation menu SHALL list the areas: Home, Setup, Session, Members, Invitations, Join links, Roles, Settings, Data, Diagnostics. Areas not built yet show a placeholder naming their story.
2. Home SHALL link to Swagger, the Keycloak admin console and the web app's testing manual, and show which host the web app is on.
3. An unknown route SHALL show a not-found page.

### Requirement 8: Session banner

1. Every page SHALL show: signed in or not, the user's email, the current tenant (name, or "no current tenant"), the roles, and when the token expires.
2. The tenant name comes from `GET /auth/me/tenants`, matched by id.
3. The expiry counts down. It does not refresh the token (GU-89 adds refresh).

### Requirement 9: Session times from `/auth/me`

The banner needs the token's times, and the app cannot read the HttpOnly cookie.
1. `GET /auth/me` SHALL also return `issuedAt`, `expiresAt` and `authTime` (UTC, ISO 8601) from the current token's `iat`, `exp` and `auth_time` claims, or null when a claim is missing.
2. The change is additive. Existing fields and their meaning do not change.
3. Unit tests cover present and missing claims.

### Requirement 10: Tests

1. Vitest unit tests SHALL cover: the CSRF fetch, header and single retry; dropping the token on sign-in, sign-out and tenant switch; each error shape (including empty and unreadable bodies and a network failure); unwrapping `OperationResult`; reading the correlation ID; and inspector masking.
2. The .NET suites SHALL still pass (Requirement 9 touches the API).

### Requirement 11: Run instructions and the web app's testing manual

1. The web app's README SHALL say how to start it (Docker, the sample API, `npm install`, `npm run dev`), which URL to open, and the folder conventions.
2. The web app SHALL have its own testing manual, `samples/GroundUp.Sample.Web/TESTING.md`, separate from the API guide (`.kiro/specs/phase-10c-auth-dispatcher/MANUAL_TESTING.md`, which stays as the Swagger and API reference). This story creates it with:
   - how to use it: test users, starting state, what each part leaves behind (the same style as the API guide)
   - "Before You Start": Docker, migrations, the sample API, the web app, and the Keycloak redirect URIs for port 5173 (adding them to an existing realm)
   - Part 1: the scaffold itself (the layout, the session banner signed out and signed in, an error with its correlation ID, the request inspector and its masking, a cookie POST without manual CSRF)
   - a placeholder list of the parts later stories add (one per page story)
3. Each later page story SHALL add its part to this manual.

## Decisions

1. **Name:** `samples/GroundUp.Sample.Web` (it may become the template app).
2. **Router:** TanStack Router (used at Rob's work; typed routes and search parameters suit the list pages in GU-92).
3. **TypeScript:** 7 if the tooling supports it, otherwise the latest 5.x (Requirement 1.4).
4. **Testing manual:** a separate manual for the web app (Requirement 11).
