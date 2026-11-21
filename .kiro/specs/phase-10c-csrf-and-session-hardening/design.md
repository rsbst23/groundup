# Design: CSRF Protection, Cookie Hardening and Session Timeouts

Status: DRAFT for review. Decisions below were agreed in the design discussion (2026-11-20).
Items marked **(confirm)** are details to verify in code when the task is implemented.

## 1. Agreed decisions

| # | Decision |
|---|---|
| D1 | Swagger supplies the CSRF token. No Development exemption. A small Swagger UI script fetches `GET /auth/csrf` once on page load and adds `X-CSRF-Token` to every non-safe request. |
| D2 | Clients get the token from a new `GET /auth/csrf` returning `{ "token": "..." }`. `ProvisionAntiforgeryToken()` and its three call sites (callback x2, `/auth/me`) are removed. |
| D3 | `AuthToken` default `SameSite=Strict`. Antiforgery cookie HttpOnly, `SameSite=Strict`, Secure from `CookieSecure`. `IdToken` unchanged. `Secure=false` outside Development: refuse to start. Single-label default domain with host tenant resolution: log a warning. |
| D4 | Forced re-auth: always send `max_age` (System absolute lifetime, seconds). After `REAUTH_REQUIRED`, the client starts login with `forceLogin=true`, which adds `prompt=login`. A missing `auth_time` claim falls back to now and logs a warning. |
| D5 | Settings cascade resolves most-specific first (see 3.1). Min/max added to `EnsureSettingDefinitionRequest`. Session policy read through a new `ISessionPolicyProvider`. Settings authorization is a separate story. |
| D6 | New shared `IdTokenValidator` (signature, iss, aud, exp, nonce) replaces the three private `ValidateNonce` copies. `auth_time` is read from the validated token. |
| D7 | The IdToken cookie is not extended on refresh. Documented, not changed. |

## 2. CSRF

### 2.1 Registration

`AddGroundUpAuth` calls `AddAntiforgery` with:
- `HeaderName = "X-CSRF-Token"`
- `Cookie.Name = "CsrfToken"`. The `__Host-` prefix is not used because it forbids a Domain attribute.
- `Cookie.HttpOnly = true`, `Cookie.SecurePolicy` from `AuthOptions.CookieSecure` (Always or None), `Cookie.SameSite = Strict`, `Cookie.Path = "/"`.
- `SuppressXFrameOptionsHeader = true` (the app sets its own framing policy).

`CsrfProtectionMiddleware` stays where it is in the pipeline. Change: when `AuthSource == "cookie"` and `IAntiforgery` is not registered, it fails closed (500 and an error log) and does not skip. After this change, `AddGroundUpAuth` always registers it, so this only triggers on misconfiguration.
`TRACE` is added to the safe methods (requirement 1.2).

### 2.2 Token endpoint

`GET /auth/csrf` (authenticated by cookie or bearer):
1. Calls `IAntiforgery.GetAndStoreTokens(HttpContext)`. This writes or refreshes the antiforgery cookie.
2. Returns `OperationResult<CsrfTokenDto>` with `RequestToken`. Response header `Cache-Control: no-store`.

Anonymous callers get 401. The token is bound to the antiforgery cookie, not to the user identity (ASP.NET Core default). **(confirm)** whether to also bind to the user: the default generator includes the claims-based identity when the user is authenticated, and `AuthSource=cookie` requests are authenticated by our middleware, not by an ASP.NET authentication scheme. If `HttpContext.User` is set by the middleware the binding applies, and the token from `/auth/csrf` must be issued after the user is set (true: same pipeline). A token issued before sign-in will not validate after sign-in, which is the desired behavior. The client refetches after login.

### 2.3 Client flow

1. Callback sets `AuthToken` (and `IdToken`).
2. SPA calls `GET /auth/csrf`, keeps `token` in memory.
3. SPA sends `X-CSRF-Token` on POST, PUT, PATCH, DELETE.
4. On 403 `CSRF_VALIDATION_FAILED`, refetch once and retry once.

Bearer requests (mobile, MCP, services) skip all of this (requirement 1.3). `/auth/callback` is GET and protected by the state cookie and nonce.

### 2.4 Swagger (sample)

- Add `wwwroot` or an embedded `swagger-csrf.js` and register it with `UseSwaggerUI(o => o.InjectJavascript("/swagger-csrf.js"))` **(confirm)** the sample serves static files, or embed the script via `o.HeadContent`.
- The script sets `window.ui.getConfigs().requestInterceptor` equivalent: Swagger UI exposes `requestInterceptor` in config. It fetches `/auth/csrf` with `credentials: "include"` once on load (and again after a 403), and sets `X-CSRF-Token` on requests whose method is not GET, HEAD, OPTIONS.
- The sample's own Swagger setup is the only place this lives. No framework package gets a Swagger dependency.

