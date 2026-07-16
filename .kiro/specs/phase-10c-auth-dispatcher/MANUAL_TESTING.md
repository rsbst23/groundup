# Phase 10 — Manual Testing Guide

This document walks through manually testing the auth system end-to-end. Follow the steps in order — each section builds on the previous.

## Prerequisites

**From PowerShell in `d:\_Rob\github\groundup`:**

```powershell
docker compose -f docker-compose.yml -f docker-compose.keycloak.yml up -d
```

**Apply database migrations (first time only):**

```powershell
dotnet ef database update --project src/GroundUp.Data.Postgres --startup-project samples/GroundUp.Sample --context SampleDbContext
dotnet ef database update --project src/GroundUp.Auth.Data.Postgres --startup-project samples/GroundUp.Sample --context AuthDbContext
```

**Run the sample app:** Hit F5 in Kiro.

**URLs:**
- Sample app Swagger: http://localhost:5000/swagger/index.html
- Keycloak admin: http://localhost:8080 (admin / admin)

---

## Phase 1: Setup Wizard (Configuring Keycloak Settings)

The setup wizard configures the Keycloak connection settings that the auth flows need. Without this, `/auth/login` and `/auth/register` won't work.

### 1.1 Configure the Bootstrap Admin Token

Add this to `samples/GroundUp.Sample/appsettings.Development.json` under the root:

```json
{
  "GroundUp": {
    "BootstrapAdminToken": "my-dev-bootstrap-token-at-least-32-chars-long!!",
    "Auth": { ... existing auth config ... }
  }
}
```

Restart the app (stop + F5).

### 1.2 Check Setup Status

```
GET /setup
```

Should return `{"code":"setup_active","message":"..."}` confirming setup mode.

### 1.3 Create a Keycloak Admin Client

Before running the setup wizard, create a confidential client in Keycloak for admin API access:

1. Go to http://localhost:8080 → login as admin/admin
2. Select the **groundup** realm (dropdown top-left)
3. **Update the `groundup-app` client** — go to Clients → `groundup-app` → Settings:
   - Valid redirect URIs: add `http://localhost:*/auth/callback` (the realm.json only has https)
   - Web origins: add `http://localhost:*`
   - Save
4. **Create the admin client** — Clients → Create client:
4. Client ID: `groundup-admin`
5. Client authentication: **ON** (makes it confidential)
6. Authorization: OFF
7. Standard flow: OFF, Direct access grants: OFF, Service accounts roles: **ON**
8. Save
9. Go to the **Credentials** tab → copy the **Client secret**
10. Go to **Service account roles** tab → **Assign role** → Filter by clients → assign `realm-admin`

### 1.4 Run the Setup Wizard Steps

All setup calls require the header: `Authorization: Bearer my-dev-bootstrap-token`

**Step 1: Set App Identity**
```
POST /setup/app-identity
Content-Type: application/json
Authorization: Bearer my-dev-bootstrap-token

{
  "applicationName": "GroundUp Dev",
  "defaultDomain": "localhost"
}
```

**Step 2: Set Identity Provider**
```
POST /setup/identity-provider
Content-Type: application/json
Authorization: Bearer my-dev-bootstrap-token

{
  "publicBaseUrl": "http://localhost:8080",
  "internalBaseUrl": "http://localhost:8080",
  "sharedRealmName": "groundup",
  "appClientId": "groundup-app"
}
```

**Step 3: Bootstrap Keycloak Admin**
```
POST /setup/keycloak-bootstrap
Content-Type: application/json
Authorization: Bearer my-dev-bootstrap-token

{
  "adminClientId": "groundup-admin",
  "adminClientSecret": "<paste the secret from step 1.3>"
}
```

**Step 4: Create First Admin** (optional for auth flow testing)
```
POST /setup/first-admin
Content-Type: application/json
Authorization: Bearer my-dev-bootstrap-token

{
  "email": "admin@example.com",
  "firstName": "Admin",
  "lastName": "User",
  "password": "Admin123!"
}
```

**Step 5: Complete Setup**
```
POST /setup/complete
Authorization: Bearer my-dev-bootstrap-token
```

### 1.5 Verify Settings Are Configured

Restart the app. If it starts without Keycloak validation errors, settings are configured correctly.

---

## Phase 2: New Organization Registration Flow

This is the "sign up" flow — a new user creates an organization (tenant).

### 2.1 Create a Test User in Keycloak

1. Go to Keycloak admin → **groundup** realm → **Users** → **Add user**
2. Email: `testuser@example.com`, First name: `Test`, Last name: `User`
3. Email verified: ON
4. Save → go to **Credentials** tab → **Set password** → `Test123!` (temporary: OFF)

### 2.2 Initiate Registration

Open this URL in your browser (not Swagger — needs browser redirects):

```
http://localhost:5000/auth/register?organizationName=My%20Test%20Org
```

**Expected:** Redirects to Keycloak login page.

### 2.3 Complete the Flow

