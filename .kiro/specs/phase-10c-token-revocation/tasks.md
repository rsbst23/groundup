# Tasks: Token Revocation (GU-71)

Execute one top-level task at a time. After each: build, run tests against the baselines, commit, stop for review.

Baselines: unit 1623 pass / 12 known failures (+1 flaky property test, GU-73); integration 357 pass / 48 known failures; sample 22/22.

- [x] 1. Store and production guard
  - [x] 1.1 `ITokenRevocationStore` (Auth.Core) and `InMemoryTokenRevocationStore` (Auth.Services), Phase 15 comment
  - [x] 1.2 `AuthOptions.AllowInMemoryTokenRevocationStore`, `TokenRevocationStartupValidator`, registration in `AddGroundUpAuth`
  - [x] 1.3 Unit tests (21 new; unit 1644 pass / 12 known + flaky; integration 357 / 48; sample 22/22)

- [x] 2. Membership service and thin controller
  - [x] 2.1 `LeaveTenantResult`, `IMembershipService`, `MembershipService`
  - [x] 2.2 Shared permission cache key helper (`PermissionCacheKeys`) used by `PermissionService`, `UserRoleChangedHandler`, `MembershipService`
  - [x] 2.3 `AuthController.LeaveTenant` calls the service only; existing responses unchanged
  - [x] 2.4 Unit tests: leave rules moved from `AuthControllerTests` to `MembershipServiceTests` (16) plus revocation and eviction; controller tests now cover only the HTTP mapping (10). Existing `LeaveTenantTests` (integration) pass unchanged. Unit 1657 / 12 known; integration 357 / 48; sample 22/22

- [x] 3. Reject revoked tokens and shorten the lifetime
  - [x] 3.1 Revocation check in `JwtAuthenticationMiddleware` (no IdP fallback, fail closed)
  - [x] 3.2 Defaults 60 → 15 (`SessionSettingKeys`, `AuthOptions`, sample appsettings)
  - [x] 3.3 Unit tests (6 middleware, 1 TokenService principal exposes `sub`/`iat`; issued-token `iat` was already asserted), updated tests that assumed 60 (session policy, seeder, GU-77 integration tests now write tenant value 10). Unit 1663 / 12 known + flaky; integration 357 / 48; sample 22/22

- [x] 4. Integration tests and documentation
  - [x] 4.1 `TokenRevocationIntegrationTests` (7): Bearer and cookie tokens held before a leave get 401; the token for the user's other tenant keeps working; a last-admin blocked leave keeps the token valid; a revoked token cannot be refreshed; leave, rejoin and a new token works (the old one stays revoked); leave evicts the app's cached permissions. Admin removal is not tested: the path does not exist yet (GU-68)
  - [x] 4.2 ARCHITECTURE.md revocation guarantee and default 15; BUILD_PHASES.md Phase 15 wording; MANUAL_TESTING.md Part 7 (save a token in 7.1, check 401 in 7.4), Part 11 default, "not yet built" list
  - [x] 4.3 Jira
  - Results: unit 1664 / 12 known; integration 364 / 48 known; sample 22/22