## 3. Session timeouts as settings

### 3.1 Settings cascade order (bug fix)

`IScopeChainProvider` returns a list and `SettingsService` takes the first match. The contract becomes explicit: **the chain is ordered most specific first**, for example `User, Group, Case, Tenant, System` for an application that has those levels. Values at a more specific level win, and each parent acts as the default for the levels below it.

- `DefaultScopeChainProvider` is changed to emit Tenant, then System (it currently emits System, then Tenant, so a System value always wins).
- The XML docs on `IScopeChainProvider` state the order.
- Consumers register their own provider for deeper hierarchies; nothing in `SettingsService` assumes two levels.
- No parent-level "lock" or "enforce" concept exists in the entities, and none is added. The "tenant can only shorten" rule is specific to the session policy (3.4), not a general Settings feature.
- **Impact check (task):** find existing settings with values at both System and Tenant level in seeders, tests and the sample. Update tests that encoded the old order.

### 3.2 Definitions

`EnsureSettingDefinitionRequest` gains optional `MinValue` and `MaxValue` (strings, as on the entity). `SettingsAdminService`-style mapping already exists for the entity. `DefaultAuthSettingsSeeder` adds:

| Key | Type | Default | Min | Max | Levels | Group |
|---|---|---|---|---|---|---|
| `auth.session.token-lifetime-minutes` | Int | 60 | 2 | 120 | system, tenant | Security > Sessions |
| `auth.session.absolute-lifetime-minutes` | Int | 480 | 10 | 1440 | system, tenant | Security > Sessions |

Seeded defaults come from `AuthOptions` values when they are within bounds. If an operator's `AuthOptions` value is outside the bounds, the seeder uses the bound-clamped value and logs a warning. `SetAsync` already enforces Min and Max.

### 3.3 `AuthOptions`

`TokenExpirationMinutes` and `AbsoluteSessionLifetimeMinutes` remain as the fallback when Settings cannot be read (database unavailable, definition missing). Their startup validation stays. They stop being the source of truth.

### 3.4 `ISessionPolicyProvider` (auth module)

```csharp
public interface ISessionPolicyProvider
{
    Task<SessionPolicy> GetSystemPolicyAsync(CancellationToken ct = default);
    Task<SessionPolicy> GetPolicyForTenantAsync(Guid tenantId, CancellationToken ct = default);
}
public sealed record SessionPolicy(TimeSpan TokenLifetime, TimeSpan AbsoluteLifetime);
```

Resolution for a tenant:
1. Build an explicit scope chain `[Tenant(tenantId), System]` from the level names (not from `ITenantContext`, because `TokenRefreshMiddleware` runs before tenant resolution).
2. Read both keys through `ISettingsService.GetAsync<int>(key, chain)`. This uses the settings cache.
3. `token = min(tenantValue, systemValue)`, `absolute = min(tenantValue, systemValue)`. (Clamp: a tenant value above System is ignored. Requirement 4.3, resolve-time check.)
4. Cross-field: if `absolute < token`, set `token = absolute` and log a warning.
5. On any failure reading settings, return the `AuthOptions` fallback and log a warning.

`GetSystemPolicyAsync` uses a chain of `[System]` only (tenantless tokens, login `max_age`, host-unknown logins).

The scope chain here is the one the policy needs (tenant, then system). Applications with deeper hierarchies are unaffected: session policy is defined at System and Tenant only.

### 3.5 Write-time validation

The Settings module validates Min and Max only. The tenant-not-above-System rule and `absolute >= token` rule need the other values. Mechanism: **an `ISettingWriteValidator` extension point in the Settings module**, called by `SetAsync` after the built-in checks and before saving. `SettingDefinition.CustomValidatorType` exists on the entity and DTO **(confirm)** whether anything invokes it; if it is unused, it is not wired up here, and the new interface is the mechanism. The auth module registers `SessionPolicyWriteValidator`, which handles only the two session keys:
- Tenant level: value must be <= the System value of the same key (read from the database, not the cache).
- Either level: after the change, `absolute >= token` for the same level, using the other key's value at that level, falling back to the next level up.
- Lowering the System value does not rewrite tenant rows. The resolve-time clamp (3.4) covers it. The admin UI shows the tenant row as "effective: lower".

Failures return `400` with specific messages. The validator interface lives in `GroundUp.Core` and is optional (no registration means no extra checks), consistent with modules registering only their own services.

### 3.6 Authorization

Out of scope here (separate story, to be discussed). Until then the session settings are as protected as any other setting. Requirement 4.7 is moved to that story. The setting key names and levels are stable, so permissions can attach later. Initial-setup (Setup wizard) behavior will be considered in that story.

### 3.7 Consumers of the policy

