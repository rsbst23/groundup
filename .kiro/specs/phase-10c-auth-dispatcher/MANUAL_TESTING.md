# Phase 10 — Manual Testing Guide

This document walks through manually testing the auth system end-to-end. Follow the steps in order — each section builds on the previous.

> **Updated for Phase 10C API Alignment.** All auth endpoints now return JSON instead of 302 redirects. The `/auth/register` endpoint has been renamed to `/auth/signup`. Test using Swagger or a REST client (Bruno, Postman, curl) — not a browser address bar.

---

## Prerequisites

**Start Docker infrastructure** from PowerShell in `d:\_Rob\github\groundup`:

```powershell
docker compose -f docker-compose.yml -f docker-compose.keycloak.yml up -d
```

**Wait ~15 seconds** for Keycloak to fully start, then verify both are healthy:

```powershell
docker compose -f docker-compose.yml -f docker-compose.keycloak.yml ps
```

**Apply database migrations** (first time only, or after pulling new migrations):

```powershell
dotnet ef database update --project src/GroundUp.Data.Postgres --startup-project samples/GroundUp.Sample --context SampleDbContext
dotnet ef database update --project src/GroundUp.Auth.Data.Postgres --startup-project samples/GroundUp.Sample --context AuthDbContext
```

**Run the sample app:** Hit F5 in Kiro. The browser will open and show you the port in the address bar — note it down, you'll use it throughout this guide. It's typically `5000` when launched via IIS Express.

**URLs:**
- Sample app Swagger: `http://localhost:<port>/swagger/index.html`
- Keycloak admin: http://localhost:8080 (admin / admin)

---

## Phase 1: Setup Wizard

The setup wizard configures the Keycloak connection settings that the auth flows need. Without completing this, all `/auth/*` endpoints will return errors.

The `BootstrapAdminToken` is already set in `appsettings.Development.json`:
```
my-dev-bootstrap-token-at-least-32-chars-long!!
```

### 1.1 Verify Setup Is Active

```
GET /setup
```
Expected: `{ "code": "setup_active", ... }` — confirms setup mode is running.

### 1.2 One-Time Keycloak Setup

The `groundup` realm is imported from `realm.json` on first Keycloak startup. Two settings need to be verified or fixed manually because they are either not picked up from the import or are version-sensitive.

**Fix the redirect URI (Keycloak 26 port wildcard limitation):**

Keycloak 26 does not honour the `*` wildcard for ports in redirect URIs. Add the explicit callback URL:

1. Go to http://localhost:8080 → log in as `admin` / `admin`
2. Select the **groundup** realm from the top-left dropdown
3. Go to **Clients** → **groundup-app** → **Settings** tab
4. Under **Valid redirect URIs**, click **Add valid redirect URIs** and add:
   ```
   http://localhost:5000/auth/callback
   ```
5. Save

> If your app runs on a different port, add that port's URL instead (or add a few common ones: 5000, 5001, 7295).

**Enable user registration:**

The realm.json import is skipped if the realm already exists from a previous run, so this setting may not have been applied:

1. Still in the **groundup** realm, go to **Realm settings** → **Login** tab
2. Toggle **User registration** to **On**
3. Save

**Create the Keycloak Admin Client:**

You only need to create the confidential admin client once:

1. Go to **Clients** → **Create client**
   - Client ID: `groundup-admin`
   - Client authentication: **ON**
   - Standard flow: **OFF**
   - Direct access grants: **OFF**
   - Service account roles: **ON**
   - Save
4. Go to the **Credentials** tab → copy the **Client secret**
5. Go to **Service account roles** tab → **Assign role** → Filter by clients → assign `realm-admin`

### 1.3 Run Setup Wizard Steps

All setup calls require the header: `Authorization: Bearer my-dev-bootstrap-token-at-least-32-chars-long!!`

**Step 1: Set App Identity**
```
POST /setup/app-identity
Authorization: Bearer my-dev-bootstrap-token-at-least-32-chars-long!!
Content-Type: application/json

{
  "applicationName": "GroundUp Dev",
  "defaultDomain": "localhost"
}
```
Expected: 200 OK.

