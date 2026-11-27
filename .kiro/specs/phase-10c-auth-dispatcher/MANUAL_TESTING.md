# Auth — Manual Testing Guide

A single script for testing the auth system by hand. **Run Parts 2 to 10 in order, once, from a clean start.** Each part says who you are, what state you start in, and what state you leave behind, so the next part can rely on it.

Use Swagger (or Bruno/Postman) against the sample app, not the browser address bar, except where a step says to open a URL in the browser.

---

## How to Use This Guide

### The test users

| Name | Email | Password | Role in this script |
|---|---|---|---|
| **Sam** | `admin@example.com` | `Admin123!` | The system administrator (SuperAdmin), created by the setup wizard's first-admin step (1.3). Only needed for the optional parts that change System-level settings (11, 14.4). If your setup used a different first admin, use that account. |
| **Alice** | `testuser@example.com` | `Test123!` | Founds `My Test Org`, creates `Second Org`. The only TenantAdmin of both. |
| **Bob** | `member@example.com` | `Test123!` | A regular member (no admin role) who joins through the default tenant, then leaves and rejoins. Joins My Test Org again through a join link in Part 16. |
| **Carol** | `invitee@example.com` | `Test123!` | Has no account until Alice invites her in Part 15. |

### What state you are in after each part

| Part | What it tests | You end as | Alice's orgs | Bob's memberships | `default-tenant-slug` |
|---|---|---|---|---|---|
| 1 | One-time setup (skip if done) | — | — | — | empty |
| 2 | Sign up and create an organization | Alice, in My Test Org | My Test Org | — | empty |
| 3 | Log in (one membership) | Alice, in My Test Org | My Test Org | — | empty |
| 4 | Second org, tenant picker, select a tenant | Alice, in My Test Org | My Test Org, Second Org | — | empty |
| 5 | Last TenantAdmin cannot leave | Alice, in My Test Org | unchanged | — | empty |
| 6 | Default tenant and simple signup | Bob, in My Test Org | unchanged | My Test Org | `my-test-org` |
| 7 | Leave (regular member) | Bob, **no current tenant** | unchanged | none (left) | `my-test-org` |
| 8 | Log in again; rejoin through the default tenant | Bob, in My Test Org | unchanged | My Test Org (same row) | `my-test-org` |
| 9 | Log in with nowhere to go | Bob, **no current tenant** | unchanged | none (left) | empty |
| 10 | Create an organization from a tenantless session | Bob, in Tenantless Org | unchanged | Tenantless Org | empty |
| 11, 12, 13, 14 | Optional: token refresh, subdomains, email verification, session lifetime and forced sign-in | Alice, then the Part 13 users | — | — | empty (Part 12 changes the default *domain*; put it back) |
| 15 | Optional: invitations (needs Parts 2 to 10) | Carol, in My Test Org | unchanged | unchanged | empty |
| 16 | Optional: join links (needs Part 15's Member role) | Bob, in My Test Org | unchanged | Tenantless Org, My Test Org | empty |

### Words used

- **Scoped / in an org:** your `AuthToken` cookie has a `tid` claim, so tenant-scoped endpoints work.
- **Tenantless session / no current tenant:** you are signed in, but your token has no `tid` and no roles. It can call `/auth/me`, `/auth/me/tenants`, `/auth/set-tenant`, `/auth/organizations`, `/auth/logout` and `/auth/tenants/{id}/leave`. Tenant data (for example `GET /api/projects`) returns **403** with `TENANT_REQUIRED`.

### Running requests

- Run requests in Swagger **in the same browser** you use for Keycloak. The browser sends your `AuthToken` cookie automatically. Swagger does not show `Set-Cookie`; use DevTools → Application → Cookies to watch it change.
- **CSRF protection.** A POST, PUT, PATCH or DELETE authenticated by the cookie must carry a CSRF token in the `X-CSRF-Token` header, or it gets **403** `CSRF_VALIDATION_FAILED`. Swagger does this for you: before the first such request it calls `GET /auth/csrf` in the background and adds the header (DevTools → Network shows the extra call once). GET requests and requests with an `Authorization: Bearer` header never need it.
- With a REST client instead, send the cookie yourself: `Cookie: AuthToken=<value>`. For a POST, PUT, PATCH or DELETE, first call `GET /auth/csrf` with that cookie, then send `Cookie: AuthToken=<value>; CsrfToken=<CsrfToken cookie from that response>` and the header `X-CSRF-Token: <token from its body>`. Or use `Authorization: Bearer <token>` instead of the cookie, which needs no CSRF token.
- `GET /api/projects` is used throughout as an example of a tenant-scoped endpoint.

### Recipes (used by many parts)

**Recipe 1 — Log in**
1. `POST /auth/login` in Swagger. Copy `redirectUrl` from the response.
2. Paste it into the address bar of **the same browser**. If Keycloak still has your session, you are signed in immediately without a form (that is SSO, and correct). Otherwise enter the user's email and password.
3. Keycloak redirects to `/auth/callback`. The browser shows the JSON result. Each part says what it should be.

**Recipe 2 — Log out fully**
1. `POST /auth/logout`. Copy `logoutUrl` from the response.
2. Open `logoutUrl` in the same browser. It ends on `http://localhost:<port>/` with a **404 "page can't be found"**. That is normal: the sample app has no page at `/`.
3. Check: `GET /auth/me` returns **401**, and the next Recipe 1 shows the Keycloak **login form**.

Calling `POST /auth/logout` without opening `logoutUrl` clears only the GroundUp cookie. Keycloak keeps its session, and the next login signs you in without a form. Always do both halves when a part says "log out fully". It also avoids Keycloak's *"You are already authenticated as different user"* error when you register a second person.

**Recipe 3 — Set or clear the default tenant**

This is a System-level setting, which only a SuperAdmin may change through the settings API (see Part 11). To keep the main script to Alice and Bob, use the database (pgAdmin/DBeaver, `groundup` database). Replace `'my-test-org'` with `''` to clear it.

```sql
-- 1. Is there already a system-level value row?
SELECT v."Id", v."Value"
FROM "SettingValues" v
JOIN "SettingDefinitions" d ON d."Id" = v."SettingDefinitionId"
WHERE d."Key" = 'auth.application.default-tenant-slug' AND v."ScopeId" IS NULL;

-- 2a. No row returned: insert one
INSERT INTO "SettingValues" ("Id", "SettingDefinitionId", "LevelId", "ScopeId", "Value", "CreatedAt")
SELECT gen_random_uuid(), d."Id", l."Id", NULL, 'my-test-org', now()
FROM "SettingDefinitions" d
CROSS JOIN "SettingLevels" l
WHERE d."Key" = 'auth.application.default-tenant-slug'
  AND lower(l."Name") = 'system';

-- 2b. A row was returned: update it instead
UPDATE "SettingValues" v
SET "Value" = 'my-test-org', "UpdatedAt" = now()
FROM "SettingDefinitions" d
WHERE d."Id" = v."SettingDefinitionId"
  AND d."Key" = 'auth.application.default-tenant-slug'
  AND v."ScopeId" IS NULL;
```

Then **restart the app** (Shift+F5, then F5). Setting values are cached for 15 minutes, so a running app will not see the change.

---

## Before You Start

1. **Start Docker** from PowerShell in `d:\_Rob\github\groundup`:
   ```powershell
   docker compose -f docker-compose.yml -f docker-compose.keycloak.yml up -d
   docker compose -f docker-compose.yml -f docker-compose.keycloak.yml ps
   ```
   Wait about 15 seconds for Keycloak, then check both containers are healthy.
2. **Apply migrations** (first time, or after pulling new ones):
   ```powershell
   dotnet ef database update --project src/GroundUp.Data.Postgres --startup-project samples/GroundUp.Sample --context SampleDbContext
   dotnet ef database update --project src/GroundUp.Auth.Data.Postgres --startup-project samples/GroundUp.Sample --context AuthDbContext
   ```
3. **Rebuild with the current code.** Stop the app (Shift+F5) before building, or the build fails on locked DLLs. Then run it with F5. The browser opens and shows the port in the address bar; note it, you use it throughout (typically `5000`).
4. **Start from a clean state.** If you have run this guide (or tested signup) before:
   - Reset both test users with **Resetting a Test User** at the end of this guide (run it for `testuser@example.com` and for `member@example.com`), including deleting them in Keycloak admin.
   - Make sure `default-tenant-slug` is empty (Recipe 3 with `''`), then restart the app.
   - Log out fully (Recipe 2) so Keycloak shows forms again.

**URLs**
- Swagger: `http://localhost:<port>/swagger/index.html`
- Keycloak admin: http://localhost:8080 (admin / admin)

---

## Part 1: One-Time Setup (Skip If Already Done)

Skip this part if the app starts without Keycloak validation errors in the console. The setup wizard configures the Keycloak connection that every `/auth/*` endpoint needs.

The bootstrap token is already in `appsettings.Development.json`:
```
my-dev-bootstrap-token-at-least-32-chars-long!!
```

### 1.1 Verify setup mode is active
```
GET /setup
```
Expected: `{ "code": "setup_active", ... }`.

### 1.2 Configure Keycloak (once)

Go to http://localhost:8080 → log in as `admin` / `admin` → select the **groundup** realm (top-left dropdown).

1. **Redirect URI.** Keycloak 26 ignores port wildcards. **Clients → groundup-app → Settings → Valid redirect URIs** → add `http://localhost:5000/auth/callback` (use your port; add a few common ones: 5000, 5001, 7295). A realm newly imported from `realm.json` already has ports 5000, 5001, 5117 and 7295 for steps 1 and 2.
2. **Post-logout redirect URI** (needed for Recipe 2). Same page → **Valid post logout redirect URIs** → add `http://localhost:5000/` (match your port). Without it the logout URL returns "Invalid redirect uri".
3. **Save.**
4. **Enable user registration.** **Realm settings → Login** → toggle **User registration** On. (The `realm.json` import is skipped if the realm already existed, so this may be off.)
5. **Create the admin client.** **Clients → Create client**: Client ID `groundup-admin`, Client authentication **ON**, Standard flow **OFF**, Direct access grants **OFF**, Service account roles **ON** → Save. On the **Credentials** tab copy the **Client secret**. On **Service account roles** → **Assign role** → filter by clients → assign `realm-admin`.
6. **Session limits.** **Realm settings → Sessions**: **SSO Session Idle** `30` minutes and **SSO Session Max** `8` hours → Save. `realm.json` sets these for a new realm; an existing realm keeps whatever it had (Keycloak's own default maximum is 10 hours, longer than the app's 8-hour cap). These two values apply to every user of the realm and are the system administrator's responsibility.

### 1.3 Run the wizard

Every call needs the header `Authorization: Bearer my-dev-bootstrap-token-at-least-32-chars-long!!`.

```
POST /setup/app-identity
{ "applicationName": "GroundUp Dev", "defaultDomain": "localhost" }

POST /setup/identity-provider
{ "publicBaseUrl": "http://localhost:8080", "internalBaseUrl": "http://localhost:8080",
  "sharedRealmName": "groundup", "appClientId": "groundup-app" }

POST /setup/keycloak-bootstrap
{ "adminClientId": "groundup-admin", "adminClientSecret": "<secret from 1.2 step 5>" }

POST /setup/first-admin
{ "email": "admin@example.com", "displayName": "Sam Admin", "password": "Admin123!" }

POST /setup/complete
```
Each returns **200**.

### 1.4 Verify
Restart the app. It should start with no Keycloak validation errors in the console.

---

## Part 2: Alice Signs Up and Creates an Organization

**Start:** nobody is logged in; `testuser@example.com` does not exist. **End:** Alice is logged in, scoped to `My Test Org`.

### 2.1 Start the signup
The flow sets an `AuthState` CSRF cookie, so the `redirectUrl` **must be opened in the same browser** that made the call (any tab). Otherwise the callback fails with `CSRF_STATE_MISMATCH`.

```
POST /auth/signup
Content-Type: application/json

{ "organizationName": "My Test Org" }
```
Expected: `200 { "redirectUrl": "http://localhost:8080/realms/groundup/protocol/openid-connect/auth?..." }`. The URL contains `prompt=create`, so Keycloak opens the **registration form** directly (needs Keycloak 26.1+; the dev compose file pins 26.3).

### 2.2 Register
Paste the `redirectUrl` into the same browser. Fill in email `testuser@example.com`, password `Test123!`, first name `Test`, last name `User`, and submit.

### 2.3 Check the callback
The browser shows JSON:
```json
{ "token": "eyJhbGci...", "redirectUrl": "/" }
```
The callback URL should contain only `code`, `state`, `session_state` and `iss`.

### 2.4 Verify
```
GET /auth/me
```
Expected: `{ "userId": "...", "email": "testuser@example.com", "tenantId": "<My Test Org id>", "roles": ["TenantAdmin"] }`. Note the `tenantId`.

Database: an `AuthTenants` row with slug `my-test-org`; an `AuthUsers` row for Alice; an `AuthUserTenants` row; an `AuthRoles` row `TenantAdmin` for that tenant; an `AuthUserRoles` row linking Alice to it.

---

## Part 3: Alice Logs In (One Membership)

**Start:** Alice is logged in. **End:** Alice is logged in again, scoped to `My Test Org`.

1. Log out fully (Recipe 2).
2. Log in as Alice (Recipe 1). You see the Keycloak login form; enter `testuser@example.com` / `Test123!`.
3. Alice has one membership, so the callback picks it for her:
   ```json
   { "token": "eyJhbGci...", "redirectUrl": "/" }
   ```
4. `GET /auth/me` → `tenantId` is `My Test Org`, `roles` is `["TenantAdmin"]`.

---

## Part 4: Second Organization, the Tenant Picker, and Selecting a Tenant

**Start:** Alice, scoped to `My Test Org`. **End:** Alice, scoped to `My Test Org`, owning two organizations.

### 4.1 Create a second organization
You are logged in, so create it directly. There is no Keycloak round trip; you become its TenantAdmin and your session stays where it is.

> Existing users create organizations with this endpoint. `POST /auth/signup` is only for new people: it sends them to Keycloak's registration form, which fails for someone who is already logged in.

```
POST /auth/organizations
Content-Type: application/json

{ "organizationName": "Second Org" }
```
Expected: **201** `{ "id": "...", "name": "Second Org", "slug": "second-org" }`.

```
GET /auth/me/tenants
```
Expected: two tenants. `My Test Org` has `"current": true`; `Second Org` has `"current": false`. Your session did **not** switch.

Error checks (these create nothing):

| Request | Expected |
|---|---|
| `{ "organizationName": "   " }` | 400, name is required |
| A name longer than 200 characters | 400 |

### 4.2 Log in and get the picker
Alice now has two memberships, so login gives her a choice. Run Recipe 1 (no need to log out first; Keycloak signs you in silently). The callback returns:
```json
{
  "requiresTenantSelection": true,
  "tenants": [
    { "id": "...", "name": "My Test Org", ... },
    { "id": "...", "name": "Second Org", ... }
  ]
}
```
There is no `token` field.

### 4.3 Check you are tenantless
While you are on the picker your cookie holds a **tenantless** token (identity only; the Keycloak token is not stored):
1. `GET /auth/me` → `"tenantId": null`, `"roles": []`.
2. `GET /auth/me/tenants` → both orgs, **none** with `"current": true`.
3. `GET /api/projects` → **403** with `TENANT_REQUIRED`.

### 4.4 Select a tenant
```
POST /auth/set-tenant
Content-Type: application/json

{ "tenantId": "<My Test Org id>" }
```
Expected: **200**. Your `AuthToken` cookie is replaced with a token scoped to `My Test Org`; your original sign-in time is kept.

1. `GET /auth/me` → `tenantId` is `My Test Org`, `roles` is `["TenantAdmin"]`.
2. `GET /auth/me/tenants` → `My Test Org` has `"current": true`, `Second Org` has `"current": false`.
3. `GET /api/projects` → **200** (an empty page is fine).

---

## Part 5: The Last TenantAdmin Cannot Leave

**The rule:** every tenant must keep at least one active TenantAdmin. A regular member can always leave; an admin can leave only if another active admin remains.

**Start:** Alice, scoped to `My Test Org`. **End:** unchanged.

Alice is the only TenantAdmin of both organizations.
```
POST /auth/tenants/<Second Org id>/leave
```
Expected: **403** `{ "error": "You are the only TenantAdmin of this organization. Assign another TenantAdmin before leaving.", "errorCode": "LAST_ADMIN" }`.

`GET /auth/me/tenants` still lists both orgs, and your session is unchanged.

---

## Part 6: Default Tenant and Simple Signup (Bob)

Simple signup registers a new person **without** creating an organization. They join the configured default tenant.

**Start:** Alice, scoped to `My Test Org`. **End:** Bob, scoped to `My Test Org` as a regular member.

### 6.1 Set the default tenant
Recipe 3 with `'my-test-org'`. Restart the app.

### 6.2 Switch to a new person
Log out fully (Recipe 2). Alice's session must be gone, or Keycloak refuses to register someone else.

### 6.3 Sign up as Bob
```
POST /auth/signup
```
No body. Expected: `200 { "redirectUrl": "..." }`. Open it in the same browser and register `member@example.com`, password `Test123!`, first name `Bob`, last name `Member`.

The callback returns:
```json
{ "token": "eyJhbGci...", "redirectUrl": "/" }
```

### 6.4 Verify
1. `GET /auth/me` → `tenantId` is `My Test Org`, `"roles": []` (Bob is a regular member, not an admin).
2. `GET /auth/me/tenants` → one tenant, `My Test Org`, `"current": true`. For a standard tenant the entry shows `"type": "standard"` and `"switchable": true`. (Enterprise tenants would show `"type": "enterprise"` and `"switchable": false`; none exist in this script.)

---

## Part 7: A Regular Member Leaves (Bob)

**Start:** Bob, scoped to `My Test Org`. **End:** Bob, signed in with **no current tenant**, no memberships.

What should happen:
- Leaving the tenant you are working in leaves you signed in with no current tenant. You are never moved into another organization automatically.
- Leaving removes your roles in that tenant, so rejoining starts with none.
- The response is `{ "success": true, "currentTenantId": ... }`, where `currentTenantId` is `null` when you have no current tenant.

### 7.1 Leave
1. **Save a copy of Bob's current token** (for 7.4). DevTools → Application → Cookies → copy the `AuthToken` value. Check it works from a terminal, outside the browser, so no cookie is sent:
   ```
   curl.exe -i http://localhost:<port>/auth/me -H "Authorization: Bearer <saved token>"
   ```
   Expected: `HTTP/1.1 200`.
2. `GET /auth/me/tenants` → note the `id` of `My Test Org`.
3. `POST /auth/tenants/<id>/leave`.

Expected: **200** `{ "success": true, "currentTenantId": null }`. Bob can leave because `My Test Org` still has an active admin (Alice).

### 7.2 Verify the session
1. `GET /auth/me` → **200**, `"tenantId": null`, `"roles": []`. Bob is still signed in.
2. `GET /auth/me/tenants` → `[]`.
3. `GET /api/projects` → **403** `TENANT_REQUIRED`.

### 7.3 Verify the database
```sql
SELECT ut."Id", ut."IsActive"
FROM "AuthUserTenants" ut
JOIN "AuthUsers" u ON u."Id" = ut."UserId"
JOIN "AuthTenants" t ON t."Id" = ut."TenantId"
WHERE lower(u."Email") = 'member@example.com' AND t."Slug" = 'my-test-org';
```
Expected: one row, `IsActive = false`. **Note the row `Id`**; Part 8 checks it is reused.

```sql
SELECT count(*) FROM "AuthUserRoles" ur
JOIN "AuthUsers" u ON u."Id" = ur."UserId"
WHERE lower(u."Email") = 'member@example.com';
```
Expected: `0`.

### 7.4 The saved token no longer works
Run the same `curl.exe` command as in 7.1 step 1, with the token saved **before** leaving.

Expected: `HTTP/1.1 401`. Leaving revoked every token Bob held for `My Test Org`, not only the browser cookie. His current (tenantless) session in the browser is not affected. A token issued after rejoining (Part 8) works again.

> Revocation is kept in memory by the app instance that handled the leave. With one instance (this guide) it is immediate. With several instances, the others accept the old token until it expires (at most the token lifetime, default 15 minutes) until the caching module provides a shared store. Restarting the app forgets revocations, so run 7.4 without a restart in between.

---

## Part 8: Bob Logs In Again and Rejoins Through the Default Tenant

**Start:** Bob, tenantless, no memberships; default tenant is still `my-test-org`. **End:** Bob, scoped to `My Test Org`.

1. Log out fully (Recipe 2).
2. Log in as Bob (Recipe 1; enter `member@example.com` / `Test123!`).
3. Bob has no memberships but a default tenant is set, so he is put back in `My Test Org`. The callback returns:
   ```json
   { "token": "eyJhbGci...", "redirectUrl": "/" }
   ```
4. `GET /auth/me` → `tenantId` is `My Test Org`, `"roles": []`.
5. Run the first query from 7.3 again. Expected: **the same single row** (same `Id` as in Part 7), now `IsActive = true`. A user has one membership row per tenant; rejoining reactivates it instead of adding a second one.

---

## Part 9: Logging In With Nowhere to Go (Bob)

Before the tenantless-session work this was a `403 ACCESS_DENIED`. Now the user is signed in with no current tenant.

**Start:** Bob, scoped to `My Test Org`. **End:** Bob, signed in with no current tenant, no memberships; default tenant empty.

1. `POST /auth/tenants/<My Test Org id>/leave` → **200** `{ "success": true, "currentTenantId": null }`.
2. Clear the default tenant: Recipe 3 with `''`. Restart the app.
3. Log out fully (Recipe 2).
4. Log in as Bob (Recipe 1).
5. The callback returns **200**:
   ```json
   { "requiresTenantSelection": false, "tenants": [] }
   ```
   There is **no** `token` field, but the `AuthToken` cookie is set and holds a tenantless token.
6. Verify:
   - `GET /auth/me` → `"tenantId": null`, `"roles": []`.
   - `GET /auth/me/tenants` → `[]`.
   - `GET /api/projects` → **403** `TENANT_REQUIRED`.

> If `default-tenant-slug` names a tenant that does not exist (or is inactive), login returns `403 ACCESS_DENIED` instead. That is a configuration fault, not "no default". It is covered by an automated test.

---

## Part 10: Creating an Organization From a Tenantless Session (Bob)

**Start:** Bob, no current tenant, no memberships. **End:** Bob, scoped to `Tenantless Org` as its TenantAdmin.

1. Create it:
   ```
   POST /auth/organizations
   Content-Type: application/json

   { "organizationName": "Tenantless Org" }
   ```
   Expected: **201** `{ "id": "...", "name": "Tenantless Org", "slug": "tenantless-org" }`.
2. Creating an organization does not change your session: `GET /auth/me` still shows `"tenantId": null`, and `GET /api/projects` is still **403**.
3. Enter it:
   ```
   POST /auth/set-tenant
   Content-Type: application/json

   { "tenantId": "<id from step 1>" }
   ```
   Expected: **200**.
4. `GET /auth/me` → `tenantId` is `Tenantless Org`, `"roles": ["TenantAdmin"]`. `GET /api/projects` → **200**.

> A tenantless session lasts as long as any token (`TokenExpirationMinutes`) and is never refreshed. If it expires, sign in again; Keycloak's session is still active, so that is silent.

---

## Part 11 (Optional): Token Refresh and Session Timeout Settings

The timeouts are runtime settings, changed through the settings API with no restart: `auth.session.token-lifetime-minutes` (2 to 120, default 15; also the inactivity timeout) and `auth.session.absolute-lifetime-minutes` (10 to 1440, default 480). Each can be set at the System level and, shorter only, per tenant. `appsettings` values are only a fallback when settings cannot be read.

**Who may change settings (GU-77):**

| Change | Allowed for | Others get |
|---|---|---|
| A System value | SuperAdmin (Sam) | **403** |
| A tenant's value | SuperAdmin, or a TenantAdmin (or a user with `settings.manage.tenant`) **of that tenant**, while it is their current tenant | **403** |
| Levels, groups, definitions (create, update, delete) | SuperAdmin | **403** |
| List settings, groups, definitions (`GET /api/settings`, `GET /api/settings/groups`, `GET /api/settings/definitions`) | `settings.read` (SuperAdmin and TenantAdmin have it) | **403** |
| One effective value (`GET /api/settings/{key}`), the levels (`GET /api/settings/levels`) | anyone (the app itself reads these during login) | — |

With no sign-in, a change returns **401**. Secret values are always masked, for every user.

1. **Find the level ids.** `GET /api/settings/levels` → note the `id` of `System` and of `Tenant`.
2. **Alice cannot change the System value.** Log out fully, log in as Alice (Recipe 1), then `POST /auth/set-tenant` to `My Test Org` (as in 4.4). `PUT /api/settings/auth.session.token-lifetime-minutes` with `{ "value": "2", "levelId": "<System id>" }`. Expected: **403**.
3. **Sam shortens the token lifetime.** Log out fully, log in as Sam (Recipe 1). Same `PUT` as in step 2. Expected: **200**; note `data.id` for step 7. Log out fully and log in as Alice again, scoped to `My Test Org`. Then `POST /auth/refresh`: the new `AuthToken` expires in 2 minutes (DevTools → Application → Cookies, **Expires**).
4. **Refresh.** Refresh only happens in a window: the token must be **more than half its lifetime old but not yet expired**, so **between 1 and 2 minutes** after it was issued. Wait about 70 seconds, then `GET /auth/me`. Expected: **200** and a **new** `AuthToken` cookie in the response headers (Network → Response Headers → `Set-Cookie`).

   If you wait **past 2 minutes** the token has expired and cannot be refreshed: `GET /auth/me` returns **401** and you log in again (Recipe 1). That is correct; you missed the window.
5. **A tenant can only shorten.** Still as Alice in `My Test Org`, using its id from `GET /auth/me/tenants`: `PUT /api/settings/auth.session.token-lifetime-minutes` with `{ "value": "5", "levelId": "<Tenant id>", "scopeId": "<My Test Org id>" }`. Expected: **400**, the tenant value cannot be longer than the System value (2). With `"value": "2"` it is accepted (**200**; note `data.id`).
6. **Only the current tenant, and the bounds.** Same `PUT` with `"value": "2"` and the `scopeId` of `Second Org` (Alice is its TenantAdmin too, but it is not her current tenant). Expected: **403**. With `My Test Org` again: `"value": "1"` → **400** (minimum 2), and `PUT /api/settings/auth.session.absolute-lifetime-minutes` with `{ "value": "1", "levelId": "<Tenant id>", "scopeId": "<My Test Org id>" }` → **400** (minimum 10).
7. **Put it back.** As Alice: `DELETE /api/settings/values/<id>` for the tenant value. Then as Sam: the same for the System value (as Alice it returns **403**). The defaults apply again on the next token.

---

## Part 12 (Optional): Subdomains

Not changed by the tenantless-session work. It pins a user to an organization by host name: `<slug>.<default-domain>`. Run it as Alice, who belongs to two organizations.

**Why not `*.localhost`?** Browsers will not store a cookie scoped to `.localhost` for a subdomain such as `my-test-org.localhost`, so the login appears to work but `/auth/me` returns 401. Use a made-up two-part domain. The examples use `groundup.test` (`.test` is reserved for this).

**Why this breaks the rest of the guide:** the auth cookie is scoped to the default domain. Once it is `groundup.test`, a cookie cannot be set on plain `localhost`. Do this part last, and switch back at the end.

The examples use the slugs `my-test-org` and `second-org`. Use your own if they differ (check `AuthTenants.Slug`). The host must be exactly `<slug>.groundup.test`.

### 12.1 Set up (once)

1. **Hosts file.** Open `C:\Windows\System32\drivers\etc\hosts` as Administrator and add:
   ```
   127.0.0.1  my-test-org.groundup.test
   127.0.0.1  second-org.groundup.test
   ```
2. **Keycloak.** Admin → groundup realm → Clients → groundup-app → Settings. Add the following for each of the two hosts (use your port). Match the slashes exactly; Keycloak compares the text:
   - **Valid redirect URIs:** `http://my-test-org.groundup.test:5000/auth/callback`
   - **Valid post logout redirect URIs:** `http://my-test-org.groundup.test:5000/` (with the trailing `/`)
   - **Web origins:** `http://my-test-org.groundup.test:5000`
   - Then the same three for `second-org.groundup.test`.
3. **Default domain.** Use Recipe 3 (SQL, then restart), but for the key `auth.application.default-domain` and the value `groundup.test`. To check it took effect:
   ```sql
   SELECT v."Value" FROM "SettingValues" v
   JOIN "SettingDefinitions" d ON d."Id" = v."SettingDefinitionId"
   WHERE d."Key" = 'auth.application.default-domain' AND v."ScopeId" IS NULL;
   ```
   Expected: `groundup.test`. Restart the app (Shift+F5, then F5).

> **Use only `*.groundup.test` hosts from here on.** Opening Swagger on `localhost` or `*.localhost` now fails to keep a cookie.

### 12.2 Host-pinned login

`/auth/login` is a **POST**, so typing its address in the browser gives **HTTP 405**. Use Swagger served from the subdomain:
1. Open `http://my-test-org.groundup.test:5000/swagger/index.html`. Check the address bar.
2. Run `POST /auth/login`. Copy the `redirectUrl`. Its `redirect_uri` parameter must contain `my-test-org.groundup.test%3A5000`. If it says `localhost`, you are on the wrong page.
3. Open the `redirectUrl` in the same browser and sign in as Alice.

Expected callback: `{ "token": "...", "redirectUrl": "/" }`. There is **no picker**, even though Alice belongs to two organizations, because the host identifies the organization. A user who is not a member would get **403**.

`GET /auth/me` → `tenantId` is `My Test Org`. In DevTools → Application → Cookies, `AuthToken` is listed with domain `.groundup.test`.

### 12.3 Host and token mismatch

Alice's token is for `my-test-org`. Now call the **other** organization's host. The cookie covers every `*.groundup.test` host, so it is sent there.

Type this address straight into the browser (it is a GET, so the address bar works):
```
http://second-org.groundup.test:5000/api/projects
```
Expected: **409** with this body:
```json
{ "success": false, "message": "Your current session is scoped to a different tenant. Please switch tenants via POST /auth/set-tenant.", "errorCode": "TENANT_SWITCH_REQUIRED", "statusCode": 409 }
```
(A user who is not a member of `second-org` would get **403** instead.)

> The check runs on every path except `/auth/*`, so **the Swagger page on the other host is blocked too** (`http://second-org.groundup.test:5000/swagger/index.html` returns the same 409). Do not try to run this step from Swagger on that host.

### 12.4 Resolve it without logging in again

`/auth/*` is exempt from the check, so switch from the Swagger page you already have open on the `my-test-org.groundup.test` host (the page itself does not need to reload):
```
POST /auth/set-tenant
{ "tenantId": "<Second Org id>" }
```
Expected: **200**. The cookie is replaced with a token scoped to `Second Org`, and it applies to every `*.groundup.test` host.

Now open `http://second-org.groundup.test:5000/api/projects` in the address bar again. Expected: **200** (an empty page of projects is fine).

The reverse now holds: `http://my-test-org.groundup.test:5000/api/projects` returns 409 until you switch back with `POST /auth/set-tenant` for `My Test Org`.

### 12.5 Put it back

Set `auth.application.default-domain` back to `localhost` (the same SQL with that value), restart the app, and go back to using `http://localhost:<port>`. Your hosts-file and Keycloak entries can stay.

---

## Part 13 (Optional): Email Verification

Tests what happens when Keycloak requires new users to verify their email address before they can sign in. **Verified end to end** (real email through Gmail SMTP, Keycloak 26.3): the same-browser path redirects back to the app with a token; opening the link in a different browser verifies the address but cannot continue the sign-up (see 13.2 and 13.4).

**How it works.** The app never looks at whether an email is verified. Keycloak enforces it: with "Verify email" on, Keycloak holds the sign-in on a "verify your email" page and only redirects to `/auth/callback` after the user clicks the link in the email. The app creates the user, organization and membership **at the callback**, so an unverified address never gets an account.

**Run this after Part 12**, with `default-domain` back at `localhost` and `default-tenant-slug` empty (the state Part 10 left). It turns verification on for the whole realm, so Part 13.7 turns it off again.

### 13.1 Set up

1. **Choose how email is delivered.** Keycloak sends the verification email itself, so it only needs an SMTP server. Nothing in this repository configures one: the setting lives in Keycloak's own database (it survives restarts but is wiped by `docker compose down -v`), so you have to enter it again after a full reset.

   **Option A: a local mail catcher (nothing leaves your PC).** In PowerShell:
   ```powershell
   docker run -d --name groundup-mailpit -p 8025:8025 -p 1025:1025 axllent/mailpit
   ```
   Its inbox is at http://localhost:8025. Use the settings in step 2 column A.

   **Option B: real email to a real inbox.** Two different addresses are involved:
   - **The sender:** a mail account Keycloak logs in to in order to send the emails. Its address goes in the "From" and "Username" fields below. Gmail is the simplest.
   - **The recipient:** the address a person types on the sign-up form. The verification email is delivered there.

   Steps for the sender account:
   1. Turn on 2-Step Verification for the Google account, then create an **App password** (Google Account → Security → App passwords). A normal password will not work. You can revoke the app password at any time; do so when you finish testing.
   2. Use the settings in step 2 column B, with the app password.

   For the recipient, test users must have addresses that really receive mail. You do not need extra accounts: Gmail delivers `yourname+verify@gmail.com` to `yourname@gmail.com`, and Keycloak treats each plus-address as a different user. The recipient can be a second mailbox you own (cleaner) or the sender's own inbox. In the rest of this part, replace `verify@example.com` with `yourname+verify@gmail.com`, `unverified@example.com` with `yourname+unverified@gmail.com`, and `verify2@example.com` with `yourname+verify2@gmail.com`.

   Other providers work the same way (SendGrid, Mailgun, Amazon SES, Microsoft 365). They usually need a verified sender address or domain, and some block plain username/password sign-in, so Gmail with an app password is the least work.
2. **Tell Keycloak to use it.** Keycloak admin → **groundup** realm → **Realm settings → Email** tab:

   | Field | A: Mailpit | B: Gmail |
   |---|---|---|
   | From | `noreply@groundup.local` | your Gmail address (Gmail replaces any other sender) |
   | Host | `host.docker.internal` (how Keycloak, inside Docker, reaches your PC) | `smtp.gmail.com` |
   | Port | `1025` | `587` |
   | Enable StartTLS | off | **on** |
   | Enable SSL | off | off |
   | Authentication | off | **on** |
   | Username / Password | (none) | your full Gmail address / the app password |

   Save, then click **Test connection**. If it reports a missing address, give the Keycloak admin user an email (master realm → Users → admin → Email) and try again. For option B, check the inbox (and the spam folder) for the test message.

   > **Keep the app password out of the repository.** Enter it only in the Keycloak admin console. Do not export the realm with it and commit the file.
3. **Mark your existing test users as verified**, or Keycloak will ask Alice and Bob to verify the next time they sign in (they registered while verification was off, so Keycloak recorded them as unverified). For each of them: **Users** → search the email → **Details** → toggle **Email verified** On → Save.
4. **Require verification.** **Realm settings → Login** tab → toggle **Verify email** On.

### 13.2 A new user verifies right away (normal path)

You need a user who does not exist yet: `verify@example.com` (option A), or your plus-address such as `yourname+verify@gmail.com` (option B).

1. `POST /auth/signup` with `{ "organizationName": "Verified Org" }`. Open the `redirectUrl` in the browser.
2. Register `verify@example.com`, password `Test123!`, first name `Vera`, last name `Verify`.
3. **Expected:** Keycloak does **not** redirect to the app. It shows a page telling you to verify your email address.
4. **Check the app has not created anything yet.** In the database:
   ```sql
   SELECT count(*) FROM "AuthUsers"   WHERE lower("Email") = 'verify@example.com';   -- expect 0
   SELECT count(*) FROM "AuthTenants" WHERE "Slug" = 'verified-org';                -- expect 0
   SELECT "FlowType", "Status", "ExpiresAt" FROM "AuthFlowStates" ORDER BY "CreatedAt" DESC LIMIT 1;  -- Pending
   ```
5. Open the inbox: Mailpit at http://localhost:8025 (option A), or your real mailbox (option B; check spam too). **Expected:** one email addressed to the user you registered, from the Keycloak realm. Click its verification link **in the same browser**.
6. Keycloak first shows a **confirm page** ("click here to proceed with verification"); click it. This extra click is Keycloak's own design, so that mail scanners cannot verify an address just by fetching the link. **Expected, when the sign-up was started in this same browser:** Keycloak continues the sign-in and the browser lands on `/auth/callback` with:
   ```json
   { "token": "eyJhbGci...", "redirectUrl": "/" }
   ```
7. `GET /auth/me` → `"email": "verify@example.com"`, a `tenantId` for **Verified Org**, `"roles": ["TenantAdmin"]`. The database queries from step 4 now return 1 for the user and 1 for the tenant.

   **If instead you see only "Your email address has been verified" and nothing continues,** Keycloak had no original sign-in session to resume (the link was opened in a different browser, or the session expired). The address is verified, but `/auth/callback` was never reached, so **no account exists yet**. This is the case 13.4 covers: sign in with `POST /auth/login`, then create the organization. To test the resume path, start the sign-up and open the email link in the **same** browser.

### 13.3 An unverified user cannot sign in

1. Log out fully (Recipe 2).
2. `POST /auth/signup` with `{ "organizationName": "Never Verified Org" }`, open the `redirectUrl`, and register `unverified@example.com` (password `Test123!`). **Do not click the email link.**
3. Instead, start a normal login: `POST /auth/login`, open its `redirectUrl`, and sign in as `unverified@example.com`.
4. **Expected:** Keycloak again shows the verify-your-email page (with a "click here to re-send the email" option). It does **not** reach the callback. The database still has **no** row for `unverified@example.com` and none for `never-verified-org`.
5. Click the re-send link. **Expected:** a second email in Mailpit. Leave it unverified for 13.4.

### 13.4 The link is used after the sign-up flow can no longer continue

The signup flow is only valid for `FlowStateExpirationMinutes` (10 by default). A user may also open the email on another device. In both cases Keycloak verifies the address, but the app's original sign-up can't finish.

1. Use the unverified user from 13.3. Open the verification link from Mailpit **in a private window** (a different browser session), or wait more than 10 minutes and open it in the original browser. To avoid waiting, you can set `"FlowStateExpirationMinutes": 1` in `appsettings.Development.json`, restart the app, and repeat 13.3 first (restore it to `10` afterwards).
2. **Expected:** Keycloak says the address is verified. If it sends the browser back to `/auth/callback`, the callback returns an error such as `FLOW_EXPIRED` (flow has expired) or `CSRF_STATE_MISMATCH` (different browser), and **no account is created**.
3. Now log in normally: log out fully (Recipe 2), then `POST /auth/login` and sign in as `unverified@example.com`. This time Keycloak lets you through.
4. **Expected callback:** the user has no organization and there is no default tenant, so you are signed in with no current tenant:
   ```json
   { "requiresTenantSelection": false, "tenants": [] }
   ```
5. Create the organization now, from the tenantless session (as in Part 10): `POST /auth/organizations` `{ "organizationName": "Never Verified Org" }` → 201, then `POST /auth/set-tenant` → 200.

> **Known behavior to decide on:** the organization name typed at signup is lost when the sign-up flow cannot finish, so the user must enter it again in step 5. That is acceptable for now, but a later change could keep a pending signup until verification.

### 13.5 Already-verified users are not affected

Log out fully, then log in as Alice (marked verified in 13.1). **Expected:** no verification prompt, and Part 3 behaves exactly as before.

### 13.6 Simple signup with verification (optional)

Set the default tenant (Recipe 3, `my-test-org`), restart, log out fully, then `POST /auth/signup` with no body and register `verify2@example.com`. **Expected:** the same verify-your-email page, no account yet. After clicking the link in the same browser, the callback returns `{ "token": ... }` and the user is a regular member of `My Test Org` (`"roles": []`). Clear the default tenant afterwards (Recipe 3 with `''`) and restart.

### 13.7 Turn it off again

1. Keycloak → **Realm settings → Login** → toggle **Verify email** Off.
2. Stop the mail catcher when you are done: `docker rm -f groundup-mailpit`.
3. If you changed `FlowStateExpirationMinutes`, set it back to `10` and restart.

---

## Part 14 (Optional): Session Lifetime and Forced Sign-In

Checks that the session is counted from when the user really typed their password (Keycloak's `auth_time`), that a silent single sign-on does not restart it, and that the app makes Keycloak ask for the password again when the session is too old. Run it as Alice, scoped to `My Test Org`, with the timeouts at their defaults.

**How to read `auth_time`.** In DevTools → Application → Cookies, copy the `AuthToken` value and decode it (paste into https://jwt.io, or in PowerShell: `[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($t.Split('.')[1].Replace('-','+').Replace('_','/')).PadRight((($t.Split('.')[1].Length+3) -band -4),'=')))` with `$t` set to the token). `auth_time` is Unix seconds; convert with `[DateTimeOffset]::FromUnixTimeSeconds(<value>).ToLocalTime()`.

### 14.1 The login URL carries `max_age`
`POST /auth/login`. **Expected:** the `redirectUrl` contains `max_age=28800` (8 hours, the default absolute lifetime) and no `prompt`.

### 14.2 A silent sign-on keeps the original `auth_time`
1. Log out fully (Recipe 2), then log in as Alice **with the password** (Recipe 1) and select `My Test Org` if asked. Note `auth_time`: it is the moment you submitted the password, so it can be a minute or two earlier than the token's `iat` (for example if you chose a tenant first).
2. Wait 2 minutes. Run Recipe 1 again **without logging out fully** (a plain `POST /auth/logout` is fine; opening `logoutUrl` would end Keycloak's session and defeat the test). Keycloak signs you in **without** a form.
3. **Expected:** the new `AuthToken` has the **same** `auth_time` as in step 1, with a later `iat`. A user with two organizations lands on the picker, so this token has no `tid`; selecting a tenant keeps the same `auth_time` too.

### 14.3 `forceLogin` asks for the password
1. `POST /auth/login?forceLogin=true`. **Expected:** the `redirectUrl` contains `prompt=login` and `max_age`.
2. Open it. **Expected:** Keycloak shows the **login form** even though its session is alive. Sign in.
3. **Expected:** `auth_time` in the new `AuthToken` is now (it moved forward). This is what a client does after a `401 REAUTH_REQUIRED`.

### 14.4 `max_age` makes an old session ask for the password
Uses the shortest allowed absolute lifetime (10 minutes), so it takes about 11 minutes.

1. Log in as Sam (System values need a SuperAdmin; see Part 11) and set the System values through the settings API (Part 11 shows how to get the System level id): `PUT /api/settings/auth.session.token-lifetime-minutes` with `{ "value": "2", "levelId": "<System id>" }`, then `PUT /api/settings/auth.session.absolute-lifetime-minutes` with `{ "value": "10", "levelId": "<System id>" }`. Note each response's `data.id`. No restart is needed.
2. Log out fully, then log in as Alice with the password (`POST /auth/login?forceLogin=true`, Recipe 1 step 2). `POST /auth/login` now returns `max_age=600`.
3. Wait 11 minutes. Run Recipe 1 (`POST /auth/login`, no `forceLogin`).
4. **Expected:** Keycloak shows the **login form**, even though its own SSO session (30 minutes idle, 8 hours maximum) is still alive, because the last sign-in is older than `max_age`. After you sign in, `auth_time` is now.
5. **Put it back:** as Sam, `DELETE /api/settings/values/<id>` for both ids from step 1.

> If a stale session ever reaches the callback anyway (for example a clock difference), the callback returns `401 { "errorCode": "REAUTH_REQUIRED" }` and writes no cookie. The automated tests cover that path.

**Record the result** (Keycloak version and whether 14.2 to 14.4 matched) in the Phase 10C spec's tasks file, task 7.2.

> **Worth knowing:** with "Verify email" off, Keycloak lets anyone register with an email address they do not own, and the app accepts it. The app does not check `email_verified` itself. If verified email should be a hard rule for the framework, regardless of how the realm is configured, the app should check the claim as well. That is not built.

---

## Part 15 (Optional): Invitations (Alice Invites Carol)

Alice invites Carol, who has no account, to My Test Org. Carol registers from the invitation and lands in the organization. Start after Part 10 (or any time Alice is the TenantAdmin of My Test Org).

The sample app has no email sender and no accept page. In Development the default sender writes the accept link to the app console instead of emailing it, and you call the accept endpoints from Swagger.

### 15.1 Set up (once)
1. **Add a Member role to My Test Org.** New organizations only get TenantAdmin, and there is no roles API yet, so use the database:
   ```sql
   INSERT INTO "AuthRoles" ("Id", "Name", "Description", "RoleType", "TenantId", "IsSystem", "CreatedAt")
   SELECT gen_random_uuid(), 'Member', 'Regular member', 1, t."Id", false, now()
   FROM "AuthTenants" t WHERE t."Slug" = 'my-test-org';

   SELECT r."Id" FROM "AuthRoles" r JOIN "AuthTenants" t ON t."Id" = r."TenantId"
   WHERE t."Slug" = 'my-test-org' AND r."Name" = 'Member';
   ```
   Note the Member role id.
2. **Set the accept page URL** (System level, so as Sam; Part 11 shows how to get the System level id): `PUT /api/settings/auth.invitations.accept-url` with `{ "value": "http://localhost:5000/invite/{token}", "levelId": "<System id>" }` (use your port). Until it is set, `POST /api/invitations` returns `INVITATION_URL_NOT_CONFIGURED`.

### 15.2 Alice sends the invitation
1. Log out fully (Recipe 2), then log in as Alice (Recipe 1) and make sure you are in My Test Org (`POST /auth/set-tenant` if you get the picker).
2. `POST /api/invitations` with `{ "email": "invitee@example.com", "roleId": "<Member id>" }`. **Expected:** 201, `status` 0 (Pending), and no token anywhere in the response.
3. The app console shows `Invitation ... for invitee@example.com (not sent: no email sender registered). Accept link: http://localhost:5000/invite/<token>`. Copy the token.
4. `GET /api/members`. **Expected:** Carol is listed with `status` 2 (Pending) and role Member.

### 15.3 Carol accepts and registers
1. `POST /auth/invitations/lookup` with `{ "token": "<token>" }`. **Expected:** `usable: true`, `organizationName: "My Test Org"`, `roleName: "Member"`. No sign-in is needed.
2. Log out fully (Recipe 2). Alice's Keycloak session must end, or Keycloak refuses to register a second person.
3. `POST /auth/invitations/accept` with `{ "token": "<token>" }`. **Expected:** the `redirectUrl` contains `prompt=create` and `login_hint=invitee%40example.com`, because Carol has no Keycloak account.
4. Open it. **Expected:** the Keycloak **registration** form, with the email filled in. Register with password `Test123!`.
5. **Expected:** the callback succeeds. `GET /auth/me` shows Carol in My Test Org with the Member role.

### 15.4 The link only works once
1. `POST /auth/invitations/lookup` with the same token. **Expected:** `usable: false`, `reason: "accepted"`.
2. `POST /auth/invitations/accept` with the same token. **Expected:** **410** `INVITATION_ALREADY_USED`.

### 15.5 Revoking an invitation
1. Log out fully and log in as Alice in My Test Org. `POST /api/invitations` with `{ "email": "nobody@example.com", "roleId": "<Member id>" }`. Note `data.id` and the token from the console.
2. `POST /api/invitations/<id>/revoke`. **Expected:** 200.
3. **Expected:** `GET /api/members` no longer lists `nobody@example.com`; `POST /auth/invitations/lookup` with its token returns `reason: "revoked"`.

**Resend** works the same way (`POST /api/invitations/<id>/resend`): the console shows a new link, and the old token's lookup returns `not_found`.

---

## Part 16 (Optional): Join Links (Bob Rejoins My Test Org)

Alice creates a shareable link; Bob, who left My Test Org in Part 9, uses it to join again with the Member role. Needs the Member role from Part 15.1.

### 16.1 Alice creates a link
1. As Alice in My Test Org: `POST /api/join-links` with `{ "roleId": "<Member id>", "maxUses": 5 }`. **Expected:** 201 with `data.token` and `data.link`. This is the only time the token is shown.
2. `GET /api/join-links`. **Expected:** the link is listed with `useCount` 0 and no token.
3. `POST /api/join-links/<id>/regenerate`. **Expected:** a new `data.token`. `POST /auth/join-links/lookup` with the **old** token returns `reason: "not_found"`; with the new one, `usable: true`. Use the new token from here on.

### 16.2 Bob joins
1. Log out fully (Recipe 2).
2. `POST /auth/join-links/join` with `{ "token": "<new token>" }`. Open the `redirectUrl` and sign in as Bob.
3. **Expected:** the callback succeeds and Bob is signed in to My Test Org, with no picker even though he also belongs to Tenantless Org. `GET /auth/me` shows the Member role. `GET /auth/me/tenants` lists both organizations.
4. As Alice, `GET /api/join-links` shows `useCount` 1.

### 16.3 Revoking a link
1. As Alice: `POST /api/join-links/<id>/revoke`. **Expected:** 200.
2. **Expected:** `POST /auth/join-links/lookup` returns `reason: "revoked"`, and `POST /auth/join-links/join` returns **410** `JOIN_LINK_NO_LONGER_VALID`. Bob is still a member (`GET /api/members`).

---

## What Automated Tests Cover (Not Repeated Here)

These need extra setup (several organizations, a second admin) or conditions that are awkward to create by hand. They run against a real database:

- Leaving your current tenant while you belong to 0, 1 or 3 other tenants: the session becomes tenantless, the other memberships stay, and nothing moves you automatically.
- Leaving a tenant other than the one your session is on: no cookie change.
- One of two admins leaves (allowed), then the remaining admin cannot (the departed admin no longer counts).
- Leaving removes roles in that tenant only.
- `set-tenant`, `/auth/refresh` and the callback return `401 REAUTH_REQUIRED` once the original sign-in is older than the absolute session lifetime (System or the tenant's, whichever applies).
- A token never expires later than the original sign-in plus the absolute lifetime.
- CSRF on the cookie path: no token, a token without its cookie, or another user's token gets 403; Bearer requests and GETs are never blocked.
- An ID token with a bad signature is rejected (`ID_TOKEN_INVALID`) and no cookie is written.
- A configured default tenant that does not exist returns 403 at login.
- A Keycloak access token sent as a Bearer header is treated as an identity-provider principal, not a GroundUp user.

---

## Troubleshooting

| Problem | Solution |
|---|---|
| `relation "SettingDefinitions" does not exist` | Run the EF migrations (Before You Start) |
| App starts with Keycloak validation errors | Run the setup wizard (Part 1) |
| `PUT` or `DELETE` on `/api/settings/...` returns 403 | System values need a SuperAdmin (Sam). A tenant value needs a TenantAdmin of that tenant, and it must be your current tenant (`POST /auth/set-tenant`). See the table in Part 11 |
| App refuses to start: `ISettingsService is registered without authorization` | The app registers the auth module but not `AddAuthorized<ISettingsService, SettingsService>()` / `AddAuthorized<ISettingsAdminService, SettingsAdminService>()`. The sample's `Program.cs` shows both |
| `POST /auth/login` returns 500 | Check that Keycloak is running: `docker compose ps` |
| `POST /auth/signup` returns 500 | Same as above; also check setup was completed |
| `redirectUrl` gives "Invalid redirect_uri" in Keycloak | Keycloak 26 ignores port wildcards. Add the explicit URL (for example `http://localhost:5000/auth/callback`) to the `groundup-app` client's Valid redirect URIs (Part 1.2) |
| Logout URL gives "Invalid redirect uri" | Add `http://localhost:<port>/` to **Valid post logout redirect URIs** on the `groundup-app` client (Part 1.2) |
| Callback returns `CSRF_STATE_MISMATCH` | You opened the `redirectUrl` in a different browser than the one that called the API. Use the same browser for both steps |
| No Register link on the Keycloak login page | User registration is off: Keycloak admin → groundup realm → Realm settings → Login → User registration On |
| Registering shows *"You are already authenticated as different user '...'. Please sign out first."* | Keycloak still has another user's session in this browser. Log out fully (Recipe 2), or retry in a private window using the same window for the API call and the `redirectUrl`. Delete the half-created user in Keycloak admin → groundup realm → Users, because the failed registration still created it |
| `POST /auth/signup` opens the login page instead of the registration form | The running Keycloak is older than 26.1 (the dev file pins 26.3; recreate with `docker compose -f docker-compose.yml -f docker-compose.keycloak.yml up -d`), or the app is an old build that still sends `kc_action=register`. The `redirectUrl` should contain `prompt=create` |
| Callback returns 400 "Missing code or state parameter" | The state cookie expired (10 minutes). Start over from the initiate step |
| Callback returns 410 Gone | You already used this auth flow. Start over |
| Login signs you in without asking for a password | Keycloak still has your session (SSO). Normal. Log out fully (Recipe 2) to see the form |
| Cookie not set in the browser | Check `CookieSecure: false` in `appsettings.Development.json` (plain http does not get Secure cookies) |
| `/auth/me` returns 401 after a successful callback | The browser did not store the cookie. Use a browser or a cookie-aware client. On a subdomain host, check DevTools → Application → Cookies: the cookie is only stored if its domain matches the host (Part 12 explains why `*.localhost` does not work) |
| Opening `/auth/login` in the address bar shows HTTP ERROR 405 | `/auth/login` and `/auth/signup` are POSTs. Run them from Swagger, served from the same host you want the callback on |
| Part 12: callback returns `CSRF_STATE_MISMATCH` ("State cookie is missing") on a subdomain | Older builds hard-coded `Secure` on the state cookie, which browsers drop on plain-http hosts other than `localhost`. Rebuild with the current code and make sure `CookieSecure` is `false` in `appsettings.Development.json` |
| Part 12: login on a subdomain shows the picker instead of going straight to the organization | The host does not end in the current `auth.application.default-domain`. Check the setting value and that you opened Swagger on `<slug>.<default-domain>` |
| Part 12: logout says "Invalid redirect uri" | The post-logout URI in Keycloak must match exactly, including the trailing `/` (`http://<host>:5000/`) |
| A tenant-scoped endpoint such as `GET /api/projects` returns 403 `TENANT_REQUIRED` | Your session has no current tenant (you are on the picker, or you left the tenant you were in). Call `POST /auth/set-tenant` with a tenant from `GET /auth/me/tenants`, or create one with `POST /auth/organizations` (Part 10) |
| After clearing `default-tenant-slug`, login still puts the user in My Test Org | The app caches settings for 15 minutes. Restart it |
| Part 13: no email appears in Mailpit | Check the mail catcher is running (`docker ps` shows `groundup-mailpit`), and that Keycloak's Email tab uses host `host.docker.internal`, port `1025`, with authentication and SSL off. Keycloak shows an error on the verify page when it cannot send |
| Part 13 with Gmail: Test connection fails, or the verify page says it could not send | A normal password is rejected: use an **App password** (needs 2-Step Verification). Check host `smtp.gmail.com`, port `587`, StartTLS on, SSL off, authentication on, and the full Gmail address as the username. The Keycloak container log (`docker compose logs keycloak`) shows the exact SMTP error (for example `535` = bad credentials) |
| Part 13 with Gmail: the email is not in the inbox | Check spam. Gmail also rewrites the sender to your own address, so look for mail from yourself. Allow a minute |
| Keycloak's Email settings are empty again | They live in Keycloak's database. A full reset (`down -v`, "Resetting Everything") wipes them. Enter them again |
| After enabling "Verify email", Alice or Bob is asked to verify at login | They registered while verification was off, so Keycloak recorded them as unverified. Mark them verified in Keycloak admin → Users → the user → Email verified On (Part 13.1 step 3) |
| A POST from Swagger or a REST client returns 403 `CSRF_VALIDATION_FAILED` | The request used the cookie without a valid CSRF token. In Swagger, hard-refresh the page (Ctrl+F5) so it loads the CSRF interceptor, and retry once (a token from before a sign-in as another user is rejected; the retry fetches a new one). With a REST client, follow "Running requests" at the top, or use a Bearer header |
| The Swagger page is blank | An old build with the broken CSRF interceptor, or a cached page. Rebuild with the current code and hard-refresh (Ctrl+F5) |
| An endpoint or the callback returns 401 `REAUTH_REQUIRED` | The session is older than the absolute session lifetime. Run `POST /auth/login?forceLogin=true` and open its `redirectUrl`; Keycloak asks for the password and the session starts again |
| Callback returns `ID_TOKEN_INVALID` | The ID token from Keycloak did not validate. The app log line "ID token rejected for realm ..." gives the reason. Check that the setup wizard's `publicBaseUrl` and `internalBaseUrl` match the URL Keycloak puts in its tokens (`iss`), and that the app can reach Keycloak at the internal URL |
| The app logs a warning that the default domain is a single label | `auth.application.default-domain` is `localhost` (or another name without a dot). That is fine on `localhost`, but browsers drop the cookie on subdomain hosts. Part 12 uses `groundup.test` |
| Part 15: `POST /api/invitations` returns `INVITATION_URL_NOT_CONFIGURED` | Set `auth.invitations.accept-url` (Part 15.1 step 2) |
| Part 15 or 16: creating an invitation or link returns 403 | You are not in My Test Org (`POST /auth/set-tenant`), or the signed-in user may not grant that role |
| Part 15: no accept link in the console | The default sender only writes the link in the Development environment |
| Part 15: accept returns 403 `INVITATION_EMAIL_MISMATCH` at the callback | You signed in as someone other than the invited email. Log out fully and accept again, or send `"forceLogin": true` so Keycloak asks who you are |
| Part 8 or 9 behaves differently from this guide | Check the starting state in the table at the top, especially `default-tenant-slug`, and that the app was restarted after changing it |

---

## Resetting Everything

Wipes all data and starts fresh (re-run migrations and the full setup wizard afterwards). If you ran Part 13, also remove the mail catcher with `docker rm -f groundup-mailpit`.

```powershell
docker compose -f docker-compose.yml -f docker-compose.keycloak.yml down -v
docker compose -f docker-compose.yml -f docker-compose.keycloak.yml up -d
```

---

## Resetting a Test User

Removes one user and every organization **only they** belong to, so you can sign up again with the same email. Run it once for each test user (`testuser@example.com`, `member@example.com`, the Part 15 users `invitee@example.com` and `nobody@example.com` if you ran that part, and the Part 13 users `verify@example.com`, `unverified@example.com`, `verify2@example.com` if you ran that part). Settings, the setup wizard and other users are untouched. Organizations that also have other members are kept; only this user's membership and roles in them are removed. The seeded `system` tenant is never deleted.

**1. Preview** (pgAdmin/DBeaver query tool, `groundup` database). Change the email on the first line:
```sql
WITH target_user AS (
  SELECT "Id" FROM "AuthUsers" WHERE lower("Email") = lower('you@example.com')
)
SELECT t."Id", t."Name", t."Slug",
       (SELECT count(*) FROM "AuthUserTenants" o
         WHERE o."TenantId" = t."Id" AND o."UserId" NOT IN (SELECT "Id" FROM target_user)) AS other_members
FROM "AuthTenants" t
WHERE t."Id" IN (SELECT ut."TenantId" FROM "AuthUserTenants" ut WHERE ut."UserId" IN (SELECT "Id" FROM target_user));
```
Rows with `other_members = 0` will be deleted. Rows with `other_members > 0` will be kept. When resetting Alice, `My Test Org` has Bob as another member until Bob is reset first; **reset Bob first, then Alice** (and Carol before Alice if you ran Part 15).

**2. Delete.** Run as one script; it is a single transaction, so any error leaves the database unchanged. Use the same email:
```sql
BEGIN;

CREATE TEMP TABLE reset_user ON COMMIT DROP AS
  SELECT "Id" FROM "AuthUsers" WHERE lower("Email") = lower('you@example.com');

CREATE TEMP TABLE reset_tenants ON COMMIT DROP AS
  SELECT DISTINCT ut."TenantId" AS "Id"
  FROM "AuthUserTenants" ut
  JOIN "AuthTenants" t ON t."Id" = ut."TenantId"
  WHERE ut."UserId" IN (SELECT "Id" FROM reset_user)
    AND t."Slug" <> 'system'
    AND NOT EXISTS (SELECT 1 FROM "AuthUserTenants" o
                    WHERE o."TenantId" = ut."TenantId"
                      AND o."UserId" NOT IN (SELECT "Id" FROM reset_user));

-- Role assignments and memberships: the user's everywhere, plus anything in the tenants being deleted
DELETE FROM "AuthUserRoles"
 WHERE "UserId" IN (SELECT "Id" FROM reset_user) OR "TenantId" IN (SELECT "Id" FROM reset_tenants);
DELETE FROM "AuthUserTenants"
 WHERE "UserId" IN (SELECT "Id" FROM reset_user) OR "TenantId" IN (SELECT "Id" FROM reset_tenants);

-- Tenant-owned rows (role-policy and policy-permission links cascade)
DELETE FROM "AuthInvitations" WHERE "TenantId" IN (SELECT "Id" FROM reset_tenants) OR "UserId" IN (SELECT "Id" FROM reset_user);
DELETE FROM "AuthJoinLinks"   WHERE "TenantId" IN (SELECT "Id" FROM reset_tenants);
DELETE FROM "AuthRoles"    WHERE "TenantId" IN (SELECT "Id" FROM reset_tenants);
DELETE FROM "AuthPolicies" WHERE "TenantId" IN (SELECT "Id" FROM reset_tenants);

-- No foreign keys on these; clean up so nothing points at deleted tenants
DELETE FROM "AuthFlowStates" WHERE "TenantId" IN (SELECT "Id" FROM reset_tenants);
DELETE FROM "SettingValues"  WHERE "ScopeId"  IN (SELECT "Id" FROM reset_tenants);

DELETE FROM "AuthTenants" WHERE "Id" IN (SELECT "Id" FROM reset_tenants);
DELETE FROM "AuthUsers"   WHERE "Id" IN (SELECT "Id" FROM reset_user);

COMMIT;
```

**3. Delete the Keycloak user too**, or Keycloak's registration form will reject the email as already taken. Keycloak admin → **groundup** realm (not master) → **Users** → search the email → **Delete**.

**4. Log out fully** (Recipe 2) so Keycloak shows the forms again, then restart the app. Setting values and permissions are cached in memory, so a running app may remember data deleted directly in the database for up to 15 minutes. (Changes made through the API clear the cache immediately on that instance.)

If `default-tenant-slug` points at an organization this deletes, clear it (Recipe 3 with `''`).

---

## What's Not Yet Testable

These flows are planned for future phases and are not implemented yet:

- **Enterprise SSO / custom realms** (Phase 10E, GU-69): `501 Not Implemented` is returned if triggered; invitations and join links for enterprise tenants return `422 ENTERPRISE_NOT_SUPPORTED`
- **Sending invitation emails** (GU-64, Notifications module): the default sender only writes the link to the console in Development
- **Admin management UI**
- **Revocation across several app instances** (Phase 15, caching module): today an old token is rejected only by the instance that handled the leave (7.4)