| Consumer | Change |
|---|---|
| `TokenService` | `exp` from `GetPolicyForTenantAsync(tenantId)`. Tenantless token uses `GetSystemPolicyAsync`. Also caps `exp` at `auth_time + AbsoluteLifetime` so a refreshed token never outlives the cap (finding 4). |
| `AuthCookieWriter` | Cookie `Expires` = the token's `exp`. The token lifetime is passed in. Removes the sync-over-async `GetAwaiter().GetResult()` by making cookie writing methods async or by passing the already-resolved domain in. **(confirm)** which touches fewer callers. |
| `TokenRefreshMiddleware` | Resolves the policy from the token's `tid`. Half-lifetime and absolute cap use it. When the cap is reached it does nothing (as today); the token then expires naturally and a request without a valid token gets 401. |
| `POST /auth/refresh` | Enforces the absolute cap. Returns `401 REAUTH_REQUIRED`. |
| `set-tenant` | Cap check uses the selected tenant's policy. Returns `401 REAUTH_REQUIRED` when the session is older than that tenant's absolute lifetime. |
| Tenantless re-issue (leave tenant) | Enforces the System cap. |
| Login (`max_age`) | `AuthUrlRequest` gains `MaxAgeSeconds?` and `ForcePrompt`. Initiate-login computes `MaxAgeSeconds` from the host-pinned tenant's policy if known, else the System policy. |

`REAUTH_REQUIRED` is a new constant in `ErrorCodes`, returned as an `OperationResult` with status 401.

## 4. Forced re-authentication and `auth_time`

### 4.1 `IdTokenValidator`

New service in `GroundUp.Auth.Services` behind `IIdTokenValidator`:

```csharp
Task<OperationResult<ValidatedIdToken>> ValidateAsync(string idToken, string expectedNonce, string realm, CancellationToken ct);
record ValidatedIdToken(DateTimeOffset? AuthTime, string Subject);
```

Checks: signature against the realm's JWKS, `iss` equals the realm issuer, `aud` contains `AppClientId`, `exp` valid (small clock skew), `nonce` equals the expected value. JWKS retrieval and caching follow the existing signing-key handling in `KeycloakIdentityProviderService` (it already builds `IssuerSigningKeys` for a `TokenValidationParameters` around line 286). **(confirm)** whether to reuse that or use `ConfigurationManager<OpenIdConnectConfiguration>` with the realm metadata address; either way the keys are cached and refreshed on unknown `kid`.

The Keycloak-specific parts (JWKS URL, issuer) belong in `GroundUp.Auth.Keycloak`. `GroundUp.Auth.Services` depends on an interface only, so the identity provider stays swappable. The existing `IIdentityProviderService` (it already validates tokens) is extended with `ValidateIdTokenAsync` if that fits better than a separate interface **(confirm at implementation)**.

A validation failure ends the login with a clear error, no cookie is written, and the state cookie is cleared (same as a nonce failure today).

### 4.2 Using `auth_time`

`LoginFlowHandler`, `SimpleSignupFlowHandler` and `NewOrganizationFlowHandler` replace `DateTimeOffset.UtcNow` with `validated.AuthTime ?? UtcNow`. If the claim is missing, log a warning (no token contents).
The three private `ValidateNonce` methods are deleted.

After issuing, each handler checks `now - authTime >= policy.AbsoluteLifetime` for the resolved tenant. If true, no cookie is written and the response is `401 REAUTH_REQUIRED` (requirement 3.5).

### 4.3 Keycloak 26.3 behavior **(confirm, task)**

Verify manually against the local Keycloak 26.3 that:
- `auth_time` is present in the ID token for a silent SSO login and holds the original authentication time.
- It changes after `prompt=login`.
- `max_age` forces a login form when the SSO session is older than the value.
Record the result in `MANUAL_TESTING.md`. If Keycloak does not include `auth_time` unless `max_age` is sent or a mapper is configured, add the mapper in `realm.json`.

### 4.4 Client contract

When a client receives `401 REAUTH_REQUIRED`, it starts login with `forceLogin=true`. `GET /auth/login?forceLogin=true` adds `prompt=login`. Normal login always carries `max_age`.

## 5. Cookies

All values assume `CookieSecure=true` unless noted. Secure is read from the one setting everywhere.

| Cookie | Written by | Purpose | HttpOnly | Secure | SameSite | Domain | Path | Expiry |
|---|---|---|---|---|---|---|---|---|
| `AuthToken` | `AuthCookieWriter` | GroundUp JWT | yes | `CookieSecure` | `CookieSameSite` (default Strict) | `.{default-domain}` | `/` | token `exp` |
| `IdToken` | `AuthCookieWriter` | `id_token_hint` for logout, only reader is `Logout` | yes | `CookieSecure` | same as AuthToken | same | `/` | token `exp` at write time |
| `AuthState` | `AuthFlowService` | login state, survives redirect from Keycloak | yes | `CookieSecure` | **Lax** (Strict would drop it on the cross-site return) | none (host-only) | `/` | `FlowStateExpirationMinutes` (10) |
| `CsrfToken` | antiforgery | antiforgery cookie token | yes | `CookieSecure` | Strict | `.{default-domain}` **(confirm)** or host-only | `/` | session |

