# Tasks: CSRF Protection, Cookie Hardening and Session Timeouts

Rules: one numbered task group at a time. After each group: build, run tests, commit, then stop for review. Never commit to main. Branch: `phase-10c/csrf-and-session-hardening`. Test baselines in the session brief must not get worse. Tests are written with each group.

PR boundaries: PR A = groups 1-2, PR B = groups 3-4, PR C = groups 5-6, PR D = groups 7-8.

- [x] 1. Settings foundation
  - [x] 1.1 Document the scope-chain contract (most specific first) on `IScopeChainProvider`; reorder `DefaultScopeChainProvider` to emit Tenant, then System (the resolution engine already takes the first match and needs no change). Its unit tests and the `AuthFlowTestFixture` comment describe an older behavior (Tenant only, empty without a tenant) and need updating
  - [x] 1.2 Impact check: find settings, seeders, tests and sample data with values at both System and Tenant; update tests that assumed the old order
  - [x] 1.3 Add optional `MinValue` and `MaxValue` to `EnsureSettingDefinitionRequest` and map them in `EnsureDefinitionAsync`
  - [x] 1.4 Add `ISettingWriteValidator` (Core) and call all registered validators from `SettingsService.SetAsync` after the built-in checks; check whether `CustomValidatorType` is used anywhere and note the result
  - [x] 1.5 Unit tests: five-level chain (User, Group, Case, Tenant, System) resolves most specific first; Min/Max set by `EnsureDefinitionAsync`; validator rejection returns 400; no validators registered changes nothing

- [x] 2. Session settings and policy provider
  - [x] 2.1 `DefaultAuthSettingsSeeder`: add `auth.session.token-lifetime-minutes` (2-120, default 60) and `auth.session.absolute-lifetime-minutes` (10-1440, default 480), System and Tenant levels, group "Security > Sessions". Seeded defaults are the fixed 60 and 480, not `AuthOptions` values: the seeder lives in `Auth.Data.Postgres`, which `AuthOptions` (in `Auth.Services`) cannot be referenced from. `AuthOptions` stays as the runtime fallback in the provider
  - [x] 2.2 `ISessionPolicyProvider` and `SessionPolicy`: tenant chain `[Tenant, System]` built from level names, clamp to System, cross-field rule, fallback to `AuthOptions` on failure
  - [x] 2.3 `SessionPolicyWriteValidator` (registered by the auth module): tenant not above System (read from the database), `absolute >= token` at the same level
  - [x] 2.4 Unit tests: clamp, cross-field, fallback, validator cases, seeder bounds; integration test: set a tenant value above System is rejected, below is accepted and resolved
  - [ ] 2.5 Stop for review (end of PR A)

- [x] 3. Use the policy everywhere
  - [x] 3.1 `TokenService`: `exp` from the policy (tenant token: tenant policy; tenantless: System policy), capped at `auth_time` plus absolute lifetime
  - [x] 3.2 `AuthCookieWriter`: expiry from the token `exp`; remove the sync-over-async domain lookup; update `IAuthCookieWriter` and callers
  - [x] 3.3 `TokenRefreshMiddleware`: resolve the policy from the token's `tid`
  - [x] 3.4 `POST /auth/refresh`, `set-tenant` and tenantless re-issue enforce the absolute cap with the right policy; add `ErrorCodes.ReauthRequired` and return `401 REAUTH_REQUIRED`
  - [x] 3.5 Unit tests for each consumer; integration test: change a timeout through the settings API and see the next token and cookie use it; `/auth/refresh` past the cap returns `REAUTH_REQUIRED`; stricter tenant cap on `set-tenant`
  - [x] 3.6 Stop for review. Found and fixed GU-78 on the way (event bus was a singleton, so settings cache invalidation never ran in Development)

- [x] 4. CSRF and cookie hardening
  - [x] 4.1 Register antiforgery (header `X-CSRF-Token`, cookie `CsrfToken`, HttpOnly, Strict, Secure from `CookieSecure`). As built: in a new `AddGroundUpAuthApi()` (see design 2.1); `UseGroundUpAuth` throws without it
  - [x] 4.2 `CsrfProtectionMiddleware`: fail closed without `IAntiforgery`; TRACE is safe; enforced only when authenticated by the cookie
  - [x] 4.3 `GET /auth/csrf`; remove `ProvisionAntiforgeryToken()` and its three call sites; clear the antiforgery cookie on logout
  - [x] 4.4 Startup validator: refuse to start when `CookieSecure=false` outside Development, warn in Development; warn on a single-label default domain (host resolution is always on with `UseGroundUpAuth`)
  - [x] 4.5 Unit tests asserting attributes of `AuthToken`, `IdToken`, `AuthState` and `CsrfToken`; middleware cases; startup validator cases
  - [x] 4.6 Integration tests on the cookie path: no token 403, valid token succeeds, Bearer succeeds, GET never blocked; add a test helper that builds cookie plus token requests
  - [x] 4.7 Stop for review (end of PR B)

- [x] 5. Swagger script (sample)
  - [x] 5.1 Add the Swagger UI script that fetches `/auth/csrf` once, adds `X-CSRF-Token` to non-safe requests, and refetches once after a 403; wire it into `UseSwaggerUI`. As built: Swashbuckle request/response interceptors (`SwaggerCsrfInterceptors`); a 403 clears the cached token so the next request refetches (Swagger UI cannot retry a request itself)
  - [ ] 5.2 Manual check: POST from Swagger with the cookie succeeds (owner, in the browser). Automated: sample test checks the interceptors are served; a Node run through Swashbuckle 6.9's parser checked GET untouched, POST gets the token, caching, refetch after 403, no header when signed out
  - [x] 5.3 Stop for review (moved into PR B)