1. Log in with `testuser@example.com` / `Test123!`
2. Keycloak redirects back to `/auth/callback`
3. **Expected:** Redirect to the app's root URL (or wherever `FlowResult.Success` sends you)

### 2.4 Verify Results

**Check the auth cookie** — open browser DevTools → Application → Cookies. You should see an `AuthToken` cookie.

**Check `/auth/me`:**
```
GET /auth/me
```
Should return your user info with a `tenantId` (the newly created org).

**Check the database** — in Keycloak admin or via Swagger:
- A new Tenant with slug derived from "My Test Org" (e.g., `my-test-org`)
- A User linked to the Keycloak sub
- A UserTenant membership
- A TenantAdmin role assigned to the user

---

## Phase 3: Login Flow

This tests logging in to an existing account.

### 3.1 Clear Your Auth Cookie

Delete the `AuthToken` cookie from DevTools, or open an incognito window.

### 3.2 Initiate Login

Open in browser:

```
http://localhost:5000/auth/login
```

**Expected:** Redirects to Keycloak login page.

### 3.3 Complete Login

Log in with `testuser@example.com` / `Test123!`

**Expected outcomes depend on membership count:**
- **One membership** (from Phase 2): Auto-selects that tenant, issues token, redirects home.
- **Multiple memberships**: Returns tenant picker JSON (no redirect).

### 3.4 Verify

```
GET /auth/me
```

Should return your identity with `tenantId`, `email`, etc.

---

## Phase 4: Tenant Selection (Multi-Tenant Users)

If the login flow returns a tenant picker (user has 2+ memberships), test set-tenant.

### 4.1 Set Tenant

```
POST /auth/set-tenant
Content-Type: application/json

{
  "tenantId": "<tenant-id-from-picker-response>"
}
```

**Expected:** 200 OK, new `AuthToken` cookie issued scoped to that tenant.

### 4.2 Verify

```
GET /auth/me
```

Should show the selected `tenantId`.

---

## Phase 5: Token Refresh

The token refresh happens automatically via middleware. To test manually:

### 5.1 Wait for Token to Age

The token refreshes when age > 50% of `TokenExpirationMinutes` (60 min by default). For testing, temporarily lower this in appsettings.Development.json:

```json
"TokenExpirationMinutes": 2
```

### 5.2 Make a Request After 1+ Minutes

```
GET /auth/me
```

**Expected:** Response succeeds AND the `AuthToken` cookie is rewritten with a new value (check DevTools → Network → Response Headers for `Set-Cookie`).

---

## Phase 6: Logout

```
POST /auth/logout
```

**Expected:**
- 200 OK
- `AuthToken` cookie cleared (expired)
- Response may include a Keycloak end_session URL

### Verify

```
GET /auth/me
```

Should return 401 Unauthorized.

---

## Phase 7: Host-Based Tenant Resolution

This tests subdomain-based tenant pinning. Requires DNS/hosts file setup.

### 7.1 Add Hosts File Entry

Add to `C:\Windows\System32\drivers\etc\hosts`:

```
127.0.0.1  my-test-org.localhost
```

### 7.2 Access via Subdomain

```
http://my-test-org.localhost:5000/auth/login
```

**Expected:** When you log in, the system detects the host-resolved tenant and auto-selects it (skipping the tenant picker even for multi-membership users).

---

## Phase 8: Host/Token Reconciliation

Tests what happens when your token is scoped to tenant A but you navigate to tenant B's subdomain.

### 8.1 Log in to Tenant A (normal flow)

### 8.2 Navigate to Tenant B's Subdomain

If you create a second org, add its slug to hosts, then navigate there:

**Expected:**
- Data endpoints: 409 TENANT_SWITCH_REQUIRED (if you're a member) or 403 Forbidden (if not)
- `/auth/*` endpoints: exempt, always accessible

### 8.3 Switch Tenant

```
POST /auth/set-tenant
{
  "tenantId": "<tenant-b-id>"
}
```

Resolves the mismatch without re-authentication.

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| `relation "SettingDefinitions" does not exist` | Run the EF migrations (see Prerequisites) |
| `Keycloak configuration validation failed` | Run the setup wizard (Phase 1) |
| `/auth/login` returns 500 | Check that Keycloak is running and settings are configured |
| Redirect URI mismatch at Keycloak | Add `http://localhost:5000/auth/callback` to the `groundup-app` client's Valid Redirect URIs in Keycloak admin (Clients → groundup-app → Settings → Valid redirect URIs) |
| Cookie not being set | Check `CookieSecure: false` in dev config (http doesn't get secure cookies) |

---

## Resetting Everything

To start completely fresh:

```powershell
docker compose -f docker-compose.yml -f docker-compose.keycloak.yml down -v
docker compose -f docker-compose.yml -f docker-compose.keycloak.yml up -d
```

Then re-run migrations and the setup wizard.

---

## Notes for Future Phases

- **Phase 10D** (Enterprise SSO): Will add dedicated realm login tests here
- **Phase 10E** (Tenant Invitations): Will add invitation acceptance flow tests
- **Phase 10F** (Admin Management): Will add admin panel endpoint tests
