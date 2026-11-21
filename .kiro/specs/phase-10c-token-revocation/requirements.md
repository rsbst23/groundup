# Requirements: Revoking Access When Membership Ends

## Context

When a user leaves a tenant, or an admin removes them, nothing invalidates the tenant-scoped GroundUp token they already hold. The token is a signed JWT naming the tenant (`tid`), and it stays valid until it expires (60 minutes by default, `AuthOptions.TokenExpirationMinutes`). Membership is re-checked only when a token is refreshed (`TokenRefreshMiddleware`, `POST /auth/refresh`) and, for subdomain requests, by `HostTokenReconciliationMiddleware`.

The browser cookie is replaced on a self-leave. A copy of the token is not: a script's Bearer token, a token saved by the user, or a token the admin-removed user still holds. For these, access continues after membership ends.

Permission lookups have a related gap. `PermissionService` caches a user's permissions for 15 minutes. `UserRoleChangedHandler` evicts the entry when a user's role assignment changes, but the leave and removal paths must be checked to make sure they trigger that eviction.

The target is the access-revocation expectation in NIST SP 800-53 AC-2 (and the FedRAMP baselines built on it): when a user's access is removed, their access actually ends within a defined, documented time, and that time is as short as is practical.

### Approach (agreed with the project owner)

- Record a **revocation cutoff** when access ends: "tokens for this user in this tenant, issued before time T, are no longer valid." Reject such tokens at validation time with an in-memory lookup. No database query per request.
- Entries only need to live as long as the longest-lived token they could apply to, so the list stays small.
- Shorten the default token lifetime so that, where a revocation record is not visible (see Limitations), the worst-case exposure is a short, documentable number.
- Put the cutoff list behind an abstraction. The default implementation is in-memory. It will be replaced when the caching module (BUILD_PHASES.md, Phase 15) delivers `ICache` and a Redis provider.

### Limitation until the caching module exists (must be documented, not hidden)

The in-memory store is per process. With several application instances, only the instance that handled the leave or removal knows about the cutoff. The others keep honoring the token until it expires. The bound on that exposure is the token lifetime, which is why the default is shortened below. A single-instance deployment gets immediate revocation.

## Scope

**In scope**
- A revocation cutoff store abstraction with an in-memory implementation.
- Recording cutoffs on every path that ends a membership, and when a user account is deactivated.
- Rejecting revoked tokens during JWT validation.
- Permission cache eviction on those same paths.
- A shorter default token lifetime.
- Tracking the follow-up work in Phase 15 so it is not lost.