- [x] 6. ID token validation, `auth_time` and forced re-authentication
  - [x] 6.1 `IIdTokenValidator` (interface in Services, Keycloak implementation): signature via realm JWKS, `iss`, `aud`, `exp`, `nonce`; returns `AuthTime`. As built: separate interface (not on `IIdentityProviderService`); issuer accepted at the public or internal base URL; JWKS fetched from the internal URL; `azp` checked when there are several audiences
  - [x] 6.2 Replace the three `ValidateNonce` copies in `LoginFlowHandler`, `SimpleSignupFlowHandler`, `NewOrganizationFlowHandler`; use `validated.AuthTime ?? now` with a warning when missing (shared `CallbackSessionGuard`)
  - [x] 6.3 `AuthUrlRequest`: add `MaxAgeSeconds` and `ForceLogin`; `AuthUrlBuilderService` writes `max_age` and `prompt=login`; `POST /auth/login` accepts `forceLogin`
  - [x] 6.4 Stale session at the callback: if `auth_time` is older than the cap after login, write no cookie and return the existing JSON error shape with `401` and `errorCode: REAUTH_REQUIRED`. The API never redirects. The client then calls `POST /auth/login?forceLogin=true`, receives a new `redirectUrl` and performs the redirect itself, as for any login. Checked against the System lifetime right after validation and against the tenant's lifetime before any join or token, so a refused sign-in changes nothing. Tests confirm `/auth/login` and `/auth/signup` still return `redirectUrl` (logout returns `logoutUrl`, unchanged and covered by existing tests)
  - [x] 6.5 Unit tests: validator cases (valid, bad signature, unsigned, wrong iss, wrong aud, azp, expired, wrong nonce, key rotation, JWKS down); fresh login, silent login keeps old `auth_time`, silent login past cap, stricter tenant cap, missing claim; URL builder cases
  - [x] 6.6 Stop for review (end of PR C)

- [x] 7. Keycloak realm, verification and documentation
  - [x] 7.1 `keycloak/realm.json`: `ssoSessionIdleTimeout` 1800, `ssoSessionMaxLifespan` 28800
  - [x] 7.2 Verify against Keycloak 26.3: `auth_time` present on silent login and unchanged, changes after `prompt=login`, `max_age` forces the form; add a mapper to `realm.json` if needed; record results. Steps: `MANUAL_TESTING.md` Part 14 (owner, in the browser). Results so far: 14.1 pass (`max_age=28800`, no `prompt`). 14.2 pass: `auth_time` present without a mapper; a silent sign-on about 5 minutes later kept `auth_time` 1795273501 (first token `iat` 1795273642, second `iat` 1795273964). 14.3 pass: `forceLogin=true` gave `prompt=login&max_age=28800`; after signing in, `auth_time` moved to 1795274099 (equal to the new `iat`). 14.4 (`max_age` forcing the form for an old session) skipped by the owner: it only confirms Keycloak honors the standard `max_age` parameter; the app side (`max_age` in the URL, `REAUTH_REQUIRED` for a stale session) is covered by automated tests. No mapper needed in `realm.json`
  - [x] 7.3 Startup warning when the System absolute lifetime exceeds the realm's SSO Session Max. Decided: documented, not built. Reading the realm needs an admin API call to Keycloak at every start (Keycloak may not be up yet, and the startup validators only check configuration today). `ARCHITECTURE.md` "Sessions" says to keep the two in step by policy
  - [x] 7.4 `MANUAL_TESTING.md`: Part 1.2 realm values for an existing realm; CSRF header and `/auth/csrf` for Swagger and REST clients; Part 11 uses the settings API (no `appsettings` edit, no restart); new Part 14 proves a silent login keeps `auth_time`, `forceLogin` shows the form, and `max_age` forces the form for an old session; Recipe 3 uses the settings API; troubleshooting rows for `CSRF_VALIDATION_FAILED`, `REAUTH_REQUIRED`, `ID_TOKEN_INVALID`, blank Swagger, single-label domain
  - [x] 7.5 Administrator note in `ARCHITECTURE.md`: settings cascade order, session timeouts (tenants only shorten, realm values are the outer limit and the system administrator's concern), cookie table, CSRF client contract, cache staleness until GU-47
  - [x] 7.6 Token-revocation spec: entries are kept for the setting's maximum token lifetime (120 minutes), because the System value can change while older tokens live; the default token lifetime is now the seeded setting

- [x] 8. Final review
  - [x] 8.1 Full code review against requirements 1 to 6 and the cookie table; run all test suites against the baselines. Review on main after PR #39 found every requirement met except 4.7 (moved to GU-77) and 7.3 (decided: documented). Tests on main: unit 1543 pass / 12 known failures; integration 340 pass / 48 known failures (41 Setup + 7 auth); sample 22/22
  - [x] 8.2 Settings authorization is GU-77. Found and fixed GU-78 (event bus lifetime) on the way. GU-72 closed. A story for issuing tokens to non-browser clients is the owner's call (not created)
  - [x] 8.3 PRs: #36 (settings foundation, session settings), #37 (policy everywhere, CSRF, GU-78), #38 (ID token validation, auth_time, max_age, forceLogin, Swagger fix), #39 (realm limits, docs)
