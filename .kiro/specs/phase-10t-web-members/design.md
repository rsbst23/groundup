# Design: Web App, Members, Invitations and Join Links (GU-90)

Requirements: `requirements.md` in this folder.

## Overview

```
src/api/            members.ts  roles.ts  invitations.ts  joinLinks.ts  acceptance.ts  devMailbox.ts
                    paging.ts (page query + PaginatedData type)   labels.ts (enum → label)
src/features/access/      permissions.ts (can, useCan, PermissionKeys)  PermissionButton.tsx  RequireTenant.tsx
src/features/members/     membersQuery.ts  MembersPage.tsx  AddRoleMenu.tsx  RemoveMemberDialog.tsx
src/features/invitations/ invitationsQuery.ts  InvitationsPage.tsx  InviteDialog.tsx
src/features/joinLinks/   joinLinksQuery.ts  JoinLinksPage.tsx  CreateJoinLinkDialog.tsx  ShowLinkDialog.tsx  joinUrl.ts
src/features/acceptance/  outcomeSearch.ts  AcceptancePage.tsx (invitation | join link)
src/features/mailbox/     MailboxPage.tsx
src/routes/               members.tsx  invitations.tsx  join-links.tsx  invite.$token.tsx  join.$token.tsx  mailbox.tsx

samples/GroundUp.Sample/Dev/   DevMailbox.cs  DevMailboxInvitationEmailSender.cs  DevSettingsSeeder.cs
                               SampleDevToolsServiceCollectionExtensions.cs
samples/GroundUp.Sample/Controllers/DevController.cs   (GET /api/dev/invitation-emails)
```

PR split: 90a = shared api/access pieces + members; 90b = invitations + join links; 90c = acceptance + mailbox + sample host + TESTING.md.

## 1. API layer (`src/api/`)

All calls go through `apiFetch` (CSRF, `ApiError`, `OperationResult` unwrapping, inspector). Types are hand-written in `types.ts`, as today.

**Paging** (`paging.ts`): `PaginatedData<T> = { items, pageNumber, pageSize, totalRecords, totalPages }` and `pageQuery(page, size = 25)` → `?pageNumber=…&pageSize=…`. Pages are 1-based in the API; MUI's `TablePagination` is 0-based, the pages convert.

**Enums** (`labels.ts`): numbers from the API mapped once:

| Enum | Values |
|---|---|
| `MembershipStatus` | 0 Active, 1 Inactive, 2 Pending |
| `RoleAssignmentSource` | 0 Manual, 1 Invitation, 2 Join link, 3 Default role, 4 Provisioning |
| `InvitationStatus` | 0 Pending, 1 Accepted, 2 Revoked, 3 Expired |
| `JoinLinkStatus` | 0 Active, 1 Revoked, 2 Expired, 3 Used up |

Each is a `const` object plus a `label(value)` helper that returns `Unknown (n)` for a value it does not know.

**Functions:**

| File | Function | Request |
|---|---|---|
| members.ts | `listMembers(page)` | `GET /api/members?pageNumber&pageSize` |
| | `removeMember(userId)` | `DELETE /api/members/{userId}` |
| | `addMemberRole(userId, roleId)` | `POST /api/members/{userId}/roles/{roleId}` |
| | `removeMemberRole(userId, roleId)` | `DELETE /api/members/{userId}/roles/{roleId}` |
| roles.ts | `listRoles()` | `GET /api/roles?pageSize=100` (the picker; a tenant has few roles) |
| invitations.ts | `listInvitations(page)`, `createInvitation({ email, roleId, expiresAt? })`, `resendInvitation(id)`, `revokeInvitation(id)` | `/api/invitations`, `POST …/{id}/resend`, `POST …/{id}/revoke` |
| joinLinks.ts | `listJoinLinks(page)`, `createJoinLink({ roleId, expiresAt?, maxUses? })`, `regenerateJoinLink(id)`, `revokeJoinLink(id)` | `/api/join-links`, `POST …/{id}/regenerate`, `POST …/{id}/revoke` |
| acceptance.ts | `lookupInvitation(token)`, `acceptInvitation(token, { forceLogin, returnUrl })`, `lookupJoinLink(token)`, `joinWithLink(token, { forceLogin, returnUrl })` | `POST /auth/invitations/lookup`, `POST /auth/invitations/accept?returnUrl=`, the join-link pair |
| devMailbox.ts | `listDevEmails()` | `GET /api/dev/invitation-emails` |

Dates are sent as ISO strings (UTC); `expiresAt` from a date input means the end of that day in the browser's time zone, converted to UTC.

