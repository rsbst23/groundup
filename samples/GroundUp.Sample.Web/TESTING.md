# Sample Web App — Testing Manual

A script for testing GroundUp by hand through the sample web app. Each page story (GU-88 to GU-97) adds its part. The API guide, [MANUAL_TESTING.md](../../.kiro/specs/phase-10c-auth-dispatcher/MANUAL_TESTING.md), stays as the Swagger and API reference; this manual replaces it part by part as the pages arrive.

Each part says who you are, what state you start in and what state you leave behind.

---

## How to Use This Guide

### The test users

The same people as the API guide.

| Name | Email | Password | Role in this script |
|---|---|---|---|
| **Sam** | `admin@example.com` | `Admin123!` | The system administrator (SuperAdmin), created by the setup wizard's first-admin step. |
| **Alice** | `testuser@example.com` | `Test123!` | Founds `My Test Org`; its TenantAdmin. |
| **Bob** | `member@example.com` | `Test123!` | A regular member. |
| **Carol** | `invitee@example.com` | `Test123!` | Has no account until Alice invites her. |

Until the sign-in page exists (GU-89), Part 1 needs a user who already exists. Any user from the API guide works.

### What state you are in after each part

| Part | What it tests | You end as |
|---|---|---|
| 1 | The scaffold: layout, session banner, errors, the Requests panel, CSRF | signed in, as you started |
| 2 | Returning to the app after sign-in (`returnUrl`) | signed in |

### Words used

- **Banner:** the strip under the app bar showing who is signed in, the current tenant, roles and the token's expiry.
- **Requests panel:** the drawer opened with the **Requests** button in the app bar. It lists the app's recent API calls with headers and bodies.

---

## Before You Start

1. **Docker** (Postgres and Keycloak), from the repo root:
   ```powershell
   docker compose -f docker-compose.yml -f docker-compose.keycloak.yml up -d
   ```
2. **Migrations**, the first time and after pulling new ones (stop the app first):
   ```powershell
   dotnet ef database update --project src/GroundUp.Data.Postgres --startup-project samples/GroundUp.Sample --context SampleDbContext
   dotnet ef database update --project src/GroundUp.Auth.Data.Postgres --startup-project samples/GroundUp.Sample --context AuthDbContext
   ```
3. **Setup.** If the setup wizard has never run, do Part 1 of the API guide first (GU-88 moves it here).
4. **Keycloak must allow port 5173.** A realm newly imported from `keycloak/realm.json` already does. For an existing realm: http://localhost:8080 (admin / admin) → **groundup** realm → **Clients → groundup-app → Settings**, then add:
   - **Valid redirect URIs:** `http://localhost:5173/auth/callback`
   - **Valid post logout redirect URIs:** `http://localhost:5173/`
   - **Web origins:** `http://localhost:5173`

   Save. Without these, Keycloak answers "Invalid parameter: redirect_uri".
5. **Start the API and the web app.** In Kiro, pick **API + Web app** in the Run and Debug dropdown and press F5. The browser opens http://localhost:5173. If it opens before the API is ready, reload once.

---

## Part 1: The Scaffold

**You start:** signed out (or signed in; step 1.2 signs you out of the app).
**You end:** signed in as the user you chose.

### 1.1 Layout and navigation
1. Click each item in the menu. **Expected:** each page except Home names the story that builds it (with a Jira link), and the menu highlights the current page.
2. On a placeholder page, reload the browser. **Expected:** the same page comes back.
3. Open http://localhost:5173/data?page=3. **Expected:** "page = 3". Then `?page=abc`. **Expected:** "page = 1".
4. Open http://localhost:5173/nope. **Expected:** "Page not found" with a link to Home.

### 1.2 Signed out
1. Open http://localhost:5173/swagger, run `POST /auth/logout`, then go back to http://localhost:5173 and reload.
2. **Expected:** the banner says "Not signed in".

### 1.3 Sign in (through Swagger until GU-89)
1. In the same browser, open http://localhost:5173/swagger (Swagger through the web app's host, not port 5000).
2. Run `POST /auth/login`. Copy `redirectUrl` and open it. **Expected:** it contains `redirect_uri=http%3A%2F%2Flocalhost%3A5173%2Fauth%2Fcallback`.
3. Sign in. **Expected:** the browser ends on `localhost:5173/auth/callback` showing JSON, because no `returnUrl` was given (Part 2 brings you back to the app instead).
4. Open http://localhost:5173. **Expected:** the banner shows your email, the current tenant's name (or "No current tenant" if you have several organizations and have not chosen one), your roles, and "Token expires in m:ss" counting down.

### 1.4 The Requests panel
1. Click **Requests**. **Expected:** `GET /auth/me` and `GET /auth/me/tenants` with status 200 and durations. Select one to see its headers and bodies.
2. Rows marked **cancelled** are calls the app cancelled itself (in development React renders pages twice). They are not errors.
3. **Clear** empties the list; **Copy as JSON** copies it.

