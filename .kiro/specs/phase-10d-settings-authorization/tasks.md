# Tasks: Settings Authorization (GU-77)

Execute one top-level task at a time. After each: build, run tests against the baselines, commit, stop for review.

- [x] 1. Core types and permission keys
  - [x] 1.1 `ISettingsAuthorizer`, `SettingsAuthorizationResult`, `ISettingScopeOwnerResolver`, `SettingsSetupAllowlist` in `GroundUp.Core`
  - [x] 1.2 `AllowAllSettingsAuthorizer` in `GroundUp.Services/Settings`, registered with `TryAddScoped` in `AddGroundUpSettings()`
  - [x] 1.3 Add `settings.manage.tenant`; remove `settings.write` and `settings.delete`
  - [x] 1.4 Unit tests for the allowlist, the default authorizer and the permission keys (commit cca8156; unit 1560 pass / 12 known failures, integration 340 / 48)

- [x] 2. Real authorizer
  - [x] 2.1 `SettingsAuthorizer` in `GroundUp.Auth.Services` and its registration (`services.Replace` in `AddGroundUpAuth`, so call order does not matter)
  - [x] 2.2 Unit tests for every rule in requirements 1 and 3 (31 tests; unit 1591 pass / 12 known failures, integration 340 / 48, sample 22/22)

- [x] 3. Wire into services
  - [x] 3.1 `SettingsService.SetAsync` and `DeleteValueAsync` call the authorizer (`ISettingsAuthorizer` is now a required constructor dependency)
  - [x] 3.2 Attributes on `ISettingsService` and `ISettingsAdminService`
  - [x] 3.3 Fix `AuthorizationInterceptor` so `[RequiresPermission]` uses `HasPermissionAsync` (bypass applies), with tests (decided: inside GU-77)
  - [x] 3.4 `SettingsAuthorizationStartupValidator`; sample `Program.cs` registers both services with `AddAuthorized`
  - [x] 3.5 Unit tests. Existing integration tests that call settings anonymously now run as a test SuperAdmin (`SettingsApiFactory`) or with the allow-all authorizer (`SessionPolicyIntegrationTests`), so the baseline holds (unit 1623 pass / 12 known + 1 flaky; integration 340 / 48; sample 22/22)

- [x] 4. Integration tests and documentation
  - [x] 4.1 `SettingsAuthorizationIntegrationTests` (17 tests, real JWTs and permission service): 401 with no user; 403 for a member without a role; TenantAdmin writes and deletes own tenant, 403 for another tenant and for System; SuperAdmin writes System and any tenant; listing reads 403 / 200 through the fixed bypass; open reads stay open; admin writes SuperAdmin only; setup exception in the first-run state; sample services are proxies and the startup check passes. The negative startup case is covered by unit tests. The wizard's own HTTP tests are among the 41 known Setup failures (GU-73), so the wizard path is covered at the authorizer level only
  - [x] 4.2 Fix `SessionPolicyIntegrationTests` (done in task group 3 to keep the baseline)
  - [x] 4.3 Update `MANUAL_TESTING.md` (Sam as SuperAdmin, first-admin step in Part 1, Part 11 and 14.4 rewritten, troubleshooting), `ARCHITECTURE.md` (settings authorization section), `BUILD_PHASES.md` (sub-phase row, Phase 12 note on group grants), Jira
  - Results: unit 1623 pass / 12 known (+1 flaky property test, GU-73); integration 357 pass / 48 known; sample 22/22
