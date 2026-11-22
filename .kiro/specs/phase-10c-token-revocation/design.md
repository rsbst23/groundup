# Design: Revoking Access When Membership Ends (GU-71)

Implements `requirements.md` in this folder (Requirements 1 to 8 and the decisions at its end).

## 1. Components

| Component | Project | Role |
|---|---|---|
| `ITokenRevocationStore` | `GroundUp.Auth.Core/Abstractions` | Record and check revocation cutoffs. |
| `InMemoryTokenRevocationStore` | `GroundUp.Auth.Services/Revocation` | Default, singleton, per process. Replaced in Phase 15 (GU-47). |
| `TokenRevocationStartupValidator` | `GroundUp.Auth.Services/Revocation` | Refuses to start in Production with the in-memory store unless opted in (Requirement 8). |
| `IMembershipService` / `MembershipService` | `GroundUp.Auth.Services/Membership` | Leave a tenant: guard, deactivate, cutoff, cache eviction, tenantless session (Requirement 7). |
| `LeaveTenantResult` | `GroundUp.Auth.Core/Dtos` | `CurrentTenantId` (null when the session became tenantless), `SessionToken` (the new tenantless token or null), `SessionEnded` (true when the old token must leave the cookie). |
| `JwtAuthenticationMiddleware` (changed) | `GroundUp.Auth.Api/Middleware` | Rejects revoked tokens after validation (Requirement 3). |
| `AuthController.LeaveTenant` (changed) | `GroundUp.Auth.Api/Controllers` | Thin adapter over `IMembershipService`. |

## 2. Store

```csharp
public interface ITokenRevocationStore
{
    Task RevokeTenantAccessAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task RevokeAllAccessAsync(Guid userId, CancellationToken ct = default);
    Task<bool> IsRevokedAsync(Guid userId, Guid? tenantId, DateTimeOffset issuedAt, CancellationToken ct = default);
}
```

