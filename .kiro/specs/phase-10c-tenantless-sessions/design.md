# Design: Sessions Without a Current Tenant

Implements `requirements.md` in this folder. Read it first. This document records how the current code behaves, what changes, and which decisions need review.

## What the code does today (verified)

| Area | Today |
|---|---|
| Token issuance | `TokenService.GenerateTokenAsync(userId, tenantId, extraClaims)` always writes `tid` and loads that tenant's roles plus system roles. `iat`, `nbf` and `exp` are emitted by the JWT library by default (checked against System.IdentityModel.Tokens.Jwt 8.x). `auth_time` is added by `AuthSessionService`. |
| Signing key | `ISigningKeyProvider.GetSigningKeyAsync(tenantId)`. The only implementation ignores `tenantId` and returns one key. |
| Authentication | Custom `JwtAuthenticationMiddleware` (no ASP.NET authentication scheme). It reads the cookie, then the `Authorization` header. It validates a GroundUp token first, then falls back to the identity provider. The fallback principal has authentication type `"ExternalIdP"`, the external user id as `sub`, and no `tid` or roles. |
| Refresh | `TokenRefreshMiddleware` skips any principal without `tid`, so a tenantless token is never refreshed. This is what Requirement 2.5 wants. |
| Picker | `LoginFlowHandler.HandleMultipleMembershipsAsync` writes the **Keycloak access token** to the auth cookie. It does not write the `IdToken` cookie, so logout from the picker has no `id_token_hint`. |
| Zero memberships | With a default tenant: join it (reactivating an old row). Without one: `FlowResult.Error("ACCESS_DENIED", ..., 403)`. |
| `set-tenant` | A principal without `tid` is treated as a Keycloak principal and resolved by `GetByExternalUserIdAsync(sub)`. It does **not** check the absolute session lifetime. |
| `me/tenants`, `leave` | Use `ResolveGroundUpUserId()`, which parses `sub` as a GUID. A Keycloak `sub` is also a GUID, so for a picker principal it silently yields a user id that matches nobody. |
| `POST /auth/organizations` | Rejects a principal without `tid` with 401. |
| Tenant-scoped data | `BaseTenantRepository` has no empty-tenant check. With `TenantContext.TenantId == Guid.Empty`, reads return empty pages or 404, and `AddAsync` writes `TenantId = Guid.Empty`. **Nothing returns 403.** |
| Leave | `AuthController.LeaveTenant` picks `remainingMemberships.First()` (unordered), or clears the cookie and returns `{ noTenants: true }`. |

## Design decisions

### D1. The tenantless token

A new method `ITokenService.GenerateTenantlessTokenAsync(Guid userId, IEnumerable<Claim>? additionalClaims = null)`.

- Claims: `sub`, `email`, `name`, plus `auth_time` from the caller. No `tid`, no `role` (Requirement 2.1). System roles are not included either: a tenantless session grants nothing.
- Signed with `GetSigningKeyAsync(Guid.Empty)`. Today every tenant gets the same key. When per-tenant keys arrive, `Guid.Empty` means the system key, and the provider documentation must say so.
- Lifetime: `TokenExpirationMinutes`, same as any token. It is never refreshed (existing middleware behavior). When it expires, the user signs in again, and Keycloak's SSO session makes that silent.
- Inactive user returns null, as the tenant-scoped method does.
- No new marker claim. A tenantless token is recognized by the absence of `tid`.

`IAuthSessionService` gains `IssueTenantlessSessionAsync(Guid userId, DateTimeOffset? originalAuthTime)` returning `OperationResult<string>`. It applies `CreateAuthTimeClaims` the same way the other methods do.

### D2. Telling a GroundUp principal from an identity-provider principal

The controller currently guesses from `tid`, which breaks once tenantless GroundUp tokens exist. Instead:

- `JwtAuthenticationMiddleware` already creates identity-provider principals with authentication type `"ExternalIdP"`. Expose that string as a constant (`AuthConstants.ExternalIdpAuthenticationType`) and add a small helper used by the controller: `IsExternalIdp(principal)`.
- A GroundUp principal (with or without `tid`): `sub` is the GroundUp user id, `auth_time` is read from the claim.
- An identity-provider principal: resolve the user by external id, as today. This only remains for clients that send a Keycloak Bearer token to `set-tenant`. The login flow no longer writes a Keycloak token to the cookie (D3).
- `ResolveGroundUpUserId()` returns null for an identity-provider principal instead of guessing. This fixes the silent mismatch for `me/tenants` and `leave`.

