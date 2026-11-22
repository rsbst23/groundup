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

- [ ] 3. Reject revoked tokens and shorten the lifetime
  - [ ] 3.1 Revocation check in `JwtAuthenticationMiddleware` (no IdP fallback, fail closed)
  - [ ] 3.2 Defaults 60 → 15 (`SessionSettingKeys`, `AuthOptions`, sample appsettings)
  - [ ] 3.3 Unit tests, `iat` assertion, update tests that assume 60

- [ ] 4. Integration tests and documentation
  - [ ] 4.1 HTTP integration tests (design section 9)
  - [ ] 4.2 ARCHITECTURE.md revocation guarantee; BUILD_PHASES.md Phase 15 wording; MANUAL_TESTING.md Part 7 step
  - [ ] 4.3 Jira