**Step 2: Set Identity Provider**
```
POST /setup/identity-provider
Authorization: Bearer my-dev-bootstrap-token-at-least-32-chars-long!!
Content-Type: application/json

{
  "publicBaseUrl": "http://localhost:8080",
  "internalBaseUrl": "http://localhost:8080",
  "sharedRealmName": "groundup",
  "appClientId": "groundup-app"
}
```
Expected: 200 OK.

**Step 3: Bootstrap Keycloak Admin**
```
POST /setup/keycloak-bootstrap
Authorization: Bearer my-dev-bootstrap-token-at-least-32-chars-long!!
Content-Type: application/json

{
  "adminClientId": "groundup-admin",
  "adminClientSecret": "<paste the secret from step 1.2>"
}
```
Expected: 200 OK.

**Step 4: Complete Setup**
```
POST /setup/complete
Authorization: Bearer my-dev-bootstrap-token-at-least-32-chars-long!!
```
Expected: 200 OK.

### 1.4 Verify Setup Completed

Restart the app (stop + F5). If it starts without Keycloak validation errors in the console, settings are configured correctly.

---

## Phase 2: New Organization Signup Flow

Tests the "create your own organization" path — a user signs up and a new tenant is created for them.

### 2.1 Initiate Signup

The signup flow sets an `AuthState` CSRF cookie when you call the API. That cookie must be present when Keycloak redirects back to the callback — which means the `redirectUrl` **must be opened in the same browser** that made the API call. Opening it in a different browser (or a browser that didn't make the original request) will cause a `CSRF_STATE_MISMATCH` error.

The simplest way to ensure this: run `POST /auth/signup` from Swagger, then paste the `redirectUrl` into the address bar of **that same browser** (any tab is fine — cookies are shared across tabs within the same browser).

1. **Call the API** to get the Keycloak URL:

```
POST /auth/signup
Content-Type: application/json

{
  "organizationName": "My Test Org"
}
```
Expected: `200 { "redirectUrl": "http://localhost:8080/realms/groundup/protocol/openid-connect/auth?..." }`

2. **Copy the `redirectUrl`** and paste it into the address bar of the same browser you ran Swagger in. The URL contains `prompt=create`, so Keycloak opens the **registration form** directly (this needs Keycloak 26.1 or later; the dev compose file pins 26.3).

### 2.2 Register a New Account

On the Keycloak registration form (if you instead land on the login page, see Troubleshooting):
- Fill in: email `testuser@example.com`, password `Test123!`, first name `Test`, last name `User`
- Submit

Keycloak will redirect the browser back to `http://localhost:<port>/auth/callback?code=...&state=...`

### 2.3 Interpret the Callback Response

The callback returns JSON instead of redirecting. The browser will show raw JSON:

```json
{
  "token": "eyJhbGci...",
  "redirectUrl": "/"
}
```

The callback URL should contain only `code`, `state`, `session_state` and `iss`. If you see a `kc_action_status=error` parameter, you are running an old build that still sends `kc_action=register`; that parameter was harmless but is gone now that signup uses `prompt=create`.

### 2.4 Verify Results

**Check your identity** — use the token as a cookie header:
```
GET /auth/me
Cookie: AuthToken=<paste token here>
```
Expected: `{ "userId": "...", "email": "testuser@example.com", "tenantId": "...", "roles": [] }`

If you completed the flow via the browser, the `AuthToken` cookie is already set. Check via browser DevTools → Application → Cookies → `AuthToken`.

**Verify the database** — confirm these were created:
- An `AuthTenants` row with slug `my-test-org`
- An `AuthUsers` row with `ExternalUserId` matching the Keycloak sub
- An `AuthUserTenants` membership row
- An `AuthRoles` row for `TenantAdmin` in that tenant
- An `AuthUserRoles` assignment for the founder

> To rerun this phase with the same email, use **Resetting a Test User** at the end of this guide.

---

## Phase 3: Login Flow

Tests logging in to an existing account (created in Phase 2).

> **If you just completed Phase 2:** You are still logged into Keycloak via SSO. Pasting the login `redirectUrl` into the browser will immediately issue a token without showing the login form — that is correct SSO behavior. To test the full login experience (entering credentials), run `POST /auth/logout` first (Phase 9) to clear both the GroundUp cookie and the Keycloak SSO session, then come back here.

### 3.1 Initiate Login

```
POST /auth/login
```
No body needed. Expected: `200 { "redirectUrl": "http://localhost:8080/realms/groundup/..." }`

Copy the `redirectUrl` and paste into your browser.

### 3.2 Log In

Enter `testuser@example.com` / `Test123!` on the Keycloak login page.

Keycloak redirects back to `/auth/callback`. Since the user from Phase 2 has a single membership, the callback auto-selects that tenant and returns JSON:

```json
{
  "token": "eyJhbGci...",
  "redirectUrl": "/"
}
```
A new `AuthToken` cookie is set. Done.

> The multi-membership tenant picker is tested separately in Phase 4, which first creates a second organization so the picker becomes reachable.

### 3.3 Verify

```
GET /auth/me
```
Expected: `{ "userId": "...", "email": "testuser@example.com", "tenantId": "...", "roles": [...] }`

---

## Phase 4: Tenant Selection (Multi-Tenant Users)

Tests the multi-membership tenant picker. The user from Phase 2 only belongs to one org, so this phase first creates a **second** organization for the same user — then logging in returns a tenant picker instead of auto-selecting.

### 4.1 Create a Second Organization

You are already logged in (Phase 3), so create the second org directly with `POST /auth/organizations`. There is no Keycloak round-trip: you become TenantAdmin of the new org, and your session stays on your current org.

> **Why not sign up again?** `POST /auth/signup` is for new people: it sends them to Keycloak's registration form (`prompt=create`) and ties the new identity to the organization. Someone who is already logged in has nothing to register, and an earlier version of the API (which sent `kc_action=register`) failed outright for logged-in users with *"Unexpected error when handling authentication request to identity provider"*. Existing users create organizations with this endpoint instead.

Run these in Swagger, in the same browser you logged in with. The browser sends your `AuthToken` cookie automatically.

1. Create the organization:
```
POST /auth/organizations
Content-Type: application/json

{
  "organizationName": "Second Org"
}
```
Expected: **201** with `{ "id": "...", "name": "Second Org", "slug": "second-org" }`.

2. Confirm your session did **not** switch, and that you now have two memberships:
```
GET /auth/me/tenants
```
Expected: two tenants. `My Test Org` has `"current": true`, `Second Org` has `"current": false`. You switch to the new org with `POST /auth/set-tenant` in step 4.3.

**Optional error checks:**

| Request | Expected |
|---|---|
| Same request again with the same name | 201, slug `second-org-2` (a third org) |
| `{ "organizationName": "   " }` | 400 — name is required |
| A name longer than 200 characters | 400 |
| After `POST /auth/logout` (not logged in) | 401 |

### 4.2 Log In and Get the Picker

Now log in fresh so the callback evaluates membership count:

1. `POST /auth/login` → copy `redirectUrl` → paste into browser.
2. Keycloak (still SSO-authenticated) redirects back to the callback.
3. Because the user now has 2+ standard-tenant memberships, the callback returns the picker instead of a token:
```json
{
  "requiresTenantSelection": true,
  "tenants": [
    { "id": "...", "name": "My Test Org", "slug": "my-test-org" },
    { "id": "...", "name": "Second Org", "slug": "second-org" }
  ]
}
```
At this point the auth cookie holds a Keycloak token (identity-only, no `tid`) — the GroundUp token is not issued until you select a tenant.

### 4.3 Select a Tenant

In Swagger (same browser — the pending-selection token travels in the `AuthToken` cookie):
```
POST /auth/set-tenant
Content-Type: application/json

{
  "tenantId": "<id from the tenants array>"
}
```
Expected: 200 OK, and the browser's `AuthToken` cookie is replaced with a GroundUp token scoped to the selected tenant. Swagger does not show `Set-Cookie`; check DevTools → Application → Cookies if you want to see the value change.

### 4.4 Verify

```
GET /auth/me
```
Should now show the selected `tenantId`.

---

## Phase 5: Simple Signup Flow

Tests signing up without creating an organization — the user is auto-joined to the configured default tenant. Requires a default tenant to be set up first.

### 5.1 Configure the Default Tenant

First, ensure a tenant exists (from Phase 2). Then set the `auth.application.default-tenant-slug` setting to that tenant's slug. There is no API for setting values yet, so do it in the database (pgAdmin/DBeaver query tool, `groundup` database).

The seeder only creates the setting *definition* (with an empty default). There is normally no value row yet, so an `UPDATE` alone changes nothing. Check first, then insert or update:

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

**Restart the app after changing it.** Setting values are cached in memory for 15 minutes, so a direct database edit is not seen by a running app.

> While this setting is set, Login also auto-joins any user with zero memberships to this tenant (Phase 3's "no memberships → 403" case no longer applies). Set the value back to `''` and restart to undo.

### 5.2 Initiate Simple Signup

```
POST /auth/signup
```
No body (or send `{}` with no `organizationName`). Expected: `200 { "redirectUrl": "..." }`

### 5.3 Complete the Flow

Copy the `redirectUrl` into your browser → register a new user → callback returns:
```json
{
  "token": "eyJhbGci...",
  "redirectUrl": "/"
}
```
The new user is automatically a member of the default tenant — no organization creation, no slug selection.

---

## Phase 6: View Your Tenant List

Tests `GET /auth/me/tenants` — the endpoint that returns all tenants a user belongs to.

### 6.1 Get Your Tenants

```
GET /auth/me/tenants
Cookie: AuthToken=<your token>
```

Expected for a single standard tenant:
```json
[
  {
    "id": "...",
    "name": "My Test Org",
    "slug": "my-test-org",
    "type": "standard",
    "switchable": true,
    "current": true
  }
]
```

The `current: true` flag appears on whichever tenant's ID is in your token's `tid` claim.
Enterprise tenants (with a `realmName`) appear as `"type": "enterprise", "switchable": false` with a `subdomain` field instead of `current`.

---

## Phase 7: Leave a Tenant

Tests `POST /auth/tenants/{id}/leave`.

**The rule:** every tenant must always keep at least one active TenantAdmin.
- A regular member can always leave.
- A TenantAdmin can leave only if another active TenantAdmin remains in that tenant.

Run these in Swagger. Each request runs as whoever is logged into the browser, so the steps are ordered to match.

### 7.1 A Regular Member Can Leave

Right after Phase 5 the browser is logged in as the user you signed up there. That user joined `My Test Org` as a **regular member** (no admin role), and it is their only membership.

1. `GET /auth/me/tenants` → note the `id` of `My Test Org`.
2. `POST /auth/tenants/{tenantId}/leave` with that id.

Expected: **200** `{ "noTenants": true }`, and the `AuthToken` cookie is cleared, so `GET /auth/me` now returns 401. In the database, that user's `AuthUserTenants` row for `My Test Org` has `IsActive = false`.

> `My Test Org` still has exactly one admin (your Phase 2 user). Before the fix, this leave was wrongly blocked, because the old check refused any leave when a tenant had only one admin.

### 7.2 The Last TenantAdmin Cannot Leave

Now switch back to your Phase 2 user, who created both `My Test Org` and `Second Org` and is the **only** TenantAdmin of each.

1. Log out fully (Phase 9, including opening the `logoutUrl`), then log in as your Phase 2 user (Phase 3). You get the tenant picker; select a tenant with `POST /auth/set-tenant` (Phase 4.3).
2. `GET /auth/me/tenants` → note the `id` of `Second Org`.
3. `POST /auth/tenants/{tenantId}/leave` with that id.

Expected: **403** `{ "error": "You are the only TenantAdmin of this organization. Assign another TenantAdmin before leaving.", "errorCode": "LAST_ADMIN" }`. `GET /auth/me/tenants` still lists both orgs.

### 7.3 Paths Covered by Integration Tests

These need extra setup (a second admin, or a regular member of two tenants) that the API can't create yet without editing the database. `LeaveTenantTests` covers them against a real database:
- One of two admins leaves (allowed), then the remaining admin tries (blocked, because the departed admin no longer counts).
- Leaving a tenant other than the one your session is on (membership deactivated, no new cookie).
- Leaving your current tenant while you have another (new `AuthToken` cookie for the next tenant).

---

## Phase 8: Token Refresh

Token refresh is automatic via middleware. To test it manually:

### 8.1 Shorten the Token Lifetime

In `appsettings.Development.json`, temporarily set:
```json
"TokenExpirationMinutes": 2
```
Restart the app.

### 8.2 Request After 1+ Minutes

```
GET /auth/me
Cookie: AuthToken=<your token>
```
Expected: 200 OK **and** a new `AuthToken` cookie in the response headers (check Network → Response Headers → `Set-Cookie`). The old token is replaced in place.

### 8.3 Restore

Set `TokenExpirationMinutes` back to `60`.

---

## Phase 9: Logout

```
POST /auth/logout
Cookie: AuthToken=<your token>
```

Expected: 200 OK with `{ "logoutUrl": "http://localhost:8080/realms/groundup/protocol/openid-connect/logout?...&id_token_hint=..." }`, and both the `AuthToken` and `IdToken` cookies cleared (expired in response headers).

**You must also navigate to the `logoutUrl`** to terminate the Keycloak SSO session. Copy the URL from the response and paste it into your browser. This clears the Keycloak session cookie so that the next login flow shows the credential form rather than silently re-authenticating.

Because the logout URL includes `id_token_hint`, Keycloak logs out immediately without showing a confirmation screen, then redirects back to the app root. (Without `id_token_hint`, Keycloak shows a "Do you want to log out?" confirmation — the id_token hint is stored in the `IdToken` cookie at login time specifically to avoid this.)

> **Expected:** After logout you land on `http://localhost:5000/` and see a **404 "This localhost page can't be found"**. This is normal — the API-only sample app has no route mapped to `/`, so the root returns 404. The logout still succeeded. In a real consuming app, `/` would be the frontend landing page. To confirm logout worked, see the verification below.

> **One-time Keycloak setup for logout redirect:** The `post_logout_redirect_uri` must be registered on the `groundup-app` client. In Keycloak admin → groundup realm → Clients → groundup-app → Settings → **Valid post logout redirect URIs**, add `http://localhost:5000/` (match your app's port). Without this, the logout URL returns "Invalid redirect uri".

If you only call `POST /auth/logout` without visiting `logoutUrl`, the GroundUp cookie is cleared but Keycloak still has an active SSO session — the next `POST /auth/login` will immediately return a token without prompting for credentials.

Verify full logout — both halves should be cleared:

1. **GroundUp session cleared:**
   ```
   GET /auth/me
   ```
   Expected: 401 Unauthorized.

2. **Keycloak SSO session cleared:** Run `POST /auth/login`, copy the `redirectUrl`, and paste it into the browser. Keycloak should now show the **login form** (prompting for credentials) instead of silently issuing a token. If it prompts for credentials, the SSO session was successfully terminated.

---

## Phase 10: Host-Based Tenant Resolution

This tests subdomain-pinning — when a user accesses the app via `myorg.localhost`, the system automatically scopes them to that tenant's context.

### 10.1 Add Hosts File Entry

Open `C:\Windows\System32\drivers\etc\hosts` as Administrator and add:
```
127.0.0.1  my-test-org.localhost
```

### 10.2 Initiate Login via Subdomain

```
POST http://my-test-org.localhost:<port>/auth/login
```
Expected: `redirectUrl` returned. Copy and open in browser.

### 10.3 Complete Login

Log in with your credentials. On callback:
- If you're a **member** of `my-test-org`: token auto-scoped to that tenant (no picker, even if you have multiple memberships)
- If you're **not a member**: 403 Forbidden

---

## Phase 11: Host/Token Reconciliation

Tests what happens when your token's `tid` doesn't match the subdomain you're accessing.

### 11.1 Log In to Tenant A Normally

Complete Phase 10 for `my-test-org` — you have a token scoped to that tenant.

### 11.2 Access Tenant B's Subdomain With Tenant A's Token

Add `another-org.localhost` to hosts. Make a data request:
```
GET http://another-org.localhost:<port>/api/projects
Cookie: AuthToken=<tenant-A token>
```
Expected: `409 { "error": "TENANT_SWITCH_REQUIRED" }` (if you're a member of `another-org`) or `403` (if not).

### 11.3 Resolve the Mismatch

```
POST /auth/set-tenant
Cookie: AuthToken=<tenant-A token>

{ "tenantId": "<tenant-B id>" }
```
Expected: 200 OK, new token scoped to tenant B — no re-authentication needed.

---

## Troubleshooting

| Problem | Solution |
|---|---|
| `relation "SettingDefinitions" does not exist` | Run the EF migrations (see Prerequisites) |
| App starts with Keycloak validation errors | Run the setup wizard (Phase 1) |
| `POST /auth/login` returns 500 | Check that Keycloak is running: `docker compose ps` |
| `POST /auth/signup` returns 500 | Same as above; also check setup was completed |
| `redirectUrl` from `/auth/login` or `/auth/signup` gives "Invalid redirect_uri" in Keycloak | Keycloak 26 does not honour port wildcards. Add the explicit URL (e.g. `http://localhost:5000/auth/callback`) to the `groundup-app` client's Valid redirect URIs in Keycloak admin → Clients → groundup-app → Settings |
| Callback returns `CSRF_STATE_MISMATCH` | You opened the `redirectUrl` in a different browser than the one that called the API. The `AuthState` cookie was set in browser A but the callback came back in browser B. Use the same browser for both steps |
| No Register link on Keycloak login page | User registration is disabled. Go to Keycloak admin → groundup realm → Realm settings → Login → toggle User registration On |
| `POST /auth/signup` opens the login page instead of the registration form | Either the running Keycloak is older than 26.1 (check `docker compose ps`; the dev file pins 26.3; recreate with `docker compose -f docker-compose.yml -f docker-compose.keycloak.yml up -d`), or the app is an old build that still sends `kc_action=register`. Check the `redirectUrl`: it should contain `prompt=create` |
| Callback returns 400 "Missing code or state parameter" | The state cookie expired (10 min). Start over from the initiate step |
| Callback returns 410 Gone | You already consumed this auth flow. Start over |
| Cookie not being set in browser | Check `CookieSecure: false` in `appsettings.Development.json` (http doesn't get Secure cookies) |
| `/auth/me` returns 401 after successful callback | The browser didn't store the cookie. The callback sets `Set-Cookie` on the response — make sure you're sending the request through a browser or a cookie-aware client, not a plain REST client |

---

## Resetting Everything

To wipe all data and start completely fresh (re-runs migrations and setup wizard):

```powershell
docker compose -f docker-compose.yml -f docker-compose.keycloak.yml down -v
docker compose -f docker-compose.yml -f docker-compose.keycloak.yml up -d
```

Then re-run migrations and the full setup wizard.

---

## Resetting a Test User

Removes one user and every organization **only they** belong to, so you can sign up again with the same email. Settings, the setup wizard and other users are untouched. Organizations that also have other members are kept; only this user's membership and roles in them are removed. The seeded `system` tenant is never deleted.

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
Rows with `other_members = 0` will be deleted. Rows with `other_members > 0` will be kept.

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

**4. Log out** (Phase 9, including opening the `logoutUrl`) so Keycloak shows the form again, then restart the app. Setting values and permissions are cached in memory, so a running app may still remember the deleted data for up to 15 minutes.

If `default-tenant-slug` (Phase 5) points at an org this deletes, reset that setting to `''`.

---

## What's Not Yet Testable

These flows are planned for future phases and are not implemented yet:

- **Enterprise SSO / custom realms** (Phase 10D) — `501 Not Implemented` is returned if triggered
- **Invitation flow** (Phase 10E)
- **Join links** (Phase 10F)
- **Admin management UI** (Phase 10G)
