# Requirements: Post-Sign-In Redirect Back to the Client App (GU-87)

## Context

A browser sign-in ends at `GET /auth/callback`, which the API handles after Keycloak sends the browser back. Today the callback answers with JSON in every case, so the browser is left on an API address showing raw JSON, and the client app has to be reopened by hand. Every browser client needs to land back on one of its own pages instead, with the outcome. The sample web app (GU-85) is the first such client.

What exists:
- `AuthFlowState.ReturnUrl` (nullable, max 2048), `FlowInitiationRequest.ReturnUrl` and `InitiateAuthFlowRequest.ReturnUrl` already exist and are stored, but no endpoint sets them and the callback never reads them. **No migration is needed.**
- The callback's outcomes, from `AuthController.Callback` and `AuthFlowService.HandleCallbackAsync`:
  - signed in with a current tenant (JSON with `token`, `redirectUrl`, `alreadyMember`)
  - signed in with no current tenant: `requiresTenantSelection` true with a tenant list (the picker), or false with an empty list (no tenant yet)
  - a flow error (`errorCode`, `message`, a status), for example `INVITATION_EMAIL_MISMATCH`, `REAUTH_REQUIRED`
  - an error before any handler runs: missing or mismatched state cookie (`CSRF_STATE_MISMATCH`), unknown state (`STATE_NOT_FOUND`), already used (`FLOW_ALREADY_CONSUMED`), expired (`FLOW_EXPIRED`), already failed (`FLOW_FAILED`)
  - missing `code` or `state` (400 JSON)
- When the identity provider itself reports an error (for example `error=access_denied`), it calls the callback with `error` and `state` but no `code`. Today that is the "missing code" 400, and the flow state is left pending.
- Flows are started by `POST /auth/login` (`forceLogin` as a query parameter), `POST /auth/signup`, `POST /auth/invitations/accept` and `POST /auth/join-links/join`. All go through `AuthFlowService.InitiateFlowAsync`.
- Logout builds its post-logout address from the request host (`/`), which the dev realm allows.

## Decisions (with Rob)

1. **Relative paths only.** A return URL is a path on the host the sign-in started from (for example `/session` or `/invite/abc`). Nothing absolute, protocol-relative or with a backslash. No allowlist setting; one can be added later if a client on another domain needs it.
2. **Logout is unchanged.** It always returns to `/`, because Keycloak needs every post-logout address registered exactly.
3. **`returnUrl` is a query parameter** on the four initiation endpoints, stored on `AuthFlowState`.
4. **Outcomes in the query string:** `result=signed_in`, `result=tenant_picker`, or `error=<CODE>` using the existing error codes. Without a return URL, the callback answers with today's JSON.

## Scope

**In scope:** validating and storing the return URL; redirecting from the callback for every outcome once the flow state is known; the identity provider's own error; a landing check in the web app (the real pages come in GU-89 and GU-90); tests; docs.

**Out of scope:**
- **Return URLs on another domain** (allowlist). Not needed: the web app is on the same origin.
- **A logout return URL** (Decision 2).
- **Native and non-browser clients** (GU-79). They keep the JSON response.
- **The web app's sign-in and accept pages** (GU-89, GU-90). This story makes the API ready and adds one landing check to the web app.
- **Removing `token` from the callback's JSON.** The JSON success response includes the session token in the body (the cookie is HttpOnly, so this is the only place a browser script could read it). The redirect path never includes it. Whether the JSON response should keep it is a separate question for GU-79 (native clients may rely on it); noted, not changed.

## Requirements

### Requirement 1: Accepting a return URL

1. `POST /auth/login`, `POST /auth/signup`, `POST /auth/invitations/accept` and `POST /auth/join-links/join` SHALL accept an optional `returnUrl` query parameter.
2. The service layer (`AuthFlowService.InitiateFlowAsync`, so SDK callers get the same rules) SHALL accept a return URL only if all of these hold:
   - it starts with a single `/` (not `//`)
   - it contains no `\`, no control characters and no fragment (`#`)
   - it is at most 2048 characters
   - it parses as a relative URI
   - it does not start with the callback path (`AuthOptions.CallbackPath`, default `/auth/callback`), which would loop
