# Tasks: Settings Authorization (GU-77)

Execute one top-level task at a time. After each: build, run tests against the baselines, commit, stop for review.

- [x] 1. Core types and permission keys
  - [x] 1.1 `ISettingsAuthorizer`, `SettingsAuthorizationResult`, `ISettingScopeOwnerResolver`, `SettingsSetupAllowlist` in `GroundUp.Core`
  - [x] 1.2 `AllowAllSettingsAuthorizer` in `GroundUp.Services/Settings`, registered with `TryAddScoped` in `AddGroundUpSettings()`
  - [x] 1.3 Add `settings.manage.tenant`; remove `settings.write` and `settings.delete`
  - [x] 1.4 Unit tests for the allowlist, the default authorizer and the permission keys (commit cca8156; unit 1560 pass / 12 known failures, integration 340 / 48)

- [ ] 2. Real authorizer
  - [ ] 2.1 `SettingsAuthorizer` in `GroundUp.Auth.Services` and its registration (replaces the default)
  - [ ] 2.2 Unit tests for every rule in requirements 1 and 3

- [ ] 3. Wire into services
  - [ ] 3.1 `SettingsService.SetAsync` and `DeleteValueAsync` call the authorizer
  - [ ] 3.2 Attributes on `ISettingsService` and `ISettingsAdminService`
  - [ ] 3.3 Fix `AuthorizationInterceptor` so `[RequiresPermission]` uses `HasPermissionAsync` (bypass applies), with tests (decided: inside GU-77)
  - [ ] 3.4 `SettingsAuthorizationStartupValidator`; sample `Program.cs` registers both services with `AddAuthorized`
  - [ ] 3.5 Unit tests

- [ ] 4. Integration tests and documentation
  - [ ] 4.1 Integration tests (tenant isolation, TenantAdmin vs System value, plain user 403, login and wizard still work, startup validator failure)
  - [ ] 4.2 Fix `SessionPolicyIntegrationTests` to run as SuperAdmin
  - [ ] 4.3 Update `MANUAL_TESTING.md`, `ARCHITECTURE.md`, `BUILD_PHASES.md`, Jira