**`MeDto`** gains `allPermissions: boolean` and `permissions: string[]` (GU-96).

## 2. Access helpers (`src/features/access/`)

- `PermissionKeys`: the keys the pages use (`auth.members.read`, `auth.members.manage`, `auth.roles.assign`, `auth.roles.read`, `auth.invitations.send`, `auth.joinlinks.manage`), copied from `PermissionKeysAuth`.
- `can(me, key)`: `me?.allPermissions || me?.permissions.includes(key)`. `useCan(key)` reads `useSession().me`.
- `PermissionButton`: an MUI `Button` that is disabled when `!can`, wrapped in a `Tooltip` "Needs the permission auth.members.manage" (a `span` wrapper so the tooltip works on a disabled button). Also used for icon buttons (`PermissionIconButton`).
- `RequireTenant`: renders its children only when signed in with a current tenant. Signed out: the existing signed-out panel. No tenant: "Choose an organization to manage its members" and a button that calls `requestTenantPicker()`.

The UI only disables what the user's permissions rule out. The API's own rules (only admins grant TenantAdmin, the last admin, the escalation guard) are left to the API, and its refusal is shown.

## 3. Pages

All three admin pages: `RequireTenant` → a `useQuery` keyed by `[area, tenantId, page]` (so a tenant switch never shows the previous tenant's rows, on top of `onSessionChanged` resetting them) → MUI `Table` + `TablePagination` (25, no size choice). A load error (including 403) is the page's `ErrorPanel`. Mutations: `useMutation`; on success invalidate `[area]`; on error the `ErrorPanel` shows inline under the table's toolbar (one "last action" error per page, cleared by the next action).

**Members** (`/members`):
- Columns: name and email, status chip (Active, Pending), roles, actions.
- Roles: a `Chip` per role; label `TenantAdmin` or `Editor · Invitation, Manual` when there are sources other than Manual or when more than one; `onDelete` (needs `auth.roles.assign`) removes the role.
- Actions: **Add role** (`AddRoleMenu`: a menu of `listRoles()` minus the roles held; disabled while roles load) and **Remove** (`RemoveMemberDialog`: "Remove Bob Member (member@example.com) from My Test Org? Their roles are removed and their sessions end."). Both permission-aware.
- After a change that touches the signed-in user (`userId === me.userId`): removing themselves → `onSessionChanged` (their access ends); a role change → `onSessionRefreshed` (permissions in `/auth/me` are resolved per request, so the session query reloads them).

**Invitations** (`/invitations`):
- Columns: email, role, status chip, expires, created; actions **Resend** and **Revoke** (Pending and Expired only; revoke confirms).
- **Invite** opens `InviteDialog` (email, role picker, optional expiry date). On success: close, invalidate, and in Vite dev mode a note "Development: the email is in the Mailbox" with a link.
- Needs `auth.invitations.send` (all actions).

**Join links** (`/join-links`):
- Columns: role, status chip, uses (`2 / 10` or `2 / unlimited`), expires, created; actions **Regenerate** (confirm: "The current link stops working") and **Revoke** (confirm).
- **Create** opens `CreateJoinLinkDialog` (role, optional expiry date, optional maximum uses ≥ 1).
- Create and regenerate open `ShowLinkDialog` with `joinUrl(token)` = `${window.location.origin}/join/${encodeURIComponent(token)}`, a copy button (`navigator.clipboard`, with a fallback of selecting the text) and "This link is shown only now. Regenerate to get a new one."
- Needs `auth.joinlinks.manage`.

**Acceptance** (`/invite/$token`, `/join/$token`): one `AcceptancePage` with `kind: 'invitation' | 'joinLink'`.
- Search params (`outcomeSearch.ts`, zod, same rules as the Session page): `result`, `alreadyMember`, `error`, `idpError`. Unknown or malformed values are dropped.
- **No outcome:** `useQuery(lookup)`. Usable → "Join **My Test Org** as **Member**" with **Accept** (or **Join**) and **Sign in as someone else**. Not usable → the reason in words (`not_found` "This link is not valid.", `revoked` "This invitation was withdrawn.", `expired` "This invitation has expired. Ask for a new one.", `accepted` "This invitation was already used.", `used_up` "This link has reached its maximum number of uses."), no buttons.
- **Start:** `acceptInvitation(token, { forceLogin, returnUrl: '/invite/' + encodeURIComponent(token) })` → `window.location.assign(redirectUrl)`. Start errors → `ErrorPanel`.
- **With an outcome:** the page copies it into component state on first render, calls `onSessionChanged` once (a `useEffect` with a ref guard), and replaces the URL without the outcome keys (`navigate({ search: {}, replace: true })`), so a reload does not repeat it. It shows: `signed_in` → "You joined My Test Org" (organization name from the refreshed session's current tenant) and links to Session and Members; `alreadyMember` → "You are already a member of My Test Org"; `tenant_picker` / `no_tenant` → the Session page's wording and a link to Session; `error` → `outcomeMessages` text, and for `INVITATION_EMAIL_MISMATCH` the **Sign in as someone else** button (the token is still in the path).
- Works signed out and signed in; the root loader already loads the session.

**Mailbox** (`/mailbox`, nav entry only when `import.meta.env.DEV`): `useQuery(listDevEmails)` with a Refresh button; a table of time, to, organization, role, expires, and the link (opens the accept page in the same tab). 404 → "The dev mailbox is not available: the API is not running in Development." 401 → the signed-out panel.

## 4. Routes and navigation

- `members.tsx`, `invitations.tsx`, `join-links.tsx` replace their `Placeholder`.
- New `invite.$token.tsx`, `join.$token.tsx` (with `validateSearch: outcomeSearchSchema`) and `mailbox.tsx`. None starts with a proxied prefix (`routes.test.ts` covers it).
- `NavMenu`: add Mailbox (dev only). The accept and join pages are not in the menu.

## 5. Sample host (`samples/GroundUp.Sample`)

`Program.cs` calls `builder.Services.AddSampleDevTools(builder.Environment)` after `AddGroundUpAuth`. Outside Development it registers nothing.

In Development:
- `DevMailbox` (singleton): the last 50 `DevEmail(Guid InvitationId, string To, string OrganizationName, string RoleName, string AcceptUrl, DateTime ExpiresAt, DateTime SentAt)`, newest first, thread-safe (a lock around a list).
- `DevMailboxInvitationEmailSender : IInvitationEmailSender` replaces the default (`services.Replace(ServiceDescriptor.Scoped<…>)`, since `AddGroundUpAuth` used `TryAddScoped`). It stores the email and logs the same line as the default sender.
- `DevSettingsSeeder : IDataSeeder`, `Order` 110 (after the auth settings seeder, 30): finds the `auth.invitations.accept-url` definition and the System level (`SettingLevel` with no parent); when the System level has no value (no row with `ScopeId` null, or a row with an empty value), sets `http://localhost:5173/invite/{token}`. Any other existing value is kept. Writes through `SampleDbContext` (the settings service is behind the permission proxy and seeders run without a user).

`DevController` `[Route("api/dev")]`: `GET invitation-emails` → 404 when `DevMailbox` is not registered (outside Development), 401 when not signed in, otherwise `OperationResult<List<DevEmail>>`. Not under the framework; sample only. The accept URLs contain tokens (credentials): acceptable for a development tool, said in the XML docs and TESTING.md.

## 6. Tests

**Web (vitest, Node, `mockFetch`):**
- each API function: method, path (with the paging query), body, and the mapped result; acceptance functions send `forceLogin` and the `returnUrl` query
- `labels.ts` (known and unknown values), `can` (allPermissions, listed key, missing key, signed out)
- `joinUrl` (encoding), `outcomeSearch` (valid values kept, crafted values dropped)
- the route test still passes with the new routes

**.NET (`GroundUp.Sample.Tests.Integration`):**
- `DevMailbox` keeps the newest 50 and orders newest first (plain unit-style test in the project)
- `AddSampleDevTools` with a Production environment registers neither the mailbox nor the sender
- `GET /api/dev/invitation-emails`: signed out 401; signed in returns the stored emails; with `DevMailbox` removed from the host → 404
- `DevSettingsSeeder`: sets the URL when there is no value or an empty one; keeps an existing value

## 7. Docs

- TESTING.md Part 5 (90c): Alice invites Carol from the Invitations page, Carol opens the Mailbox link and accepts (registration), Members shows Carol; resend and revoke; a join link for Bob (create, copy, join signed out and as an existing member); refusals: remove the last TenantAdmin (`LAST_ADMIN`), a member without `auth.members.manage` sees Remove disabled.
- The "Parts to Come" table drops Part 5; the API guide's Parts 15 and 16 point to it.

## 8. Risks

- **Clipboard:** `navigator.clipboard` needs a secure context; `http://localhost` counts as one. The fallback selects the text.
- **Tooltips on disabled buttons:** MUI needs a wrapper element; `PermissionButton` provides it.
- **The `next`/outcome flow on the acceptance page** must not loop: the outcome is removed from the URL with `replace`, and the session refresh runs once per mount.
