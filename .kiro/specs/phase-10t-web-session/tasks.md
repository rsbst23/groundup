# Tasks: Web App Sign-In, Session and Tenants (GU-89)

Execute one top-level task at a time. After each: web build, lint and tests (the .NET code does not change), commit, stop for review.

Baselines (main after #55): web 70 / 0; unit 2010 / 0; integration 539 / 0; sample 22/22.

Requirements: `requirements.md`. Design: `design.md`. One PR (branch `phase-10/web-session`).

## Gap analysis (before tasks)

- **`next` survives the callback.** `CallbackRedirect` parses the return URL's query and rebuilds it, so `next` comes back decoded and re-encoded with the same value. A test in task 1 checks `buildReturnUrl` output is a valid API return URL (`ReturnUrlRules` accepts it), and the GU-87 tests already cover keeping existing query values.
- **`set-tenant` can answer `{ tenants }`** (selection still required). The app always sends a tenant id, so any 200 is success; the session refetch shows the result.
- **Node test environment.** The return logic takes a location object instead of reading `window`, so it runs under Vitest without a DOM. Only `useStartSignIn` touches `window`.
- **Sign out of this app** is a cookie POST, so it needs CSRF; `apiFetch` handles it, and `onSessionChanged` drops the token afterwards.
- **The tenant picker after sign-in** comes back as `result=tenant_picker`; the organization list moves to the top (requirement 3.7) based on the session, not only the outcome, so a reload still shows it first.

## Tasks

- [x] 1. Endpoint functions, return logic and messages (design 1, 2, 4 messages)
  - [x] 1.1 `api/types.ts` and `api/auth.ts`: `login`, `signup`, `setTenant`, `refreshSession`, `createOrganization`, `leaveTenant`, `logout`
  - [x] 1.2 `features/session/returnTo.ts` (`isAppPath`, `pageToReturnTo`, `buildReturnUrl`); `sessionSearchSchema` gains `next`
  - [x] 1.3 `features/session/outcomeMessages.ts` (also `FLOW_FAILED`)
  - [x] 1.4 Tests: `auth.test.ts` (8), `returnTo.test.ts`, `sessionSearch.test.ts` additions, `outcomeMessages.test.ts`. Noted: while signed out, no CSRF token is kept, so each POST first gets a 401 from `/auth/csrf` (one extra call, harmless; the request is sent without the header as intended)
  - [x] 1.5 Check: web build, lint (0 problems), tests 117 / 0

- [ ] 2. The Session page and the sign-in entry points (design 3, 4, 5)
  - [ ] 2.1 `useStartSignIn`, `useSessionActions`
  - [ ] 2.2 Session page sections: `CallbackOutcome` with landing handling, `SignedOutPanel`, `SessionDetails`, `OrganizationsPanel` (switch, leave with confirmation, enterprise rows), `CreateOrganizationForm`, `SignOutPanel`
  - [ ] 2.3 `SessionButton` in the top bar; the banner's sign-in button; `ErrorPanel` "Sign in again" starts a sign-in
  - [ ] 2.4 Check: web build, lint, tests; a smoke run against the API (sign in, land back, switch, sign out)

- [ ] 3. Testing manual, manual run, PR
  - [ ] 3.1 TESTING.md Part 2 with the Sign in button; Part 3 (the API guide's Parts 2 to 10 and 14.1 to 14.3 from the web app); the API guide's pointers
  - [ ] 3.2 Run Parts 2 and 3 by hand (or record what is left for Rob)
  - [ ] 3.3 Full check; push; PR info
  - [ ] 3.4 Jira: GU-89 comment; Done after merge
