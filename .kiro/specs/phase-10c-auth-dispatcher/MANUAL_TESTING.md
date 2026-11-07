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

**Run the sample app:** Hit F5 in Kiro (use the `http` profile).

**URLs:**
- Sample app Swagger: http://localhost:5117/swagger/index.html
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

### 1.2 Create the Keycloak Admin Client

The `groundup-app` public client is already imported from `realm.json` with the correct redirect URIs. You only need to create the confidential admin client once:

1. Go to http://localhost:8080 → log in as `admin` / `admin`
2. Select the **groundup** realm from the dropdown (top-left)
3. Go to **Clients** → **Create client**
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

Because the endpoint now returns a redirect URL as JSON instead of 302-redirecting, the two-step flow is:

1. **Call the API** to get the Keycloak URL:

```
POST /auth/signup
Content-Type: application/json

{
  "organizationName": "My Test Org"
}
```
Expected: `200 { "redirectUrl": "http://localhost:8080/realms/groundup/protocol/openid-connect/auth?..." }`

2. **Copy the `redirectUrl`** from the response and paste it into your browser. This opens the Keycloak login page with the registration tab active.

### 2.2 Register a New Account

On the Keycloak login page:
- Click **Register** (or the form opens directly on the registration tab)
- Fill in: email `testuser@example.com`, password `Test123!`, first name `Test`, last name `User`
- Submit

Keycloak will redirect the browser back to `http://localhost:5117/auth/callback?code=...&state=...`

### 2.3 Interpret the Callback Response

The callback now returns JSON instead of redirecting. The browser will show raw JSON:

```json
{
  "token": "eyJhbGci...",
  "redirectUrl": "/"
}
```

In a real frontend, the client would receive this JSON, store the token, and navigate to `redirectUrl`. For manual testing, copy the `token` value.

### 2.4 Verify Results

**Check your identity** — use the token as a cookie header:
```
GET /auth/me
Cookie: AuthToken=<paste token here>
```
Expected: `{ "userId": "...", "email": "testuser@example.com", "tenantId": "...", "roles": [] }`

If you completed the flow via the browser, the `AuthToken` cookie is already set. Check via browser DevTools → Application → Cookies → `AuthToken`.

**Verify the database** — confirm these were created:
- A `Tenants` row with slug `my-test-org`
- A `Users` row with `ExternalUserId` matching the Keycloak sub
- A `UserTenants` membership row
- A `Roles` row for `TenantAdmin` in that tenant
- A `UserRoles` assignment for the founder

---

## Phase 3: Login Flow

Tests logging in to an existing account (created in Phase 2).

### 3.1 Initiate Login

```
POST /auth/login
```
No body needed. Expected: `200 { "redirectUrl": "http://localhost:8080/realms/groundup/..." }`

Copy the `redirectUrl` and paste into your browser.

### 3.2 Log In

Enter `testuser@example.com` / `Test123!` on the Keycloak login page.

Keycloak redirects back to `/auth/callback`. The browser shows JSON:

**Single membership (Phase 2 user):**
```json
{
  "token": "eyJhbGci...",
  "redirectUrl": "/"
}
```
A new `AuthToken` cookie is set. Done.

**Multiple memberships** (if you've joined more than one org):
```json
{
  "requiresTenantSelection": true,
  "tenants": [
    { "id": "...", "name": "My Test Org", "slug": "my-test-org" },
    { "id": "...", "name": "Other Org", "slug": "other-org" }
  ]
}
```
Proceed to Phase 4.

### 3.3 Verify

```
GET /auth/me
```
Expected: `{ "userId": "...", "email": "testuser@example.com", "tenantId": "...", "roles": [...] }`

---

## Phase 4: Tenant Selection (Multi-Tenant Users)

If the login callback returned `requiresTenantSelection: true`, use this to complete sign-in.

### 4.1 Select a Tenant

```
POST /auth/set-tenant
Content-Type: application/json
Cookie: AuthToken=<pending-selection token from callback>

{
  "tenantId": "<id from the tenants array>"
}
```
Expected: 200 OK, new `AuthToken` cookie written scoped to that tenant.

### 4.2 Verify

```
GET /auth/me
```
Should now show the selected `tenantId`.

---

## Phase 5: Simple Signup Flow

Tests signing up without creating an organization — the user is auto-joined to the configured default tenant. Requires a default tenant to be set up first.

### 5.1 Configure the Default Tenant

First, ensure a tenant exists (from Phase 2). Then set the `auth.application.default-tenant-slug` setting in the database to point to it. The easiest way is to insert it directly via psql or by having the seeder pick it up — for now, create it via the settings API if available, or insert via Swagger or psql:

```sql
-- Quick dev approach: update the setting value directly
UPDATE "SettingValues"
SET "Value" = 'my-test-org'
WHERE "SettingDefinitionId" = (
  SELECT "Id" FROM "SettingDefinitions" WHERE "Key" = 'auth.application.default-tenant-slug'
)
AND "ScopeId" IS NULL;
```

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

### 7.1 Join a Second Tenant First

To leave a tenant without triggering the "no tenants remaining" path, you need at least two active memberships. Repeat Phase 2 or Phase 5 with a different user / incognito window to create a second tenant, then add the current user as a member via the database.

### 7.2 Leave a Non-Current Tenant

```
POST /auth/tenants/<other-tenant-id>/leave
Cookie: AuthToken=<your token>
```
Expected: `200 { "success": true }` — no cookie change (you're still on your current tenant).

### 7.3 Leave the Current Tenant

```
POST /auth/tenants/<current-tenant-id>/leave
Cookie: AuthToken=<your token>
```
Expected: `200 { "success": true }` — a **new** `AuthToken` cookie is written scoped to the next available tenant.

### 7.4 Leave Your Last Tenant

If you have only one membership:
```
POST /auth/tenants/<tenant-id>/leave
Cookie: AuthToken=<your token>
```
Expected: `200 { "noTenants": true }` — `AuthToken` cookie is cleared.

### 7.5 Last-Admin Guard

If you're the sole `TenantAdmin` in a tenant:
```
POST /auth/tenants/<tenant-id>/leave
Cookie: AuthToken=<your token>
```
Expected: `403 { "error": "...", "errorCode": "LAST_ADMIN" }` — membership untouched.

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
Expected: 200 OK, `AuthToken` cookie cleared (expired in response headers).

Verify:
```
GET /auth/me
```
Expected: 401 Unauthorized.

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
POST http://my-test-org.localhost:5117/auth/login
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
GET http://another-org.localhost:5117/api/projects
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
| `redirectUrl` from `/auth/login` or `/auth/signup` gives "Invalid redirect_uri" in Keycloak | The realm.json already includes `http://localhost:*/auth/callback` — this shouldn't happen. If it does, check you're using port `5117` |
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

## What's Not Yet Testable

These flows are planned for future phases and are not implemented yet:

- **Enterprise SSO / custom realms** (Phase 10D) — `501 Not Implemented` is returned if triggered
- **Invitation flow** (Phase 10E)
- **Join links** (Phase 10F)
- **Admin management UI** (Phase 10G)
