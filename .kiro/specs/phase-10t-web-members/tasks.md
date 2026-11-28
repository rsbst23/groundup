# Tasks: Web App, Members, Invitations and Join Links (GU-90)
Execute one top-level task at a time. After each: web build, lint and tests (and the .NET suites when the sample host changes), commit, stop for review.
Baselines (main after #59): web 125 / 0; unit 2177 / 0; integration 622 / 0; sample 22/22.
Requirements: `requirements.md`. Design: `design.md`. Three PRs: 90a = task 1 and 2 (branch `phase-10/web-members`), 90b = task 3, 90c = tasks 4 and 5 (new branches cut from main after each merge).

## Gap analysis (before tasks)
- **React StrictMode** runs effects twice in development. The acceptance page's one-time session refresh uses a ref guard, and the outcome is copied into state on the first render, so the second effect run neither refreshes twice nor loses the outcome after the URL is replaced.
- **The acceptance outcome's organization name** comes from the refreshed session (current tenant). Until it loads, the page says "You joined the organization"; it does not call the lookup again (an accepted invitation's lookup now says "already used").
- **Removing your own last TenantAdmin role** is refused by the API (`LAST_ADMIN`); no special UI. Removing your own membership ends your access: `onSessionChanged`, then the Session page shows the new state.
- **Pending members** can be removed (the API revokes their invitation too) and can be given roles; the UI does not treat them differently apart from the status chip.
- **Role picker size:** `GET /api/roles?pageSize=100` (the API's maximum). A tenant with more than 100 roles would see the first 100 by name; noted in the code, not handled.
- **The accept URL in Development** assumes the web app on port 5173 (the Vite default the project uses). Another port means changing the setting (GU-91 adds the page; until then Swagger). TESTING.md says so.
- **`/api/dev/invitation-emails` with a tenantless session** works (the endpoint does not need a tenant; the host/token check only compares a tenant when both name one).
- **`MeDto` change:** existing web tests build `MeDto` objects; they get the two new fields.
- **The accept and join start calls** are POSTs: a signed-out browser has no CSRF token; `getCsrfToken` returns null on 401 and the public endpoints accept the request without one (as the API guide does from Swagger signed out). Verified in task 4 by hand; the Node test covers that no header is sent when the token is null.

## Tasks
- [x] 1. Shared API pieces and access helpers (design 1, 2) — PR 90a
  - [x] 1.1 `paging.ts`, `labels.ts`; types for members, roles, invitations, join links, lookups, dev emails; `MeDto` permissions
  - [x] 1.2 `members.ts`, `roles.ts`, `invitations.ts`, `joinLinks.ts`, `acceptance.ts`, `devMailbox.ts`
  - [x] 1.3 `features/access`: `PermissionKeys`, `can`, `useCan`, `PermissionButton` / `PermissionIconButton`, `RequireTenant`
  - [x] 1.4 Tests: every API function (method, path, body, query, mapping), labels, `can`
  - [x] 1.5 Check: web build, lint, tests 146 / 0. `withQuery` moved from `auth.ts` to `api/query.ts` (shared); `paging.ts` builds `pagedPath`
- [x] 2. Members page (design 3) — PR 90a
  - [x] 2.1 `membersQuery.ts`, `MembersPage.tsx`, `AddRoleMenu.tsx`, `RemoveMemberDialog.tsx`; `routes/members.tsx`
  - [x] 2.2 Self-changes refresh or reset the session
  - [x] 2.3 Check: web build, lint, tests 150 / 0; push; PR 90a info. The manual run is Rob's, in review (no browser here)
- [x] 3. Invitations and Join links pages (design 3) — PR 90b
  - [x] 3.1 `invitationsQuery.ts`, `InvitationsPage.tsx`, `InviteDialog.tsx`; `routes/invitations.tsx`
  - [x] 3.2 `joinLinksQuery.ts`, `JoinLinksPage.tsx`, `CreateJoinLinkDialog.tsx`, `ShowLinkDialog.tsx`, `joinUrl.ts`; `routes/join-links.tsx`
  - [x] 3.3 Tests: `joinUrl`, uses and max-uses parsing, date-to-UTC conversion for expiry, invitation helpers. Shared `features/common`: `dates.ts`, `ConfirmDialog`, `RoleSelect`. In 90b the invite notice points to the API's log; 90c changes it to the Mailbox
  - [x] 3.4 Check: web build, lint, tests 162 / 0; push; PR 90b info
- [x] 4. Acceptance pages, mailbox, sample host (design 3, 5) — PR 90c
  - [x] 4.1 `outcomeSearch.ts`, `AcceptancePage.tsx`; `routes/invite.$token.tsx`, `routes/join.$token.tsx`
  - [x] 4.2 `MailboxPage.tsx`, `routes/mailbox.tsx`, NavMenu entry (dev only)
  - [x] 4.3 Sample host: `DevMailbox`, `DevMailboxInvitationEmailSender`, `DevSettingsSeeder`, `AddSampleDevTools`, `DevController`; `Program.cs`
  - [x] 4.4 Tests: `outcomeSearch`; .NET: mailbox ring, sender, dev tools not registered outside Development, endpoint 200 over HTTP and 401 / 404 on the controller (the shared test host signs every request in), seeder (no value, empty value, existing value). Also: callback messages for the invitation and join-link errors; new routes need `npx vite build` once to regenerate the route tree before `npm run build`
  - [x] 4.5 Check: web 170 / 0; unit 2177 / 0, integration 622 / 0, sample 30/30
- [ ] 5. Docs, full check, PR 90c
  - [x] 5.1 TESTING.md Part 5 (5.1 to 5.10); "Parts to Come"; the API guide's Parts 15 and 16 point to Part 5; ARCHITECTURE.md and the web README: one request path, TanStack Query for API data, Zustand for shared client state when needed, permission-aware buttons, the dev tools
  - [x] 5.2 Full check (web 170 / 0; unit 2177 / 0, integration 622 / 0, sample 30/30); push; PR 90c info. Manual run: Rob ran TESTING.md Parts 3 and 5 end to end, all passed (2026-11-27). Found: Yahoo rejects plus addresses, so with Keycloak's Verify email on use Gmail plus addresses or turn verification off; a bare "403 Forbidden" should name the missing permission (added to GU-98)
  - [ ] 5.3 Jira: GU-90 comment; Done after merge
