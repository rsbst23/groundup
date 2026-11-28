# Requirements: Web App, Members, Invitations and Join Links (GU-90)

## Context

The sample web app (`samples/GroundUp.Sample.Web`, GU-86/87/89) has placeholder pages for Members, Invitations and Join links. The API for them exists (GU-68), as do the roles API (GU-95) and the caller's permissions in `/auth/me` (GU-96). Testers still run the API guide's Parts 15 and 16 from Swagger, and copy the invitation link from the API's log.

What exists in the web app: `apiFetch` (cookies, CSRF, `OperationResult` unwrapping, `ApiError`), `ErrorPanel` (status, code, message, `errors`, correlation id), the Requests panel, the session query and helpers (`onSessionChanged`), the Session page that reads the sign-in outcome (`result`, `alreadyMember`, `error`, `idpError`) and `outcomeMessages`. Tests are Node-only (vitest with a fetch mock), no component tests. The API sends enums as numbers.

## Decisions (with Rob)

1. The web app reads `permissions` and `allPermissions` from `/auth/me`. Actions the user lacks are disabled with a tooltip naming the missing permission. A page the user cannot read shows the API's 403 in `ErrorPanel`.
2. The sample's settings seeder sets `auth.invitations.accept-url` to `http://localhost:5173/invite/{token}` in Development, only when it is empty.
3. **Dev mailbox:** a Development-only invitation email sender in the sample host keeps the last 50 emails in memory; a Development-only endpoint and a Mailbox page show them. (May change later.)
4. Accept and join return to `/invite/{token}` and `/join/{token}`, which show the outcome and refresh the session.
5. Join links: the page builds `<origin>/join/{token}` and shows it once, with a copy button.
6. Members page: a table with status, role chips with their sources (remove), add a role from `GET /api/roles`, remove a member with confirmation, refusals inline, 25 per page.
7. Tests: Node tests for the API functions and pure helpers; TESTING.md Part 5 mirrors the API guide's Parts 15 and 16.
8. Three PRs: **90a** API functions, `/auth/me` permissions, Members page; **90b** Invitations and Join links pages; **90c** accept and join pages, the setting seed, the dev mailbox, TESTING.md Part 5.

## Scope

**In scope:** the pages above, their API functions and types, permission-aware actions, the setting seed and dev mailbox in the sample host, tests, TESTING.md Part 5.

**Out of scope:**
- Roles and policies pages: GU-97.
- Settings pages (changing the accept URL from the UI): GU-91.
- Component tests (a separate decision if ever wanted).
- Any change to the framework's endpoints or `LoggingInvitationEmailSender` (still the default outside the sample).

## Requirements

### Requirement 1: Shared (90a)

1. `MeDto` gains `permissions: string[]` and `allPermissions: boolean`. A helper `can(me, key)` is true for `allPermissions` or a listed key.
2. API functions in `src/api/` for: members (list, remove, add role, remove role), roles (list), invitations (create, list, resend, revoke), join links (create, list, regenerate, revoke), the public lookup, accept and join calls, and the dev mailbox. Each with hand-written types like the existing ones, numeric enums mapped to labels in one place.
3. Paged lists use `pageNumber` and `pageSize` (25) and read `totalRecords` from the body.
4. The tenant's data is cleared on a tenant switch (the existing `onSessionChanged` resets non-session queries).
5. Without a current tenant, the three admin pages say so and offer the organization picker instead of calling the API.

### Requirement 2: Members page (90a)

1. Lists Active and Pending members: name, email, status, and roles as chips. A role held for more than one reason shows its sources (Manual, Invitation, Join link, Default role, Provisioning).
2. **Add role:** a picker of the tenant's roles not already held (from `GET /api/roles`), then `POST /api/members/{userId}/roles/{roleId}`.
3. **Remove role:** the chip's delete action, `DELETE /api/members/{userId}/roles/{roleId}`. It removes the role whatever its sources.
4. **Remove member:** a confirmation dialog naming the member, then `DELETE /api/members/{userId}`.
5. Refusals (`LAST_ADMIN`, `LAST_SUPERADMIN`, `PERMISSION_NOT_HELD`, 403) are shown with `ErrorPanel` next to the action, without leaving the page. After a change the list reloads.
6. Actions need: list `auth.members.read`; remove member `auth.members.manage`; add or remove role `auth.roles.assign` (the API also applies the role rules: only admins grant TenantAdmin, the escalation guard). The UI disables by permission only; the API's refusal covers the rest.
7. If the signed-in user removes their own last role or leaves, the session is refreshed.

