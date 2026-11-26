# Tasks: Test Debt (GU-73)

Execute one top-level task at a time. After each one: build, run the tests, commit, and stop for review.

Starting point: unit 1745 pass / 12 known failures (plus the flaky correlation ID test); integration 369 / 48 known; sample 22/22.

## PR 1: No known failures

- [x] 1. Setup tests
  - [x] 1.1 Remove the duplicate `BootstrapAdminToken` scheme registration from `SetupWizardApiFactory`, `SetupRateLimitApiFactory` and `FirstAdminIdempotencyApiFactory`
  - [x] 1.2 Use `DefaultSettingLevelSeeder.SystemLevelName` for every level seed and lookup in those factories and their tests. Make `_systemLevelSeeded` an instance field
  - [x] 1.3 Run the 41 setup integration tests. Fix any further test bugs here. Report a production bug before changing anything
  - [x] 1.4 Unit: `SetupWizardServiceTests` and `WizardIdempotencyAndRotationPropertyTests` seed `SystemLevelName`
  - [x] 1.5 Keycloak startup validator: correct the doc comment; partial settings during setup don't throw; new property that partial settings after setup throw naming each missing key
  - [x] 1.6 Build, run all suites, commit
  - Results: unit 1758 pass / 0 failed; integration 413 pass / 7 failed (the group 2 tests) / 0 skipped; sample 22/22. Also found in 1.3: `SetupBodySizeTests` registered the scheme twice too, and 3 of its tests were skipped with an outdated reason (`AddGroundUpSetup` is wired in the sample now); unskipped, all pass

- [x] 2. Auth tests and the MarkFailed bug
  - [x] 2.1 Unique permission keys in `AuthorizationProxyIntegrationTests` (2 sites) and `PermissionResolutionTests`
  - [x] 2.2 Clear `AuthFlowStates` before each test (`IAsyncLifetime`) in the sweeper, bulk-operations and mark-failed test classes. Check the other auth-flow-state test classes for count assertions
  - [x] 2.3 `MarkFailedAsync` as one conditional update (design). Regression tests: consumed then failed, and expired then failed, each returning 409 with the row unchanged, using one context
  - [x] 2.4 Cookie-domain test expects 200 JSON with `redirectUrl`, and the cookie domain check is case-insensitive
  - [x] 2.5 Build, run all suites (expected: no failures except the flaky correlation ID test, fixed in 3), commit. **PR 1 boundary**
  - Results: unit 1757 pass / 1 failed (the flaky correlation ID property, replaced in 3); integration 422 / 0; sample 22/22. Permission keys: the proxy tests need the literal `orders.read` (the attribute checks it), so `SeedPermissionAsync` reuses an existing key instead of inserting a duplicate; only the sweeper and bulk-operation classes needed clearing

## PR 2: Hardening

- [x] 3. Correlation ID validation
  - [x] 3.1 `CorrelationIdMiddleware.IsValid` and `MaxLength = 128`; an invalid incoming id is replaced with a new GUID, and nothing is logged
  - [x] 3.2 Replace the flaky property with two properties (valid ids propagate unchanged, invalid ids are replaced) using explicit generators. Facts for 128 and 129 characters, tab, and CR/LF
  - [x] 3.3 Build, run all suites, commit
  - Results: unit 1769 / 0; integration 422 / 0; sample 22/22 (first fully green run). The correlation ID tests passed 5 consecutive runs at 200 cases per property

- [x] 4. Host/token exemptions and Keycloak version
  - [x] 4.1 `AuthOptions.HostTokenCheckExemptPathPrefixes`, added to the two built-in prefixes, with segment-boundary matching
  - [x] 4.2 Middleware unit tests (configured prefix, segment boundary, built-ins with an empty list, non-exempt still checked)
  - [x] 4.3 Sample `appsettings.Development.json` adds `/swagger`
  - [x] 4.4 `KeycloakFixture` on `keycloak:26.3`, with a comment pointing at the compose file. Keycloak integration tests pass
  - [x] 4.5 Build, run all suites, commit. **PR 2 boundary**
  - Results: unit 1783 / 0; integration 422 / 0 (13 Keycloak tests on 26.3); sample 22/22. Prefixes are normalized (leading slash added, trailing slash removed); a root or blank prefix is ignored, because it would match every path and switch the check off

## PR 3: Keycloak test gaps

- [x] 5. Keycloak unit tests
  - [x] 5.1 Extract `KeycloakRetryPolicy.Create(logger, sleepDurationProvider)`; registration unchanged
  - [x] 5.2 `PollyRetryPolicyPropertyTests` (5xx retried `RetryCount` times then returned, 4xx not retried, 408 retried, exception rethrown after the last attempt, success after failures), with zero delay
  - [x] 5.3 `KeycloakOptionsSetupTests` (mapping property, change token fires only for Keycloak keys, `CurrentValue` refreshes)
  - [x] 5.4 Build, run all suites, commit
  - Results: unit 1793 / 0; integration 422 / 0; sample 22/22. The 10 new tests run in under a second (no real retry delays)

- [x] 6. Keycloak code exchange and cleanup
  - [x] 6.1 `KeycloakCodeExchangeTests`: login form with PKCE, exchange, token validation; a reused code and an invalid code return null
  - [x] 6.2 Phase 10b `tasks.md` checkboxes match the existing tests
  - [x] 6.3 CI check: every container image in the test projects is pinned; no fixed ports, sleeps or Windows-only paths in new or changed tests
  - [x] 6.4 Docs: cleanup SQL note for leftover `UserRole` rows (development only); CLAUDE.md and steering baselines say "all suites pass"
  - [x] 6.5 Build, run all suites (expected: no failures), commit. **PR 3 boundary**, then Jira GU-73
  - Results: unit 1793 / 0; integration 426 / 0; sample 22/22. Found while writing 6.1: the fixture registered `http://localhost:*/auth/callback`, which Keycloak rejects (wildcards only at the end), and Keycloak's Secure session cookies are not sent back by HttpClient over HTTP; the fixture now registers an exact URI and the test carries the cookies itself. `keycloak/realm.json` (dev) has the same `localhost:*` patterns and is raised with the user. CI check: all images pinned (`postgres:16-alpine`, `keycloak:26.3`); localhost ports in tests are data only; the one fixed wait (`TokenRevocationIntegrationTests`, 1.1 s for the issued-at second) is deterministic. CLAUDE.md and steering held no baselines; steering gains a CI-readiness testing rule. Cleanup SQL in `phase-10c-token-revocation/dev-cleanup.md`
