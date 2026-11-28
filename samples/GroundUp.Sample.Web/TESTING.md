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

Parts 1 and 2 need a user who already exists (any user from the API guide); Part 3 creates Alice and Bob from scratch; Part 5 creates Carol through an invitation.

### What state you are in after each part

| Part | What it tests | You end as |
|---|---|---|
| 1 | The scaffold: layout, session banner, errors, the Requests panel, CSRF | signed in, as you started |
| 2 | Returning to the app after sign-in (`returnUrl`) | signed in |
| 3 | Sign-in, session and organizations (the API guide's Parts 2 to 10 and 14.1 to 14.3) | Alice, in My Test Org |
| 5 | Members, invitations and join links (the API guide's Parts 15 and 16) | Bob, in My Test Org |

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
1. Click **Sign out** in the top bar (if signed in). Keycloak signs you out and returns you to Home.
2. **Expected:** the banner says "Not signed in" and the top bar shows **Sign in**.

### 1.3 Sign in
1. Click **Sign in** in the top bar and sign in at Keycloak. Its address contains `redirect_uri=http%3A%2F%2Flocalhost%3A5173%2Fauth%2Fcallback` (the web app's host).
2. **Expected:** you are back on Home. If you belong to several organizations, the **Choose an organization** dialog is open; pick one.
3. Without the app (Swagger on port 5173, `POST /auth/login` with no `returnUrl`, open `redirectUrl`), the sign-in ends on the callback's JSON instead, as for API clients.
4. **Expected:** the banner shows your email, the current tenant's name (or "No current tenant" if you have several organizations and have not chosen one), your roles, and "Token expires in m:ss" counting down.

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

A sign-in comes back to the page it started from. Behind the scenes the app sends `returnUrl=/session?next=<page>`, the callback redirects to the Session page with the outcome, and the Session page continues to `next`.

**You start:** signed in as a user with **one** organization. **You end:** signed in.

### 2.1 Back on the page you started from
1. Click **Sign out** in the top bar (it signs out everywhere, so Keycloak asks for the password).
2. Open **Data** with a search value: http://localhost:5173/data?page=3.
3. Click **Sign in** in the top bar and sign in.
4. **Expected:** you land on `/data?page=3` ("page = 3"), signed in. In the browser history there is no extra entry for the Session page (Back goes to the page before Data).

### 2.2 A return URL that is not a path on this site
The app never builds one, so check the API directly: in Swagger on port 5173, run `POST /auth/login` with **returnUrl** `https://example.com`.
**Expected:** `400` with `"errorCode": "INVALID_RETURN_URL"`. `//example.com` and `/auth/callback` are refused the same way.

### 2.3 An error comes back to the app too
1. In Swagger on port 5173, run `POST /auth/login` with **returnUrl** `/session` and copy `redirectUrl`.
2. Open it in a **private window** (which does not have the sign-in cookie set in the normal window) and sign in.
3. **Expected:** the private window shows the Session page with "The sign-in did not complete: CSRF_STATE_MISMATCH", an explanation and **Sign in again**. Close the private window.

### 2.4 Crafted values are ignored
1. Open http://localhost:5173/session?result=hacked&error=%3Cb%3Ehi%3C%2Fb%3E&next=https%3A%2F%2Fexample.com.
2. **Expected:** no message, and you are not sent anywhere. Values that do not match the known forms are dropped.

---

## Part 3: Sign-In, Session and Organizations (GU-89)

The API guide's Parts 2 to 10 and 14.1 to 14.3, from the browser. Same people, same order, same states; run them once, in order, from a clean start (the API guide's "Resetting a Test User" for `testuser@example.com` and `member@example.com`, and `default-tenant-slug` empty).

**Where things are:**
- **Session page:** sign-in and sign-up when signed out; session details, **Refresh token**, your organizations (**Switch**, **Leave**), **Create an organization**, and the two sign-out buttons when signed in.
- **Top bar:** **Sign in**, or **Sign out** (everywhere: the app and Keycloak).
- **Banner:** who you are, the current organization, roles, the token countdown, and **Choose organization** when you have none selected.
- **Organization picker:** a dialog that opens on its own after a sign-in that needs a choice.

| Part | What it tests | You end as |
|---|---|---|
| 3.1 | Alice signs up and creates My Test Org | Alice, in My Test Org |
| 3.2 | Alice logs in (one membership) | Alice, in My Test Org |
| 3.3 | Second organization, the picker | Alice, in My Test Org, two organizations |
| 3.4 | The last TenantAdmin cannot leave | unchanged |
| 3.5 | Simple sign-up through the default tenant (Bob) | Bob, in My Test Org |
| 3.6 | A member leaves | Bob, no current tenant |
| 3.7 | Rejoining through the default tenant | Bob, in My Test Org |
| 3.8 | Nowhere to go | Bob, no current tenant |
| 3.9 | Creating an organization from a tenantless session | Bob, in Tenantless Org |
| 3.10 | Forced sign-in and the session times | Alice, in My Test Org |

### 3.1 Alice signs up and creates an organization
1. Signed out, open **Session**. Type `My Test Org` in **Organization name** and click **Sign up and create it**.
2. Keycloak opens its **registration** form. Register `testuser@example.com` / `Test123!`, first name `Test`, last name `User`.
3. **Expected:** the Session page says "Signed in". Session details: email `testuser@example.com`, current tenant **My Test Org**, role **TenantAdmin**. **Your organizations** lists My Test Org as current.

### 3.2 Alice logs in (one membership)
1. Top bar **Sign out**, then open **Members** and click **Sign in**. Sign in as Alice.
2. **Expected:** you land back on **Members**, in My Test Org (banner), with no picker.

### 3.3 Second organization and the picker
1. On **Session**, **Create an organization**: `Second Org`. **Expected:** "Created Second Org (second-org)" with **Switch to it**; the banner still shows **My Test Org** (creating does not move you). Your organizations: two, My Test Org current.
2. Refusals: an empty name cannot be submitted; a name over 200 characters shows "At most 200 characters".
3. Top bar **Sign out**. Open **Members**, click **Sign in**, sign in as Alice.
4. **Expected:** you land on **Members** with the **Choose an organization** dialog open, listing both. The banner says "No current tenant" with no roles.
5. Click **Not now**. **Expected:** the dialog closes; the banner shows **Choose organization**. (Pages with organization data would show `TENANT_REQUIRED` now; Home's **Call a tenant endpoint** shows it.)
6. Click **Choose organization**, pick **My Test Org**. **Expected:** the dialog closes; the banner shows My Test Org and TenantAdmin. Home's **Call a tenant endpoint** succeeds.
7. On **Session**, **Switch** to Second Org and back. Each time the banner and "current" follow; the sign-in time in Session details does not change.

### 3.4 The last TenantAdmin cannot leave
1. On **Session**, **Leave** on **Second Org**, and confirm.
2. **Expected:** an error panel: **HTTP 403**, **LAST_ADMIN**, "You are the only TenantAdmin of this organization…". Both organizations are still listed.

### 3.5 Simple sign-up through the default tenant (Bob)
1. Set the default tenant to `my-test-org` (the API guide's Recipe 3) and restart the app (Shift+F5, F5).
2. Top bar **Sign out** (Alice's Keycloak session must end, or Keycloak refuses to register someone else).
3. On **Session**, click **Sign up without an organization**. Register `member@example.com` / `Test123!`, first name `Bob`, last name `Member`.
4. **Expected:** "Signed in"; current tenant **My Test Org**; **no roles**; one organization, standard, current.

### 3.6 A member leaves (Bob)
1. On **Session**, **Leave** on **My Test Org**, and confirm.
2. **Expected:** the banner says "No current tenant"; Session details show no current tenant and no roles; **Your organizations** is empty. No picker opens (it only opens after a sign-in). Home's **Call a tenant endpoint** gives **403 TENANT_REQUIRED**.
3. The API guide's 7.3 (database) and 7.4 (a saved token stops working) still need Swagger, curl and SQL; run them there if you want that check.

### 3.7 Rejoining through the default tenant (Bob)
1. Top bar **Sign out**, then **Sign in** as Bob.
2. **Expected:** current tenant **My Test Org**, no roles. (The API guide's Part 8 step 5 checks in the database that the same membership row was reused.)

### 3.8 Nowhere to go (Bob)
1. On **Session**, **Leave** My Test Org.
2. Clear the default tenant (Recipe 3 with `''`) and restart the app.
3. Top bar **Sign out**, then **Sign in** as Bob.
4. **Expected:** the Session page says "Signed in. You do not belong to any organization yet; create one below." (with no organizations you stay on the Session page, whatever page you started from). No current tenant, no roles, no organizations.

### 3.9 Creating an organization from a tenantless session (Bob)
1. **Create an organization**: `Tenantless Org`. **Expected:** "Created Tenantless Org"; the banner still says "No current tenant" and now shows **Choose organization**.
2. Click **Switch to it**. **Expected:** current tenant **Tenantless Org**, role **TenantAdmin**; Home's **Call a tenant endpoint** succeeds.

### 3.10 Forced sign-in and the session times (Alice)
1. Top bar **Sign out**, **Sign in** as Alice with the password, and choose **My Test Org** if asked. On **Session**, note **Signed in with password** and **Token expires**.
2. Wait 2 minutes. On **Session**, click **Sign out of this app** (Keycloak keeps its session), then **Sign in**. **Expected:** no Keycloak form (single sign-on), and **Signed in with password** is the **same** time as in step 1; **Token issued** is later.
3. **Refresh token**. **Expected:** **Token issued** and **Token expires** move forward; **Signed in with password** does not.
4. **Sign out of this app**, then **Sign in and ask for my password**. **Expected:** Keycloak shows the form; afterwards **Signed in with password** is now.
5. (The API guide's 14.4, an old session asking for the password because of `max_age`, needs the session settings changed as SuperAdmin; it moves here with the settings pages, GU-91.)

**Record the result** in `.kiro/specs/phase-10t-web-session/tasks.md`, task 3.2.

---

## Part 4: Setup Wizard (GU-88)

Not written yet. The setup wizard's pages come with GU-88, which runs from a reset database; until then use the API guide's Part 1. Parts are numbered by story, so Part 5 comes before Part 4 exists.

---

## Part 5: Members, Invitations and Join Links (GU-90)

The API guide's Parts 15 and 16, from the browser. Start after Part 3: Alice is TenantAdmin of My Test Org, and Bob is no longer a member of it.

**Where things are:**
- **Members:** the organization's members, their roles (a chip per role, with why it is held when that is more than a manual grant), **Add role**, the chip's delete button, **Remove**.
- **Invitations:** **Invite**, and **Resend** and **Revoke** for pending and expired invitations.
- **Join links:** **Create link**, and **Regenerate** and **Revoke** for active links. A link is shown once, with **Copy**.
- **Mailbox** (Development only): the invitation emails the API kept instead of sending them, with their links. It needs a signed-in user, so copy a link before signing out.
- **Accept and join pages** (`/invite/…`, `/join/…`): what a link is for, **Accept** or **Join**, **Sign in as someone else**, and the outcome after the sign-in.

Buttons you do not have the permission for are disabled; hover to see which permission. The API checks every request anyway, and its refusals appear in an error panel above the table.

| Part | What it tests | You end as |
|---|---|---|
| 5.1 | Set up (once): the accept URL, a Member role | Alice, in My Test Org |
| 5.2 | The members list and the last-admin guard | unchanged |
| 5.3 | Alice invites Carol | Alice |
| 5.4 | Carol accepts and registers | Carol, in My Test Org |
| 5.5 | An invitation works once | Carol |
| 5.6 | Resend and revoke | Alice |
| 5.7 | The wrong account accepts an invitation | Alice |
| 5.8 | A join link for Bob, and its maximum uses | Bob, in My Test Org |
| 5.9 | Regenerate, revoke, and an existing member | Alice |
| 5.10 | What a member without admin permissions sees | Bob |

### 5.1 Set up (once)
1. **Restart the app** (Shift+F5, F5) so the sample API seeds the accept URL: in Development it sets `auth.invitations.accept-url` to `http://localhost:5173/invite/{token}` when it is empty. If the web app runs on another port, change the setting through Swagger (the API guide's 15.1, step 3).
2. **A Member role**: Part 6.1, step 2 (Roles › New role). Skip it if it exists already.
3. **Optional: let Members see the member list** (for 5.10): Part 6.2 (the Member basics policy with `auth.members.read`, attached to Member).

### 5.2 The members list and the last-admin guard (Alice)
1. Open **Members**. **Expected:** Alice, Active, with the chip **TenantAdmin · Provisioning** (she got it by creating the organization).
2. Click the delete button on Alice's **TenantAdmin** chip. **Expected:** an error panel: **HTTP 409**, **LAST_ADMIN**. The chip stays.
3. Click **Remove** on Alice and confirm. **Expected:** the same **LAST_ADMIN** refusal.

### 5.3 Alice invites Carol
1. Open **Invitations** and click **Invite**. Email `invitee@example.com`, role **Member**, no expiry. **Send invitation**.
2. **Expected:** "Invitation sent to invitee@example.com. In Development no email goes out: open the Mailbox page…". The list shows the invitation, **Pending**, expiring in 7 days.
3. Open **Members**. **Expected:** Carol (`invitee@example.com`), **Pending**, chip **Member · Invitation**.
4. Open **Mailbox**. **Expected:** the email to `invitee@example.com` for My Test Org, role Member. Right-click **Open the invitation** and copy the link.
5. Refusals: **Invite** `invitee@example.com` again. **Expected:** in the dialog, **409 INVITATION_PENDING**. An address that is not an email cannot be submitted.

### 5.4 Carol accepts and registers
1. Top bar **Sign out** (Alice's Keycloak session must end, or Keycloak refuses to register someone else).
2. Paste the link into the address bar. **Expected:** the **Invitation** page: "Join **My Test Org** as **Member**", with **Accept invitation** and **Sign in as someone else**. No sign-in was needed to see it.
3. Click **Accept invitation**. **Expected:** Keycloak's **registration** form with `invitee@example.com` filled in (Carol has no account). Register with `Test123!`, first name `Carol`, last name `Invitee`.
4. **Expected:** back on the Invitation page: "You joined My Test Org", with **Session** and **Members** buttons. The banner shows Carol in My Test Org with the Member role. The address no longer has `?result=…`.

### 5.5 An invitation works once
1. Reload the same link. **Expected:** "This invitation was already used." and no buttons.

### 5.6 Resend and revoke (Alice)
1. Top bar **Sign out**, **Sign in** as Alice (choose My Test Org if asked).
2. **Invitations** → **Invite** `nobody@example.com`, Member. In **Mailbox**, copy its link (link A).
3. Back on **Invitations**, **Resend** it. **Expected:** "A new invitation was sent… The old link no longer works." **Mailbox** now has a second email (link B).
4. Open link A. **Expected:** "This link is not valid." Open link B. **Expected:** "Join My Test Org as Member".
5. **Revoke** the invitation and confirm. **Expected:** status **Revoked**; **Members** no longer lists `nobody@example.com`; link B now says "This was withdrawn by the organization."

### 5.7 The wrong account accepts an invitation (Alice)
1. **Invite** `someone@example.com`, Member, and copy its link from **Mailbox**.
2. Still signed in as Alice, open the link and click **Accept invitation**. Keycloak asks for credentials (you are signed in with a different email); sign in as **Alice**.
3. **Expected:** the Invitation page shows **INVITATION_EMAIL_MISMATCH** ("The invitation was sent to a different email address…") with **Sign in as someone else**. Alice's session is unchanged.
4. Revoke the invitation afterwards (**Invitations** → **Revoke**).

### 5.8 A join link for Bob, and its maximum uses
1. As Alice, **Join links** → **Create link**: role **Member**, maximum uses `1`. **Expected:** the link dialog: `http://localhost:5173/join/…`, "This link is shown only now". **Copy** it, then **Done**. The list shows it **Active**, uses `0 / 1`.
2. Top bar **Sign out**. Open the link. **Expected:** the **Join link** page: "Join **My Test Org** as **Member**".
3. **Join**, sign in as **Bob**. **Expected:** "You joined My Test Org"; the banner shows Bob in My Test Org with Member.
4. Open the same link again. **Expected:** "This link has reached its maximum number of uses."

### 5.9 Regenerate, revoke, and an existing member (Alice)
1. Top bar **Sign out**, **Sign in** as Alice. **Join links**: the first link shows **Used up**, `1 / 1`, with no actions.
2. **Create link**: Member, no maximum. Copy it (link C).
3. Open link C (still Alice) and **Join**. **Expected:** "You are already a member of My Test Org. Nothing changed." **Join links** still shows `0 / unlimited` (a member's visit is not counted).
4. **Regenerate** link C and confirm. **Expected:** a new link is shown once. Open link C: "This link is not valid."
5. **Revoke** the new link and confirm. **Expected:** status **Revoked**; opening it says "This was withdrawn by the organization."
6. **Members** lists Alice, Carol and Bob; Carol's chip is **Member · Invitation**, Bob's **Member · Join link**.

### 5.10 What a member without admin permissions sees (Bob)
1. Top bar **Sign out**, **Sign in** as Bob.
2. Open **Members**. **Expected:** with the optional policy from 5.1, the list loads; **Add role**, **Remove** and the chips' delete buttons are disabled, and hovering names the permission (for example "Needs the permission auth.members.manage"). Without it: "Permission needed. You need the permission auth.members.read to see this page." The Requests panel shows no call to `/api/members` (GU-98).
3. Open **Invitations** and **Join links**. **Expected:** the same message with `auth.invitations.send` and `auth.joinlinks.manage`, and no call to their endpoints.

### 5.11 Sorting, searching and filtering (Alice, GU-98)
After 5.9 there are three members, two invitations and three join links.
1. **Members**: click **Email**. **Expected:** sorted by email, the header shows an arrow, and the address bar has `?sort=email`. Click it again: `email desc`, reversed. Click **Name**: sorted by name.
2. Type `car` in **Search name or email**. **Expected:** about a third of a second after you stop typing, only Carol is listed and the address bar has `q=car`. Clear it: everyone is back.
3. **Status**: **Pending**. **Expected:** "Nothing matches." (everyone has joined). **All**: everyone is back.
4. Press the browser **Back** button. **Expected:** the previous view (sort, search, status) comes back. Refresh the page: the same view.
5. **Invitations**: **Status** **Revoked**. **Expected:** only the revoked invitation. Sort by **Role**, **Expires**, **Created**.
6. **Join links**: **Status** **Used up**. **Expected:** only the first link. **Status** **Revoked**: the revoked link. Type `mem` in **Search role**: links for the Member role.
7. Open `/members?page=9`. **Expected:** the first page (the list moves back to the last page with rows).
8. Open `/members?sort=bogus&status=x`. **Expected:** the normal list: values the page does not understand are dropped, and fields the API does not allow are ignored.

**Record the result** in `.kiro/specs/phase-10t-web-members/tasks.md`, task 5.2 (5.1 to 5.10) and in `.kiro/specs/phase-10t-admin-lists/tasks.md` (5.10 and 5.11).

---

## Part 6: Roles and Policies (GU-97)

The roles and policies pages replace the API guide's 15.1 steps 1 and 2. Start as Alice, TenantAdmin of My Test Org. The 403 → 200 check (6.5) also needs Carol, a member with the Member role (Part 5.4).

### 6.1 Roles and the default role (Alice)
1. Open **Roles**. **Expected:** TenantAdmin with **built-in**, type **Built-in**, policies **All**, no **Delete**. The box above the table says "No default role. New members who join by signing in get no role." (unless one was set earlier).
2. **New role**: name `Member`, description `Regular member`, type Application. **Expected:** the Member role's page opens; it has no policies and "None." under Effective permissions. If Member exists already, the dialog shows **409 ROLE_NAME_TAKEN** (try `member` too: names ignore case).
3. **‹ Roles**, then **Set** (or **Change**) in the default role box and pick Member. **Expected:** "Default role: Member…", and Member has the **default** chip.
4. Open **TenantAdmin**. **Expected:** "TenantAdmin has all permissions in this organization", no policy section, the button reads **Edit description**, and in the dialog the name cannot be changed.

### 6.2 A policy for Members (Alice)
1. Open **Policies**, **New policy**: name `Member basics`, tick **auth** › the members read permission (`auth.members.read`). **Expected:** the module heading's box shows a dash (some selected); **Create** adds the row with Permissions 1 and Roles 0.
2. Open **Roles** › **Member** › **Attach policy**, pick Member basics (the dialog shows what it grants), **Attach**. **Expected:** the policy is listed, and Effective permissions shows the members read permission "Granted by Member basics". **Roles** shows Policies 1 for Member; **Policies** shows Roles 1 for Member basics.
3. Click **Member basics** on the Policies page. **Expected:** the dialog lists "Used by: Member" (a link to the role).

### 6.3 Refusals (Alice)
1. **Roles**: **Delete** on Member. **Expected:** **409 ROLE_IN_USE** with the reasons (it is the default role; members hold it if Part 5 ran). Clear the default role first to see that reason go.
2. **Policies**: **Delete** on Member basics. **Expected:** **409 POLICY_IN_USE** naming Member.
3. **New role** named `TenantAdmin`. **Expected:** **409 ROLE_NAME_TAKEN**.
4. A throwaway role: **New role** `Temp`, then **Delete** it from its page. **Expected:** the roles list opens and Temp is gone at once.

### 6.4 Sorting and search
1. **Roles**: click **Policies** twice (most policies first), **Type**, **Name**. **Expected:** the order changes and the address bar has `sort=…`. Search `regular`: only Member (its description).
2. **Policies**: sort by **Permissions** and **Roles**; search `basics`.
3. On a role's page, **‹ Roles** returns to the same sort and search.

### 6.5 A permission change applies without signing in again (Alice and Carol)
Use two browsers: Alice in your normal one, Carol in a private window (sign in as Carol there).
1. As Carol, open **Members**. **Expected:** the list (Member basics grants `auth.members.read`).
2. As Alice, **Policies** › **Member basics**: untick the members read permission, **Save**.
3. As Carol, reload **Members** (no sign-in). **Expected:** "Permission needed. You need the permission auth.members.read to see this page." (the session reloaded her permissions), and the Requests panel shows no call to `/api/members`.
4. As Alice, tick it again and **Save**. As Carol, reload **Members**. **Expected:** the list again.
5. As Carol, open **Roles**. **Expected:** "You need the permission auth.roles.read…" (Member basics does not grant it).

**Record the result** in `.kiro/specs/phase-10t-web-roles/tasks.md`, task 5.

---

## Parts to Come

| Part | Story | Page |
|---|---|---|
| 4 | GU-88 | Setup wizard (placeholder above) |
| 7 | GU-91 | Settings (then the API guide's 14.4 moves here) |
| 8 | GU-92 | Sample data |
| 9 | GU-93 | Diagnostics |

---

## Troubleshooting

| Problem | Solution |
|---|---|
| Keycloak's verification email bounces ("Address not found") for an address like `name+alice@yahoo.com` | Yahoo does not accept plus addresses. Use Gmail plus addresses (`name+alice@gmail.com`), or turn off **Verify email** in the realm (the API guide's 13.7) and use the `@example.com` test users; invitations go to the Mailbox either way. Delete the half-registered user in Keycloak before retrying |
| Keycloak says "Invalid parameter: redirect_uri" | Add the three port 5173 URIs (Before You Start, step 4) |
| The browser opened before the API was ready, and the page says the API is not reachable | Reload once the API's console shows "Now listening on" |
| `npm run dev` fails with "Port 5173 is in use" | Another dev server is running. Stop it; the port is fixed because Keycloak only accepts that address |
| After signing in, the banner still says "Not signed in" | You signed in through Swagger on port 5000. Use Swagger on port 5173 (step 1.3.1) so the cookie is set for the web app |
| A page you added goes to the API instead of the app | Its path starts with a proxied prefix; see the rules in README.md |
