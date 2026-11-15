# Requirements: CSRF Protection, Cookie Hardening and Session Timeouts

## Context

A review of the auth cookie handling, plus the manual testing of token refresh and login, found four related gaps. They are grouped in one spec because they touch the same code (cookie writer, auth middleware, login handlers, `AuthOptions`) and the same compliance concerns (NIST SP 800-53 session and authentication controls, FedRAMP, CIS).

1. **CSRF protection does not run.** `CsrfProtectionMiddleware` only validates a token when `IAntiforgery` is registered, and antiforgery services are never registered, so the middleware skips every request. The controller's `ProvisionAntiforgeryToken()` generates a token pair and discards the request token, so no client can ever receive one. State-changing requests authenticated by cookie are not protected. The integration tests send POSTs with a Bearer header (`CreateBearerRequest`) to avoid the check, so no test exercises the cookie path.
2. **Cookie settings need a compliance review.** The auth cookie is HttpOnly. The other cookies and their SameSite, Secure, Domain and expiry settings have not been reviewed against what each one needs.
3. **`auth_time` does not mean what the absolute session cap assumes.** `AbsoluteSessionLifetimeMinutes` is meant to force a fresh sign-in a fixed time after the user actually authenticated. Every login handler sets `auth_time` to the moment the callback runs. When Keycloak signs the user in silently (its SSO session is still alive), `auth_time` resets without the user proving anything, so the cap never forces re-authentication while the Keycloak session lives. This was seen during manual testing: the app token expired, login returned immediately with no form.
4. **Session timeouts are fixed in `appsettings.json`.** `TokenExpirationMinutes` and `AbsoluteSessionLifetimeMinutes` live in `AuthOptions`. System administrators and tenant administrators need to change them at runtime, through the API or UI, without editing a backend file and restarting. This also matches the framework rule that settings are database-driven.

## Scope

**In scope**
- Working CSRF protection for cookie-authenticated requests, including the way a client obtains the token.
- Review and hardening of every auth cookie.
- Using the identity provider's `auth_time`, and forcing re-authentication when the cap is reached.
- Session timeouts as runtime settings with system and tenant levels.
- Tests that exercise the cookie path, and updates to `MANUAL_TESTING.md`.

**Out of scope**
- Changing Keycloak's own SSO Session Idle and Max values from the application (see Requirement 4.9).
- Distributed settings cache invalidation (Phase 15, caching module).
- Token revocation (`phase-10c-token-revocation`), except where noted in Requirement 4.10.
- Enterprise tenants and their separate realms (Phase 10D).

## Requirements

### Requirement 1: CSRF protection that works

**User story:** As a user signed in with a cookie, I am protected from other sites making state-changing requests in my name.

#### Acceptance Criteria

1. Antiforgery services SHALL be registered by `AddGroundUpAuth` with an explicit request-header name (not the framework default) and an HttpOnly, Secure-in-production, SameSite antiforgery cookie.
2. For requests authenticated by the `AuthToken` cookie, every method other than GET, HEAD, OPTIONS and TRACE SHALL be rejected with `403 CSRF_VALIDATION_FAILED` unless a valid antiforgery token is supplied in the header.
3. Requests authenticated by an `Authorization: Bearer` header SHALL remain exempt (a browser does not attach that header on its own).
4. The `/auth/callback` GET SHALL remain protected by the existing state cookie and nonce, not by the antiforgery token.
5. A client SHALL have a documented way to obtain the request token after sign-in and after a token refresh. The design SHALL choose between a response header on `GET /auth/me`, a dedicated `GET /auth/csrf` endpoint, or both. `ProvisionAntiforgeryToken()` SHALL either deliver the token or be removed.
6. Integration tests SHALL cover the cookie path: a POST with the cookie and no token returns 403; with a valid token succeeds; with a Bearer header succeeds without a token; a GET is never blocked.
7. The Swagger UI and `MANUAL_TESTING.md` SHALL keep working. Swagger posts with the cookie, so the design SHALL state how it supplies the token (for example an operation filter or a documented header) or how a development-only exemption is limited so it cannot reach production.

### Requirement 2: Cookie hardening review

**User story:** As a security reviewer, each cookie has the strictest settings it can work with, and the reasons are written down.

#### Acceptance Criteria

1. The design SHALL list every cookie the auth module sets (`AuthToken`, `IdToken`, `AuthState`, the antiforgery cookie) with its purpose, HttpOnly, Secure, SameSite, Domain, Path and expiry, and justify each.
2. All of them SHALL be HttpOnly unless the design documents a reason a script must read one, and no cookie SHALL carry a token a script does not need.
3. `AuthState` must survive the cross-site redirect back from Keycloak, so it cannot be `SameSite=Strict`. The design SHALL pick the strictest value that works. `AuthToken` SHALL use the strictest value that still works for the supported client types.
4. Secure SHALL default to true and only be false in an explicitly development configuration. Startup SHALL log a warning, or refuse to start outside Development, if it is false.
5. The cookie `Domain` (`.<default-domain>`) SHALL be reviewed for how widely it exposes the cookie across subdomains, and for the single-label host `localhost` case used in development.
6. Cookie expiry SHALL follow the effective token lifetime from Requirement 4, not a fixed configured value.
7. Unit tests SHALL assert the attributes written for each cookie.

### Requirement 3: A trustworthy `auth_time`, and forced re-authentication at the cap

**User story:** As a security reviewer, the absolute session limit is counted from when the user last proved who they are, and cannot be restarted by a silent single sign-on.

#### Acceptance Criteria