### 1.5 An error, with its code and correlation ID
1. On Home, click **Call a tenant endpoint (GET /api/members)**.
2. **Expected**, depending on the session:
   - with a current tenant and the TenantAdmin role: "GET /api/members succeeded"
   - with no current tenant: an error panel with **HTTP 403**, the code **TENANT_REQUIRED**, a message and a correlation ID with a copy button
3. For the error case: copy the correlation ID and find the same ID on the call in the Requests panel and in the sample API's log.

### 1.6 A cookie POST without manual CSRF
1. On Home, click **Send a cookie POST (POST /auth/refresh)**. **Expected:** "POST /auth/refresh succeeded (the CSRF token was sent)".
2. In the Requests panel:
   - `GET /auth/csrf` came first; its response body shows `{ "token": "***" }`
   - `POST /auth/refresh` has `"x-csrf-token": "***"` in its request headers
   The real value never appears in the panel.
3. Click the button again. **Expected:** no new `GET /auth/csrf` (the token is reused).

### 1.7 The API is not running
1. Stop the app (Shift+F5 stops both), then start only the web app: in a terminal, `cd samples/GroundUp.Sample.Web` and `npm run dev`. Open http://localhost:5173.
2. **Expected:** "The sample API is not reachable", with an error panel and a **Reload** button.
3. Stop it (Ctrl+C) and start both again with F5.

**Record the result** (date, and anything that did not match) in `.kiro/specs/phase-10t-web-scaffold/tasks.md`, task 4.4.

---

## Part 2: Returning to the App After Sign-In (GU-87)

A sign-in started with a `returnUrl` comes back to that page in the app with the outcome in the address, instead of ending on the callback's JSON. Until GU-89 builds the sign-in page, start it from Swagger on port 5173.

**You start:** signed in or not. **You end:** signed in.

### 2.1 Signed in, back on the app
1. Log out fully (Swagger on port 5173: `POST /auth/logout`, then open `logoutUrl`), so Keycloak asks for the password.
2. Run `POST /auth/login` with **returnUrl** `/session`. Open the `redirectUrl` in the same browser and sign in.
3. **Expected:** the browser lands on `http://localhost:5173/session?result=signed_in` (or `result=tenant_picker` if you belong to several organizations, `result=no_tenant` if none) and the page shows a matching message. The banner shows you signed in without a reload.
4. In the Requests panel there is no callback entry (the callback was a full-page redirect), and `/auth/me` was fetched again after landing.

### 2.2 A return URL that is not a path on this site
1. Run `POST /auth/login` with **returnUrl** `https://example.com`.
2. **Expected:** `400` with `"errorCode": "INVALID_RETURN_URL"`. No Keycloak address is returned. `//example.com` and `/auth/callback` are refused the same way.

### 2.3 An error comes back to the app too
1. Run `POST /auth/login` with **returnUrl** `/session` and copy `redirectUrl`.
2. Open it in a **private window** (which does not have the sign-in cookie the API set in the normal window) and sign in.
3. **Expected:** the private window lands on `http://localhost:5173/session?error=CSRF_STATE_MISMATCH` with an error message. The sign-in was refused, because it was finished in a different browser from the one that started it. Close the private window.

### 2.4 Crafted values are ignored
1. Open http://localhost:5173/session?result=hacked&error=%3Cb%3Ehi%3C%2Fb%3E.
2. **Expected:** no message; values that do not match the known forms are dropped.

**Record the result** in `.kiro/specs/phase-10t-return-url/tasks.md`, task 3.3.

---

## Parts to Come

| Part | Story | Page |
|---|---|---|
| 3 | GU-88 | Setup wizard |
| 4 | GU-89 | Sign-in, session and tenants (replaces the Swagger sign-in in Parts 1 and 2) |
| 5 | GU-90 | Members, invitations and join links |
| 6 | GU-97 | Roles, policies and permissions |
| 7 | GU-91 | Settings |
| 8 | GU-92 | Sample data |
| 9 | GU-93 | Diagnostics |

---

## Troubleshooting

| Problem | Solution |
|---|---|
| Keycloak says "Invalid parameter: redirect_uri" | Add the three port 5173 URIs (Before You Start, step 4) |
| The browser opened before the API was ready, and the page says the API is not reachable | Reload once the API's console shows "Now listening on" |
| `npm run dev` fails with "Port 5173 is in use" | Another dev server is running. Stop it; the port is fixed because Keycloak only accepts that address |
| After signing in, the banner still says "Not signed in" | You signed in through Swagger on port 5000. Use Swagger on port 5173 (step 1.3.1) so the cookie is set for the web app |
| A page you added goes to the API instead of the app | Its path starts with a proxied prefix; see the rules in README.md |
