# Requirements: Web App Sign-In, Session and Tenants (GU-89)

## Context

The web app (GU-86) can show the session but cannot start a sign-in; testers use Swagger on port 5173. GU-87 made the callback return to an app page (`returnUrl`) with `result=signed_in|tenant_picker|no_tenant` or `error=CODE`. This story builds the pages a user needs to get in, choose an organization and get out, so the auth flows can be tested from the browser. Done before GU-88 (order swapped with Rob).

No API change is needed. The endpoints (all exist):

| Endpoint | Used for | Notes |
|---|---|---|
| `POST /auth/login?forceLogin=&returnUrl=` | sign in | `{ redirectUrl }` |
| `POST /auth/signup?returnUrl=` with `{ organizationName? }` | sign up | a name creates an organization; no name is simple sign-up into the default tenant |
| `GET /auth/me`, `GET /auth/me/tenants` | session, organizations | tenants carry `id, name, slug, type, switchable`, plus `current` (standard) or `subdomain` (enterprise) |
| `POST /auth/set-tenant` with `{ tenantId }` | switch | 200 sets the cookie; errors carry `errorCode` (for example `REAUTH_REQUIRED`) |
| `POST /auth/refresh` | refresh the token | 200; 401 `REAUTH_REQUIRED` past the absolute lifetime |
| `POST /auth/organizations` with `{ name }` | create an organization | 201 `{ id, name, slug }`; the session stays where it is |
| `POST /auth/tenants/{id}/leave` | leave | 200 `{ success, currentTenantId }`; 403 `LAST_ADMIN` |
| `POST /auth/logout` | sign out | `{ logoutUrl }` (Keycloak end-session address) |

The token cookie is HttpOnly, so the app shows what `/auth/me` returns (user id, email, name, tenant, roles, issued, expiry and sign-in times), not the raw token.

## Decisions (with Rob)

1. **One Session page.** Signed out: the sign-in and sign-up actions. Signed in: the session, the organizations, and sign-out.
2. **Back to where you started.** Signing in from a page returns to that page. The tenant picker and errors land on the Session page.
3. **A Sign in / Sign out button in the top bar**, next to Requests. The banner's "Sign in" link does the same.
4. **Enterprise organizations** in the list are marked "not switchable here" and show their subdomain; no switch button (GU-69).
5. **Tests:** Vitest for the new endpoint functions and the return logic; pages checked by hand with TESTING.md Part 3.

## Scope

**In scope:** the Session page, the top-bar button, the sign-in return logic, the "sign in again" action, the endpoint functions, tests, and TESTING.md Part 3 (Part 2 updated to use the page).

**Out of scope:**
- **Changing settings** (token lifetime, default tenant): GU-91. Until then the testing manual keeps the API guide's SQL or Swagger steps for them.
- **Subdomain setup** (hosts file, default domain): stays as in the API guide's Part 12; the page shows the host so the results are readable.
- **Invitations and join links:** GU-90 (they use the same return logic).
- **Resetting test users:** the API guide's SQL.
- **Enterprise sign-in:** GU-69.

## Requirements

### Requirement 1: Starting a sign-in and coming back

1. Every sign-in the app starts SHALL send `returnUrl=/session?next=<page>`, where `<page>` is the path and query of the page the user was on (without any `result`, `alreadyMember`, `error`, `idpError` or `next` values). Starting from the Session page itself gives no `next`.
2. `next` SHALL be accepted only when it is a path in this app: it starts with a single `/`, is not a proxied API path (`/api`, `/auth`, `/setup`, `/health`, `/swagger`) and is at most 1000 characters. Anything else is dropped (the user stays on the Session page). This keeps the app from being used to bounce users to another site, in addition to the API's own `returnUrl` rules.
3. On landing, the Session page SHALL:
   - for `result=signed_in`: refresh the session, show a short confirmation, and go to `next` when there is one (replacing the history entry, so Back does not land on the callback outcome)
   - for `result=tenant_picker`: stay, and show the organization list with a prompt to choose
   - for `result=no_tenant`: stay, and show that there is no organization yet, with the create form
   - for `error=CODE`: stay, and show the code with a plain-language explanation for the known codes (`REAUTH_REQUIRED`, `CSRF_STATE_MISMATCH`, `FLOW_ALREADY_CONSUMED`, `FLOW_EXPIRED`, `IDP_ERROR`, `ACCESS_DENIED`, `NO_DEFAULT_TENANT`, `EMAIL_NOT_VERIFIED`, `INVITATION_EMAIL_MISMATCH`, and a generic line for others), with "Sign in again"