Notes:
- The domain-wide cookie scope for `AuthToken` is needed for subdomain tenants. The `CsrfToken` cookie can be host-only if every API call goes to one host; **(confirm)** with the host-pinned flow, and use the same domain as `AuthToken` if tenants switch hosts.
- `AuthState` stays host-only and Lax. It carries no token.
- No cookie carries a token a script needs. Nothing is readable by script.
- Startup checks (`AuthStartupValidator`):
  - `CookieSecure=false` and environment is not Development: throw, refuse to start.
  - `CookieSecure=false` in Development: log a warning.
  - Default domain is a single label (no dot) and host tenant resolution is enabled: log a warning explaining browsers drop the cookie.
- Realm vs app limits: at startup, log a warning if `auth.session.absolute-lifetime-minutes` (System) exceeds the Keycloak realm's SSO Session Max, when the realm value is readable through the admin API **(confirm)**. If it's not readable without extra permissions, skip the warning and rely on documentation (requirement 6.4).

## 6. Keycloak realm

`keycloak/realm.json`: `ssoSessionIdleTimeout: 1800`, `ssoSessionMaxLifespan: 28800`. `MANUAL_TESTING.md` Part 1.2 gets steps for an existing realm. Administrator documentation states these values are realm-wide, the system administrator's responsibility, and the values a reviewer will check.

## 7. Documentation

- Admin note: tenants can only shorten session timeouts; Keycloak's two realm values are the outer limit; settings changes apply from the next token issue or refresh on the instance that handled them and after the cache TTL (15 min default) on other instances until GU-47.
- Revocation spec: entries live for the System token lifetime maximum (requirement 4.10).
- `ARCHITECTURE.md` / `CLAUDE.md`: document the cascade order contract.

## 8. Test plan

Unit:
- Antiforgery options (header name, cookie flags). `CsrfProtectionMiddleware`: cookie + no token 403, cookie + token passes, bearer exempt, GET and TRACE exempt, missing `IAntiforgery` fails closed.
- `/auth/csrf` returns a token and `Cache-Control: no-store`.
- Cookie attributes for all four cookies, including `Secure` from the one setting.
- `DefaultScopeChainProvider` order. `SettingsService` cascade with a 5-level chain: User over Group over Case over Tenant over System.
- `SessionPolicyProvider`: tenant clamp to System, cross-field rule, fallback on failure.
- `SessionPolicyWriteValidator`: tenant above System rejected, `absolute < token` rejected.
- `IdTokenValidator`: valid, bad signature, wrong iss, wrong aud, expired, wrong nonce, missing `auth_time`.
- Login handlers: fresh login, silent login (old `auth_time` preserved), silent login past cap (`REAUTH_REQUIRED`), missing-claim fallback.
- `AuthUrlBuilderService`: `max_age`, `prompt=login`.
- Startup validator cases.

Integration (cookie path, the current gap):
- POST with cookie and no token is 403; with token succeeds; with Bearer succeeds; GET never blocked.
- Change a timeout through the settings API; the next issued token and cookie use the new value.
- `POST /auth/refresh` past the cap returns 401 `REAUTH_REQUIRED`.
- Tenant stricter cap on `set-tenant`.

Baselines in the brief must not get worse.

## 9. Risks

- **Cascade fix changes behavior** for any existing System plus Tenant values. Mitigated by the impact-check task.
- **ID token validation** adds a network dependency (JWKS) at login. Keys are cached; failure to fetch fails the login closed.
- **`max_age` on every login** forces a login form when the SSO session is older than the cap. This is the intended behavior.
- **Antiforgery cookie scope** across subdomains needs checking (5).
- **Multi-instance cache** staleness until GU-47 (documented).

## 10. Out of scope

Settings authorization (separate story). Non-browser client token issuance. Pushing session values into Keycloak (GU-76). Token revocation (GU-71).

## 11. Requirements changes to apply

- 1.5: choose `GET /auth/csrf` only; remove `ProvisionAntiforgeryToken()`.
- 1.7: Swagger supplies the token via a UI script; no exemption.
- 2.4: refuse to start outside Development when `CookieSecure=false`.
- 2.5: warn on single-label domain.
- 3: add ID token validation as 3.7; add the `forceLogin` parameter.
- 4.7: moved to the settings authorization story.
- Add a requirement for the cascade order contract and the `IScopeChainProvider` documentation.
- Open question 1 and 2 resolved.