**Out of scope**
- Distributed or persistent storage of cutoffs (Phase 15, caching module).
- Revoking Keycloak sessions or tokens (Keycloak's own session is handled by logout).
- CSRF and cookie hardening (separate spec).
- The tenantless session itself (separate spec; this spec must work with it, see Requirement 5).

## Requirements

### Requirement 1: The cutoff store

**User story:** As the framework, I need one place that answers "was this token's access revoked?" so every layer asks the same question and the storage can change later without touching callers.

#### Acceptance Criteria

1. The system SHALL define an abstraction, in a project that has no infrastructure dependencies, with operations to: record a revocation for a user and tenant, record a revocation for a user across all tenants, and ask whether a token (user id, tenant id or none, issued-at) is revoked.
2. A revocation for a user and tenant SHALL apply to tokens whose `tid` equals that tenant and whose issued-at is earlier than the cutoff time.
3. A revocation for a user across all tenants SHALL apply to every token for that user, including tenantless tokens, whose issued-at is earlier than the cutoff time.
4. A token issued after the cutoff time SHALL NOT be treated as revoked. This is what lets a user leave, rejoin, and sign in again.
5. The default implementation SHALL be in-memory and thread-safe, and SHALL drop entries older than the maximum possible token lifetime, plus any clock allowance, so memory use is bounded by the number of revocations in that window. Since GU-72 the token lifetime is the runtime setting `auth.session.token-lifetime-minutes`, and a tenant value can never exceed the System value. The System value can change while older tokens are still alive, so the safe bound is the setting's maximum (120 minutes, `SessionSettingKeys.TokenLifetimeMax`), not the current System value.
6. The abstraction and the default implementation SHALL carry a code comment and a reference in the Phase 15 entry of BUILD_PHASES.md stating that the in-memory store must be replaced by an `ICache`-backed implementation when the caching module is built.
7. The cutoff time SHALL come from `TimeProvider` (or an equivalent injectable clock) so tests control it.

### Requirement 2: Access-ending events record a cutoff

**User story:** As a tenant admin or user, when membership ends, access ends.

#### Acceptance Criteria

1. WHEN a user leaves a tenant THEN a (user, tenant) cutoff SHALL be recorded.
2. WHEN an admin removes or deactivates a member of a tenant THEN a (user, tenant) cutoff SHALL be recorded. All code paths that deactivate a membership SHALL do so, not only the controller's leave endpoint. The design SHALL list every such path.
3. WHEN a user account is deactivated THEN a user-wide cutoff SHALL be recorded.
4. A cutoff SHALL be recorded only after the change has been saved successfully. A failed leave or removal SHALL NOT record one.
5. The cutoff time SHALL be at or after the moment the membership change was committed, so a token issued before the change is always older than the cutoff.

### Requirement 3: Revoked tokens are rejected

**User story:** As the system, I must not accept a token whose access was revoked.

#### Acceptance Criteria

1. JWT validation SHALL reject a token that the store reports as revoked, for both cookie and Bearer authentication, with 401.
2. The check SHALL run before any tenant-scoped work and SHALL NOT query the database.
3. The check SHALL fail closed if the store throws: the request is rejected and the failure is logged without the token or other sensitive data.
4. A revoked token SHALL NOT be refreshed. `TokenRefreshMiddleware` and `POST /auth/refresh` SHALL treat it as unauthenticated.
5. The rejection response SHALL carry the correlation ID like other auth errors.
6. The comparison SHALL use the `iat` claim. The JWT library emits `iat` by default (verified); a test SHALL assert that generated tokens carry it so a library change cannot silently disable revocation. `auth_time` keeps its current meaning (original sign-in) and is not used for revocation.

### Requirement 4: Permission cache eviction on the same events

**User story:** As a tenant admin, when I remove someone, their cached permissions do not linger.

#### Acceptance Criteria

1. WHEN a membership ends (Requirement 2.1 and 2.2) THEN the user's cached permissions SHALL be evicted in the same operation, using the existing eviction mechanism.
2. The design SHALL state whether eviction is per user or per user and tenant, based on how `PermissionService` keys its cache.
3. A test SHALL show that after a leave, `HasPermission` does not return a stale grant.

### Requirement 5: Shorter token lifetime and compatibility with other work

**User story:** As a compliance reviewer, the worst-case exposure where revocation is not visible is short and documented.

#### Acceptance Criteria

1. The default token lifetime SHALL change from 60 to 15 minutes. Since GU-72 that default is the seeded value of `auth.session.token-lifetime-minutes` (`SessionSettingKeys.TokenLifetimeDefault`); `AuthOptions.TokenExpirationMinutes` is only the fallback when settings cannot be read, and SHALL change with it. The rule absolute lifetime >= token lifetime SHALL still hold with the defaults.
2. Sliding refresh SHALL keep working with the shorter lifetime (refresh at the halfway point). Existing tests that assume 60 minutes SHALL be updated, not skipped.
3. Documentation SHALL state the revocation guarantee in one place: immediate on a single instance; at most `TokenExpirationMinutes` across instances until the caching module replaces the store.
4. The revocation check SHALL work for tenantless tokens (no `tid`), using the user-wide cutoff. The tenantless-sessions spec SHALL reference this spec for its token validation.
5. Because a rejoin issues a token with a later `iat`, leaving and rejoining SHALL work with no manual cleanup (Requirement 1.4).

### Requirement 6: Tests and manual verification

#### Acceptance Criteria

1. Unit tests SHALL cover the store: tenant cutoff, user-wide cutoff, token issued after the cutoff, token for a different tenant, expiry of old entries, concurrent writes.
2. Integration tests SHALL cover, over HTTP: a token held before a leave is rejected with 401 afterwards; the same for admin removal; leave, rejoin and new sign-in succeeds; a token for another tenant of the same user keeps working after a leave from the first; a failed leave (last admin) leaves the token valid; a revoked token cannot be refreshed.
3. MANUAL_TESTING.md SHALL gain a step that saves a Bearer token, leaves the tenant, and confirms the saved token is rejected.

## Decisions Made for the Design

1. **Clock precision.** `iat` has whole-second precision. The cutoff time is rounded up to the next whole second and a token is revoked when `iat < cutoff`. A token issued in the same second as the leave is revoked, which is the safe direction. A rejoin followed by a new sign-in goes through Keycloak and takes longer than a second, so legitimate tokens are not affected.
2. **Multi-instance before Phase 15.** No database-backed store. The documented limitation stands. The feature is not enabled in production until the caching module is done (project owner).
3. **Where the check runs.** In `JwtAuthenticationMiddleware`, directly after `TokenService.ValidateTokenAsync` succeeds and before the identity-provider fallback. This project authenticates with custom middleware and has no ASP.NET `JwtBearerEvents`, and the middleware already handles cookie and header tokens in one place.

## Manual Re-Testing Required After Implementation

- Phase 7 (leave): save a Bearer token before leaving, confirm it is rejected afterwards, and confirm rejoin works.
- Admin removal of a member (when an endpoint exists), with the same check.