4. After acting on the outcome, the Session page SHALL remove the outcome values from its address, so a reload does not show the message again.

### Requirement 2: Signed out

1. The Session page SHALL offer:
   - **Sign in**
   - **Sign in and ask for my password** (`forceLogin=true`), for a different account or after `REAUTH_REQUIRED`
   - **Sign up and create an organization**, with a required organization name (1 to 200 characters)
   - **Sign up** without an organization (joins the default tenant, if one is configured), with a note saying so
2. Each action calls its endpoint with the return URL and then navigates the whole page to `redirectUrl`. While the call runs the button shows progress; a failure shows the error panel.

### Requirement 3: Signed in

1. **Session details:** user id, email, display name, current tenant (name and id, or "No current tenant"), roles, and issued, expiry and sign-in times, each in local time with the UTC value on hover, plus the time remaining.
2. **Refresh token:** calls `POST /auth/refresh` and shows the new expiry.
3. **Organizations:** the list from `GET /auth/me/tenants`, with the current one marked:
   - standard and switchable: **Switch** (`POST /auth/set-tenant`)
   - enterprise or not switchable: "Not switchable here" and the subdomain when given
   - **Leave** on each, behind a confirmation that names the organization; `LAST_ADMIN` and other refusals show in the error panel; leaving the current organization leaves the session with no current tenant
4. **Create an organization:** a name field (1 to 200 characters). On success it shows the new organization with a **Switch to it** action. The current tenant does not change on its own.
5. **Sign out:**
   - **Sign out of this app:** `POST /auth/logout`; the session becomes signed out. Keycloak's own session stays, so the next sign-in may not ask for a password (said on the button's help text)
   - **Sign out everywhere:** `POST /auth/logout`, then the whole page goes to `logoutUrl`; Keycloak returns to the app's home page
6. Every action that changes the user or tenant SHALL call `onSessionChanged` (fresh CSRF token, session refetched).
7. When the session is on the tenant picker (signed in, no current tenant, organizations available), the organization list SHALL be the first thing on the page.

### Requirement 4: Top bar and "sign in again"

1. The top bar SHALL show **Sign in** when signed out and **Sign out** (sign out of this app) when signed in. Sign in starts a normal sign-in returning to the current page.
2. The error panel's "Sign in again" (shown for `REAUTH_REQUIRED`) and the banner's "Sign in" link SHALL start a sign-in directly (with `forceLogin` for `REAUTH_REQUIRED`) returning to the current page, instead of only linking to the Session page.

### Requirement 5: Code structure

1. Endpoint functions in `api/auth.ts` (login, signup, setTenant, refresh, createOrganization, leaveTenant, logout) with typed request and response types in `api/types.ts`.
2. The return logic (building `returnUrl` from the current location, checking `next`) in `features/session/` as plain functions, separate from components.
3. Mutations in `features/session/` hooks; the route file stays thin.

### Requirement 6: Tests

1. Vitest: each endpoint function sends the right method, path, query and body and returns the typed result.
2. Vitest: building `returnUrl` (current path and query kept, outcome values removed, encoding); `next` accepted for app paths and dropped for `//x`, `https://x`, `/api/x`, `/auth/x`, `/authors` (a proxied prefix by Vite's rule), and over-long values.
3. The pages are checked by hand (TESTING.md Part 3).

### Requirement 7: Testing manual

1. TESTING.md Part 2 (return URL) SHALL use the Sign in button instead of Swagger.
2. A new Part 3 SHALL cover, from the web app, the API guide's Parts 2 to 10 and 14.1 to 14.3: sign-up with an organization, login with one membership, a second organization and the picker, the last-TenantAdmin refusal, simple sign-up through the default tenant (the default-tenant setting still set as in the API guide), a member leaving, rejoining, logging in with nowhere to go, creating an organization from a tenantless session, and forced sign-in. Each step says what the page shows.
3. The API guide's Parts 2 to 10 get a line pointing to the web app's Part 3.