- **Cutoff:** `now` rounded up to the next whole second (a time already on a whole second stays). A token is revoked when `iat < cutoff`.
- **Tenant entry** `(userId, tenantId)` applies to tokens with that `tid`. **User-wide entry** `(userId)` applies to every token of the user, with or without `tid`. `IsRevokedAsync` checks both.
- A second revocation for the same key keeps the later cutoff.
- **Retention:** 121 minutes (the token lifetime setting's maximum, `SessionSettingKeys.TokenLifetimeMax` = 120, plus 1 minute; `TokenService` validates with zero clock skew). Expired entries are ignored on read and removed on each write. Memory is bounded by the number of revocations in that window.
- **Storage:** `ConcurrentDictionary<(Guid User, Guid? Tenant), DateTimeOffset>`; updates through `AddOrUpdate` keeping the maximum.
- **Clock.** The store takes a `TimeProvider` so unit tests control it. Tokens get `iat` from the JWT handler, which uses the system clock. The cutoff must use the same clock, so the DI registration passes `TimeProvider.System` explicitly instead of resolving `TimeProvider` from the container (the integration fixture registers an unused `FakeTimeProvider`, and an application could register another one; either would put the cutoff on a different clock than `iat`).
- Both files carry a comment that the store must be replaced by an `ICache`-backed implementation in Phase 15 (GU-47), and BUILD_PHASES.md Phase 15 already says so (checked; the wording is updated to name the interface).
- Registration: `services.TryAddSingleton<ITokenRevocationStore>(_ => new InMemoryTokenRevocationStore(TimeProvider.System))` in `AddGroundUpAuth`, so an application can register its own store first.

## 3. Production guard (Requirement 8)

`TokenRevocationStartupValidator : IHostedService`, registered by `AddGroundUpAuth`.

- If `IHostEnvironment.IsProduction()` and the registered store is `InMemoryTokenRevocationStore` and `AuthOptions.AllowInMemoryTokenRevocationStore` is false: throw `InvalidOperationException`. The message says that revocation reaches only the instance that handled the leave, that other instances honor the token until it expires, and names `GroundUp:Auth:AllowInMemoryTokenRevocationStore`.
- Production with the option set: start and log one warning with the same explanation.
- Any other environment, or any other store: nothing.
- New option `AuthOptions.AllowInMemoryTokenRevocationStore` (default false).

## 4. Leaving a tenant (Requirement 7)

```csharp
public interface IMembershipService
{
    Task<OperationResult<LeaveTenantResult>> LeaveTenantAsync(
        Guid tenantId, DateTimeOffset? originalAuthTime, CancellationToken ct = default);
}
```

Flow, using `ICurrentUser.UserId` and `ITenantContext.TenantId` (the session's current tenant, `Guid.Empty` when tenantless):

1. No user → `Unauthorized`.
2. No active membership in `tenantId` → `NotFound("User is not a member of this tenant.")`.
3. `LastAdminGuard.CanLeaveTenantAsync` fails → `Forbidden` with the guard's message and error code.
4. `IUserTenantRepository.DeactivateMembershipAsync` fails → that result (409 on conflict).
5. After a successful save: `RevokeTenantAccessAsync(userId, tenantId)`, then evict `permissions:{userId}:{tenantId}` from `IMemoryCache`. A store failure here is logged and does not undo the leave (the membership is already gone and the token expires within the token lifetime); the response is still success. This is the one place where revocation is best effort, and it is documented.
6. If `tenantId` was the current tenant: issue a tenantless session (`IAuthSessionService.IssueTenantlessSessionAsync(userId, originalAuthTime)`). Return `CurrentTenantId = null`, `SessionEnded = true`, `SessionToken` = the new token, or null when issuing failed.
7. Otherwise: `CurrentTenantId` = the current tenant (null if the session was already tenantless), `SessionEnded = false`.

The cache key format (`permissions:{userId}:{tenantId}`) is owned by `PermissionService`. It is moved into one internal static helper used by `PermissionService`, `UserRoleChangedHandler` and `MembershipService`, so the three cannot drift.

**Controller** keeps only HTTP work: the existing 401 for an unauthenticated request and for an identity-provider (Keycloak) principal, reading `auth_time` from the principal, calling the service, writing the cookie when `SessionToken` is set or clearing it when `SessionEnded` and no token, and mapping the result to the existing responses (200 `{ success, currentTenantId }`, 403 `{ error, errorCode }`, 404 `{ error }`, 409). It drops its `IUserTenantRepository` and `LastAdminGuard` dependencies if no other action uses them.

## 5. Rejecting revoked tokens (Requirement 3)

In `JwtAuthenticationMiddleware`, after `ValidateTokenAsync` returns a principal:

1. Read `sub` (user id), `tid` (optional) and `iat`. A GroundUp token without a parsable `sub` or `iat` is treated as revoked (fail closed; `TokenService` always writes both).
2. `IsRevokedAsync`. If revoked, or the store throws: leave `context.User` anonymous, **do not** try the identity-provider fallback, log `"Rejected a revoked token for user {UserId}"` (Information) or the store error (Error, without the token), and continue the pipeline.
3. Otherwise set `context.User` as today.

Protected endpoints then return their normal 401, with the correlation ID the error middleware already adds. `TokenRefreshMiddleware` and `POST /auth/refresh` read `context.User`, so a revoked token is never refreshed. `HostTokenReconciliationMiddleware` runs after authentication and sees an anonymous request.

A test asserts `TokenService` tokens carry `iat` (Requirement 3.6).

## 6. Shorter token lifetime (Requirement 5)

- `SessionSettingKeys.TokenLifetimeDefault` 60 → 15. `AuthOptions.TokenExpirationMinutes` default 60 → 15. `samples/GroundUp.Sample/appsettings.Development.json` `TokenExpirationMinutes` 60 → 15.
- Absolute lifetime default stays 480 (rule absolute >= token holds). Refresh happens after 7.5 minutes.
- Tests that assert the default are updated. Tests that set their own values are left alone.
- No data migration: GroundUp has never been deployed.

## 7. Revocation guarantee (documented once, in ARCHITECTURE.md)

> When a membership ends, tokens for that tenant issued before the change are rejected immediately on the instance that handled it. Until the caching module replaces the in-memory store, other instances accept such a token until it expires: at most the token lifetime (default 15 minutes, maximum 120). Production refuses to start with the in-memory store unless `AllowInMemoryTokenRevocationStore` is set.

## 8. Edge cases

| Case | Behavior |
|---|---|
| Leave a tenant that is not the current one | Cutoff for that tenant only; the current token (other `tid`) keeps working. |
| Leave the current tenant | Old token revoked; the new tenantless token has no `tid`, so the tenant cutoff does not apply to it. |
| Leave, rejoin, sign in again | The new token's `iat` is after the cutoff (the sign-in goes through Keycloak, longer than a second). |
| Token issued in the same second as the leave | Revoked (cutoff rounds up). Safe direction. |
| Last-admin guard blocks the leave | No cutoff, token keeps working. |
| `set-tenant` to the tenant just left | Already rejected by the membership check. |
| Several instances | See section 7. |
| Store throws during the check | Request is anonymous (fail closed). |
| Store throws while recording | Leave succeeds, error logged, exposure bounded by token lifetime. |

## 9. Tests

- **Unit:** store (tenant cutoff, user-wide cutoff incl. tenantless token, token after cutoff, other tenant, same-second rounding, retention expiry, concurrent writes, later cutoff wins); production guard (Production blocks, opt-in warns, Development silent, custom store ignored); `MembershipService` (each result, cutoff and eviction only after a successful save, no cutoff on guard failure, tenantless session only for the current tenant); middleware (revoked → anonymous and no IdP call; store throws → anonymous; missing `iat` → anonymous; valid → authenticated); controller maps results; `iat` present.
- **Integration (HTTP):** Bearer token held before leave → 401 after; cookie token → 401 after; another tenant's token of the same user keeps working; last-admin blocked leave keeps the token valid; revoked token cannot be refreshed; leave, rejoin, new token works; `HasPermission` not stale after leave. Admin removal is not tested because the path does not exist yet (GU-68).
- **Manual:** MANUAL_TESTING.md Part 7 gains a step: save a Bearer token before leaving, confirm 401 afterwards.

## 10. Task groups

1. Store, production guard, option, registration, unit tests.
2. `IMembershipService`, cache key helper, thin controller, unit tests.
3. Middleware check, 15-minute default, updated tests, `iat` test.
4. Integration tests; ARCHITECTURE.md guarantee, BUILD_PHASES.md Phase 15 wording, MANUAL_TESTING.md step; Jira.