3. An invalid return URL SHALL be refused with `400` and the code `INVALID_RETURN_URL`, before any flow state is created and before any redirect to the identity provider.
4. A valid return URL SHALL be stored on the flow state as given. It is never read from the callback's query string.
5. An empty or missing `returnUrl` SHALL mean "no return URL" (today's behaviour).

### Requirement 2: Redirecting after the callback

When the callback can identify the flow state and that state has a return URL, it SHALL answer with `302 Found` to the return URL, on the host the callback was served from, with the outcome added to the query string:

| Outcome | Added to the return URL |
|---|---|
| Signed in with a current tenant | `result=signed_in` |
| Signed in through an invitation or join link by someone who was already a member | `result=signed_in&alreadyMember=true` |
| Signed in, no current tenant, organizations to choose from | `result=tenant_picker` |
| Signed in, no current tenant, no organizations yet | `result=no_tenant` |
| A flow error after the state was found (any code, including `REAUTH_REQUIRED`, `INVITATION_EMAIL_MISMATCH`, `FLOW_ALREADY_CONSUMED`, `FLOW_EXPIRED`, `FLOW_FAILED`, `CSRF_STATE_MISMATCH`) | `error=<CODE>` |

1. The redirect SHALL NOT include the token or any other secret. The cookie is set before the redirect, exactly as for the JSON response.
2. The outcome SHALL be added to any query string the return URL already has. If the return URL already contains `result`, `error` or `alreadyMember`, those values SHALL be replaced, not repeated.
3. **Finding the return URL on errors.** The callback SHALL look up the flow state by the `state` parameter to find its return URL, even when the state cookie is missing or does not match, or the state is consumed, expired or failed. The outcome is still the same error; only its delivery changes from JSON to a redirect. This is safe because the return URL was validated at initiation and is a path on the same host.
4. When the state is unknown (`STATE_NOT_FOUND`), the state has no return URL, or `code`/`state` are missing, the callback SHALL answer with today's JSON.
5. The flow's error message (human text) SHALL NOT be put in the URL. The client shows its own message for the code.

### Requirement 3: The identity provider's own error

1. When the callback receives `error` (and optionally `error_description`) from the identity provider, with a `state`, it SHALL mark that flow state failed (so it cannot be used later) and report the error code `IDP_ERROR`.
2. With a return URL, it redirects with `error=IDP_ERROR&idpError=<error>`, where `<error>` is the provider's value if it matches `^[a-z_]{1,64}$` (OAuth error codes such as `access_denied`, `login_required`), otherwise it is left out. `error_description` is never put in the URL.
3. Without a return URL, it answers `400` JSON with `errorCode: IDP_ERROR` and the provider's `error` (not the description).
4. The provider's error is logged with the flow state id (without the description's free text if it is longer than 200 characters).

### Requirement 4: No change for JSON clients

1. A flow started without a return URL SHALL produce exactly today's responses (status codes and bodies), so Swagger, the API guide and the existing tests keep working.
2. The identity-provider-error case (Requirement 3) is the one change for JSON clients: `400` with `IDP_ERROR` instead of "Missing code or state parameter", and the flow state is marked failed.

### Requirement 5: Web app check

1. The web app's Session placeholder page SHALL show the `result`, `alreadyMember`, `error` and `idpError` query values it was opened with (typed with a zod search schema), so the redirect can be tested by hand before GU-89 builds the real sign-in page.
2. TESTING.md gains a short part: sign in through Swagger on port 5173 with `returnUrl=/session` and land on the Session page with `result=signed_in` (or `tenant_picker`); then a bad return URL (`https://example.com`) is refused with `INVALID_RETURN_URL`.

### Requirement 6: Tests

1. Unit tests for the return URL rules: valid paths (with and without a query string), and each refused form (absolute `http://`, `https://`, protocol-relative `//evil`, `/\evil`, a backslash anywhere, a fragment, a control character, too long, the callback path, an empty string treated as none).
2. Unit tests for building the redirect: each outcome in the table; an existing query string kept; existing `result`/`error`/`alreadyMember` values replaced; values URL-encoded.
3. Unit tests for the callback: the redirect for each outcome; JSON when there is no return URL; JSON when the state is unknown; the cookie mismatch and consumed state redirecting with their codes; the identity-provider error marking the state failed and redirecting or answering JSON.
4. Integration tests (real database, mock identity provider): a login flow with a return URL ends in `302` to `/session?result=signed_in` with the cookie set; an invalid return URL is refused before a flow state row exists; an invitation flow with a return URL that fails the email check redirects with `error=INVITATION_EMAIL_MISMATCH`.
5. The web app's search schema for the Session page is covered by a unit test.

### Requirement 7: Documentation

1. ARCHITECTURE.md: the return URL rules, the outcome values, the JSON fallback and the `IDP_ERROR` case.
2. The API guide (MANUAL_TESTING.md) notes that `returnUrl` exists and that without it nothing changes.