1. Login handlers SHALL take `auth_time` from the validated identity-provider ID token's `auth_time` claim instead of the current time. If the claim is missing, the handler SHALL fall back to the current time and log a warning without the token contents.
2. The design SHALL confirm against the pinned Keycloak version (26.3) that the ID token carries `auth_time` for a silent SSO login, and that the claim changes after a forced login.
3. `auth_time` SHALL be carried unchanged through `set-tenant`, refresh, leave and tenantless re-issue (already true) so the cap measures from the real authentication.
4. When the absolute lifetime has passed, the application SHALL make the next sign-in require credentials. The design SHALL choose how: always send `max_age` (the effective absolute lifetime, in seconds) on the authorization request so the identity provider re-authenticates a stale session, and/or send `prompt=login` after the application has rejected a session for age. Either way a silent SSO login SHALL NOT produce a token whose `auth_time` is older than the cap.
5. After a login, a token whose `auth_time` is already older than the effective absolute lifetime SHALL be rejected with `401` and a machine-readable code (for example `REAUTH_REQUIRED`) so the client can start a forced login.
6. Tests SHALL cover: a fresh login (new `auth_time`), a silent login (old `auth_time` preserved), a silent login past the cap (rejected with `REAUTH_REQUIRED`), and the missing-claim fallback.

### Requirement 4: Session timeouts are runtime settings

**User story:** As a system administrator or a tenant administrator, I change session timeouts from the API or UI and they apply without a restart.

#### Acceptance Criteria

1. Two settings SHALL be defined in the Settings module, seeded by `DefaultAuthSettingsSeeder` with defaults taken from `AuthOptions`, each with metadata (data type integer, group "Security > Sessions", description, a minimum and a maximum, UI hint number):
   - `auth.session.token-lifetime-minutes`: how long a token is valid. It also acts as the inactivity timeout, because a user who makes no request for that long has an expired token.
   - `auth.session.absolute-lifetime-minutes`: the longest a session can last from the user's real authentication, however active they are.
2. Both settings SHALL be definable at the System level and at the Tenant level, and resolve through the existing cascade: Tenant, then System, then default.
3. **A tenant value SHALL NOT be longer than the System value.** A tenant administrator may shorten a timeout for their organization but never lengthen it beyond what the system administrator allows. The check SHALL run when the value is set and again when it is resolved, so lowering the System value later also lowers any larger Tenant value.
4. The cross-field rule `absolute-lifetime >= token-lifetime` SHALL be enforced when either value is set, at each level, and the effective pair SHALL be re-checked at resolution time. The current startup-only validation of `AuthOptions` remains as the check on the fallback defaults.
5. The effective values SHALL be used by: token generation (`exp`), the auth cookie expiry, `TokenRefreshMiddleware` (half-lifetime refresh and the absolute cap), `set-tenant` (absolute cap), and the tenantless token. A tenant-scoped token uses the effective values for its tenant. A tenantless token (no tenant yet) uses the System values.
6. A change SHALL apply from the next token issue or refresh: existing tokens keep their `exp`, but the absolute cap is evaluated against the current effective value at refresh and `set-tenant`, so shortening takes effect without waiting for tokens to expire.
7. Setting the System level SHALL require a system administrator. Setting a tenant's level SHALL require TenantAdmin of that tenant (or a system administrator). Authorization lives in the service layer, as with all other settings. Every change SHALL publish the existing `SettingChangedEvent` so it is auditable.
8. The per-request lookup in `TokenRefreshMiddleware` SHALL use the settings cache (no database query on a cache hit) and SHALL resolve the scope chain from the token's `tid` explicitly, because that middleware runs before tenant resolution.
9. Keycloak's own SSO Session Idle and SSO Session Max apply to every user of the shared realm and cannot be set per tenant. They remain a system-administrator concern in the Keycloak admin console. The app-enforced limits in this requirement are the lever tenant administrators have. The design SHALL state this boundary in the administrator documentation. Changing Keycloak's realm values from GroundUp's own API is out of scope for this spec.
10. The token-revocation spec keeps revocation entries only as long as the longest possible token lifetime. With tenant overrides never longer than the System value, that bound is the System value, and the revocation design SHALL use it.
11. Until the caching module (Phase 15) exists, a change is visible immediately on the instance that handled it and after the settings cache TTL on other instances. This SHALL be documented.

### Requirement 5: Tests and manual verification

#### Acceptance Criteria

1. Unit and integration tests for each requirement above, including tenant values rejected above the System value and the cascade (tenant overrides system overrides default).
2. An integration test that changes a timeout through the settings API and sees the next issued token and cookie use the new value without a restart.
3. `MANUAL_TESTING.md` SHALL be updated: the CSRF header on POSTs (Swagger), Part 11 using the setting instead of editing `appsettings.Development.json`, and a step that proves a silent login past the absolute cap is forced to show the login form.

## Open Questions for the Design

1. **Ceiling rule (Requirement 4.3).** Recommended: tenants can only shorten. If you want tenants able to lengthen, say so; it conflicts with a system administrator being able to set a compliance maximum.
2. **Tenant unknown at login.** At the moment of login the user may not have chosen a tenant. Recommended: send `max_age` using the System absolute lifetime, then after the tenant is selected apply that tenant's (shorter) cap in `set-tenant`, returning `REAUTH_REQUIRED` if the session is already older than it. Host-pinned logins know the tenant and can send its value directly.
3. **Keycloak realm values.** Which SSO Session Idle and Max does your policy require? They are not set in `realm.json` today (Keycloak defaults: 30 minutes idle, 10 hours max).
4. **Minimum and maximum bounds** for the two settings (for example token lifetime from 5 to 120 minutes, absolute lifetime from 30 minutes to 24 hours).
5. **Swagger and CSRF (Requirement 1.7).** Prefer Swagger supplying the token, or a Development-only exemption.