### D3. Picker and zero-membership login use the tenantless token

`LoginFlowHandler`:

- 2+ standard tenants: issue a tenantless token (`auth_time = now`), write it to the auth cookie, write the `IdToken` cookie (new, fixes logout from the picker), and return `FlowResult.TenantSelectionRequired(list)` as before. The Keycloak access token is no longer stored.
- 0 active memberships and **no default tenant configured**: issue a tenantless token the same way and return a new `FlowResult.NoCurrentTenant()` with an empty tenant list. A default tenant that is configured but does not resolve to an active tenant stays `ACCESS_DENIED` 403: that is a configuration fault and should stay visible.
- Memberships only in enterprise tenants: unchanged, 403 (Requirement 3.4).

`FlowResult` gets `IsTenantless` (true for both cases above). The callback response:

| Case | Response |
|---|---|
| Token issued | `{ token, redirectUrl }` (unchanged) |
| Picker | `{ requiresTenantSelection: true, tenants: [...] }` (unchanged) |
| Zero memberships, no default | `{ requiresTenantSelection: false, tenants: [] }`. The client sees there is no `token` and no tenant, and offers "create an organization". |

### D4. `set-tenant`, `me`, `me/tenants`, `organizations`

- `set-tenant`: a GroundUp principal (tenantless or not) uses its own user id and `auth_time`. **New:** reject with 401 when `now - auth_time >= AbsoluteSessionLifetimeMinutes`. Today `set-tenant` and the leave re-issue can restart the clock indefinitely, so the absolute cap is only enforced on automatic refresh. Tenantless sessions make this path more visible, so it is fixed here (see Risks).
- `me`: already returns `tenantId: null` without `tid`. No change.
- `me/tenants`: no `current` flag is true for a tenantless session (the code already compares with a null current tenant). No change beyond D2.
- `organizations`: drop the `tid` requirement. Keep the checks for a GroundUp user id and an active user. A tenantless caller is accepted; the session stays tenantless (Requirement 4.3).

### D5. Leave

`LeaveTenant` after a successful deactivation:

- If the session's `tid` equals the tenant left: issue a tenantless token with the same `auth_time`, write the cookie, respond `{ success: true, currentTenantId: null }`. If issuing fails, clear the cookie and still report success: the membership is already gone and the old token must not be written back (Requirement 1.3).
- Otherwise (left a different tenant, or the session is tenantless): no cookie change, respond `{ success: true, currentTenantId: <tid or null> }`.
- `noTenants` is removed. `remainingMemberships.First()` is removed.

API-only clients that authenticate with a Bearer header do not receive the new token (cookies are the only delivery channel today, for `set-tenant` as well). That limitation already exists and is out of scope. Closing the old token's access is the job of the token-revocation spec.

### D6. Rejecting tenantless sessions on tenant-scoped data

The rule belongs where tenant isolation already lives, so SDK callers get it too and controllers stay free of security logic.

