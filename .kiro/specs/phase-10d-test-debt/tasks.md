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

- [ ] 2. Auth tests and the MarkFailed bug
  - [ ] 2.1 Unique permission keys in `AuthorizationProxyIntegrationTests` (2 sites) and `PermissionResolutionTests`
  - [ ] 2.2 Clear `AuthFlowStates` before each test (`IAsyncLifetime`) in the sweeper, bulk-operations and mark-failed test classes. Check the other auth-flow-state test classes for count assertions
  - [ ] 2.3 `MarkFailedAsync` as one conditional update (design). Regression tests: consumed then failed, and expired then failed, each returning 409 with the row unchanged, using one context
  - [ ] 2.4 Cookie-domain test expects 200 JSON with `redirectUrl`, and the cookie domain check is case-insensitive
  - [ ] 2.5 Build, run all suites (expected: no failures except the flaky correlation ID test, fixed in 3), commit. **PR 1 boundary**

## PR 2: Hardening

- [ ] 3. Correlation ID validation
  - [ ] 3.1 `CorrelationIdMiddleware.IsValid` and `MaxLength = 128`; an invalid incoming id is replaced with a new GUID, and nothing is logged
  - [ ] 3.2 Replace the flaky property with two properties (valid ids propagate unchanged, invalid ids are replaced) using explicit generators. Facts for 128 and 129 characters, tab, and CR/LF
  - [ ] 3.3 Build, run all suites, commit

- [ ] 4. Host/token exemptions and Keycloak version
  - [ ] 4.1 `AuthOptions.HostTokenCheckExemptPathPrefixes`, added to the two built-in prefixes, with segment-boundary matching
  - [ ] 4.2 Middleware unit tests (configured prefix, segment boundary, built-ins with an empty list, non-exempt still checked)
  - [ ] 4.3 Sample `appsettings.Development.json` adds `/swagger`
  - [ ] 4.4 `KeycloakFixture` on `keycloak:26.3`, with a comment pointing at the compose file. Keycloak integration tests pass
  - [ ] 4.5 Build, run all suites, commit. **PR 2 boundary**

## PR 3: Keycloak test gaps

- [ ] 5. Keycloak unit tests
  - [ ] 5.1 Extract `KeycloakRetryPolicy.Create(logger, sleepDurationProvider)`; registration unchanged
  - [ ] 5.2 `PollyRetryPolicyPropertyTests` (5xx retried `RetryCount` times then returned, 4xx not retried, 408 retried, exception rethrown after the last attempt, success after failures), with zero delay
  - [ ] 5.3 `KeycloakOptionsSetupTests` (mapping property, change token fires only for Keycloak keys, `CurrentValue` refreshes)
  - [ ] 5.4 Build, run all suites, commit

- [ ] 6. Keycloak code exchange and cleanup
  - [ ] 6.1 `KeycloakCodeExchangeTests`: login form with PKCE, exchange, token validation; a reused code and an invalid code return null
  - [ ] 6.2 Phase 10b `tasks.md` checkboxes match the existing tests
  - [ ] 6.3 CI check: every container image in the test projects is pinned; no fixed ports, sleeps or Windows-only paths in new or changed tests
  - [ ] 6.4 Docs: cleanup SQL note for leftover `UserRole` rows (development only); CLAUDE.md and steering baselines say "all suites pass"
  - [ ] 6.5 Build, run all suites (expected: no failures), commit. **PR 3 boundary**, then Jira GU-73