### Requirement 3: Invitations page (90b)

1. Lists invitations newest first: email, role, status (Pending, Accepted, Revoked, Expired), expiry, created.
2. **Invite:** email, role (picker), optional expiry date (can only shorten the lifetime). `INVITATION_URL_NOT_CONFIGURED`, `ALREADY_MEMBER` and `INVITATION_PENDING` are shown inline.
3. **Resend** and **Revoke** for Pending and Expired invitations; revoke asks for confirmation.
4. In Development the page links to the Mailbox ("The email is in the dev mailbox").
5. Needs `auth.invitations.send`.

### Requirement 4: Join links page (90b)

1. Lists links newest first: role, status (Active, Revoked, Expired, Used up), uses (`useCount` / `maxUses` or "unlimited"), expiry, created.
2. **Create:** role, optional expiry, optional maximum uses. The response's token becomes `<origin>/join/{token}`, shown once in a dialog with a copy button and a warning that it cannot be shown again.
3. **Regenerate** (confirmation: the old link stops working) shows the new link the same way. **Revoke** asks for confirmation.
4. Needs `auth.joinlinks.manage`.

### Requirement 5: Accept and join pages (90c)

1. `/invite/$token` and `/join/$token` work signed out and signed in.
2. On load they call the lookup and show the organization and role, or the reason: invitation `not_found`, `revoked`, `expired`, `accepted`; join link `not_found`, `revoked`, `expired`, `used_up`, each in plain words.
3. **Accept** / **Join** call the start endpoint with `returnUrl` set to the same page (`/invite/{token}`), then navigate the browser to the returned URL. **Sign in as someone else** sends `forceLogin: true`.
4. When the page is reached with an outcome (`result`, `alreadyMember`, `error`, `idpError`), it shows it instead of the lookup: joined ("You joined My Test Org"), already a member, or the error message from `outcomeMessages` (with **Sign in as someone else** for `INVITATION_EMAIL_MISMATCH`). It refreshes the session (`onSessionChanged`) once and removes the outcome from the URL. Links lead to Session and Members.
5. Start errors (`410`, `403 INVITATION_WRONG_ORGANIZATION`, `422 ENTERPRISE_NOT_SUPPORTED`, `400 INVALID_RETURN_URL`) are shown with `ErrorPanel`.
6. The routes do not start with a proxied prefix (the existing route test covers it).

### Requirement 6: Sample host (90c)

1. In Development, the sample's settings seeder sets `auth.invitations.accept-url` (System level) to `http://localhost:5173/invite/{token}` when it has no value (missing or empty). Any other existing value is never changed. Outside Development nothing is seeded.
2. In Development, the sample registers a `DevMailboxInvitationEmailSender` that keeps the last 50 invitation emails (to, organization, role, link, expiry, time) in memory (a singleton, newest first) and also logs like the default sender. Outside Development the framework's default stays.
3. `GET /api/dev/invitation-emails` exists only in Development and returns the stored emails. It needs a signed-in user but no permission (a development tool). It is not part of the framework.
4. A **Mailbox** page (`/mailbox`, in the navigation when the web app runs in Vite's dev mode) lists them with the link (opens the accept page) and a refresh button. When the endpoint is 404 (the API is not in Development) it says so.

### Requirement 7: Tests and docs

1. Node tests: each API function's request (method, path, body, query) and response mapping; the permission helper; enum labels; the join URL builder; the outcome-on-page parsing; the dev mailbox sender (a .NET unit test in the sample's integration test project or a small unit test).
2. Sample integration tests: the dev mailbox endpoint is 404 outside Development; the seeder sets the accept URL only when empty.
3. TESTING.md Part 5 (90c): the API guide's Parts 15 and 16 from the web app (Alice invites Carol through the Invitations page, Carol accepts from the mailbox link, revoke and resend, join links with Bob), plus the refusals (last admin, a permission the user lacks).
4. The "Parts to Come" table and the API guide's Parts 15 and 16 point to Part 5.