- `BaseTenantRepository`: every public and protected entry that filters or stamps by tenant (`GetAllAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, and export) returns `OperationResult.Forbidden` with `ErrorCodes.TenantRequired` when `TenantContext.TenantId == Guid.Empty`. It fails closed before touching the database. The HTTP result is 403 through the existing `OperationResult` to status mapping.
- New constant `ErrorCodes.TenantRequired` (message resource string, per the localization rule).
- Code that legitimately works with no ambient tenant must not go through these entry points. The known cases are the membership and role lookups that already take an explicit tenant id or bypass the filter. Any caller that breaks is found by running the full unit and integration suites, and is fixed by using the existing explicit-tenant or bypass methods, not by weakening the guard.

Rejected alternatives:

- **A middleware that rejects tenantless sessions outside `/auth/*`.** It puts the rule in the HTTP layer only (SDK callers would not get it) and it would block consuming-app endpoints that need no tenant.
- **A `[RequiresTenant]` attribute on services.** It works only where developers remember to add it. The repository guard is automatic.

Not covered: a custom service that never touches a tenant-aware repository (for example, one that calls an external API) is not protected by this guard. That service should check `ITenantContext.TenantId` itself. The framework documentation will say so.

### D7. Interplay with the token-revocation spec

- A tenantless token has no `tid`, so a per-(user, tenant) cutoff from leaving a tenant does not touch it. A user-wide cutoff (user deactivated) does.
- The same-second rule for the revocation spec is decided here: a cutoff time is **rounded up to the next whole second** and a token is revoked when `iat < cutoff`. A token issued in the same second as the leave is revoked (the safe direction). A rejoin followed by a new sign-in always takes longer than a second because it goes through Keycloak, so legitimate tokens are not affected.
- The check lives in `JwtAuthenticationMiddleware`, directly after `ValidateTokenAsync` and before the identity-provider fallback. That covers cookie and header tokens in one place, because this project has no ASP.NET `JwtBearerEvents`. The revocation requirements are updated to say so.

## Files affected

| Area | Files |
|---|---|
| Token | `ITokenService`, `TokenService` (tenantless method) |
| Session service | `IAuthSessionService`, `AuthSessionService` (tenantless issuance, absolute-lifetime check in `SetTenantAsync`) |
| Flows | `FlowResult`, `LoginFlowHandler` |
| API | `AuthController` (callback response, `set-tenant`, `organizations`, `leave`, `ResolveGroundUpUserId`), `JwtAuthenticationMiddleware` (constant only), new `AuthConstants` |
| Repositories and Core | `BaseTenantRepository`, `ErrorCodes`, resource strings |
| Tests (unit) | `TokenServiceTests`, `AuthSessionServiceTests`, `LoginFlowHandlerTests`, `AuthControllerTests`, a `BaseTenantRepository` empty-tenant suite |
| Tests (integration) | `PendingSelectionTests` (now tenantless token), `LeaveTenantTests`, `LoginFlowTests`, new `TenantlessSessionTests`, a tenant-scoped endpoint rejecting a tenantless session over HTTP |
| Docs | `MANUAL_TESTING.md` (Phases 3, 7 and a tenantless section), `auth-flows-design.md` |

## Risks and how they are checked

1. **The repository guard breaks a code path that legitimately has no tenant.** Mitigation: full unit and integration runs before the guard is committed, and the failing callers are fixed by explicit-tenant or bypass methods. The 7 known integration failures and 1 known unit failure are the baseline.
2. **The absolute-lifetime check on `set-tenant` rejects sessions that work today.** It only rejects sessions older than `AbsoluteSessionLifetimeMinutes` (480 by default), which the refresh middleware already treats as expired. Tests cover the boundary.
3. **Existing clients read `noTenants`.** There are none in this repository. The response change is called out in the commit message and `MANUAL_TESTING.md`.
4. **Keycloak tokens in the cookie stop working for `set-tenant`.** `PendingSelectionTests` currently depends on that behavior and is rewritten for the tenantless token. A Keycloak Bearer token sent directly to `set-tenant` still works (D2).

## Proposed task groups

1. Tenantless token and session-service issuance, with unit tests.
2. `ErrorCodes.TenantRequired` and the `BaseTenantRepository` guard, with unit tests and a full-suite run to find broken callers.
3. Principal classification (D2), `FlowResult` and `LoginFlowHandler` (D3), with unit tests.
4. `AuthController`: callback, `set-tenant`, `organizations`, `leave` (D4, D5), with unit tests.
5. Integration tests: leave with 0, 1 and 3 other memberships, non-current leave, `set-tenant` from tenantless, create organization from tenantless, tenant-scoped endpoint returns 403, zero-membership login, picker.
6. Documentation and manual testing steps, final checkpoint.

## Decisions for review

1. The `set-tenant` absolute-lifetime fix (D4) goes in this spec because the tenantless flow depends on `set-tenant`. Confirm.
2. The empty-tenant guard lives in `BaseTenantRepository` (D6), not in HTTP middleware. Confirm.
3. A configured-but-unresolvable default tenant stays a 403 (D3). Confirm.
4. Callback response for zero memberships is `{ requiresTenantSelection: false, tenants: [] }` and leave returns `{ success, currentTenantId }` (D3, D5). Confirm the shapes.
