# Requirements: Settings Authorization (GU-77)

## Context

`SettingsService`, `SettingsAdminService` and the settings controllers have no authorization. Anyone who can reach the API can change any setting, including `auth.session.token-lifetime-minutes`, `auth.session.absolute-lifetime-minutes` and `auth.application.default-domain`. GU-72 moved this work here (its Requirement 4.7).

Authorization belongs in the service layer (SDK-first). Controllers stay thin and get no checks.

Two facts shape the design:

1. **Some checks depend on the call's arguments.** `SetAsync(key, value, levelId, scopeId)` is allowed or denied depending on the level and scope. The existing `[RequiresPermission]` attribute and proxy decide from the method name only, so they cannot express this.
2. **Some callers have no user.** Login code, `SessionPolicyProvider`, `KeycloakOptionsSetup`, startup seeders and the setup wizard use the settings services before anyone is authenticated.

## Scope

**In scope**
- Authorization for every settings write, delete and listing read, and for the admin service.
- The setup-wizard exception.
- Documentation and tests.

**Out of scope**
- Object-level (row-level) security, such as groups that see only some documents. That belongs to `GroundUp.Security` (`ResourceAccess`, `BaseSecuredRepository`) and will be designed in its own phase. This spec only keeps a seam for it (Requirement 6).
- Revealing secret values through the API (decided: never; see Requirement 5).
- Token revocation (GU-71), non-browser clients (GU-79), API keys (GU-80).

## Decisions already made

| # | Decision |
|---|---|
| D1 | Static rules use the existing `[RequiresRole]` and `[RequiresPermission]` attributes with the `AddAuthorized` proxy. Argument-dependent rules use a new `ISettingsAuthorizer` called inside the service. |
| D2 | No setting-level exceptions except the setup allowlist (Requirement 3). |
| D3 | Admin service writes (levels, groups, definitions) are SuperAdmin only. |
| D4 | Listing reads need `settings.read`. Internal effective-value reads stay open. |
| D5 | Secrets stay masked for everyone. No reveal path. |
| D6 | With no auth module registered, the default authorizer allows everything. Once the auth module is registered, its authorizer replaces the default and cannot be switched off by configuration. |
| D7 | The level named `Tenant` is the tenant level. A custom hierarchy supplies its own `ISettingScopeOwnerResolver`. |

## Requirements

### Requirement 1: Value writes and deletes are authorized by level and scope

**User story:** As a system administrator I manage System values. As a TenantAdmin I manage my own tenant's values. Nobody else changes values.

#### Acceptance Criteria

1. `SettingsService.SetAsync` and `DeleteValueAsync` SHALL call `ISettingsAuthorizer` before changing anything. `DeleteValueAsync` SHALL load the stored value first to learn its key, level and scope.
2. A write or delete at the System level, or at any level above the Tenant level, SHALL require the SuperAdmin role.
3. A write or delete at the Tenant level SHALL be allowed when the caller is SuperAdmin, or when `scopeId` equals the current tenant (`ITenantContext.TenantId`) and the caller holds `settings.manage.tenant` (TenantAdmin holds it through the existing bypass). It SHALL be denied for any other tenant.
4. A write or delete at a level below Tenant SHALL be allowed for SuperAdmin. For other callers, `ISettingScopeOwnerResolver` SHALL map `(levelId, scopeId)` to the owning tenant, and Criterion 3 SHALL then apply to that tenant. With no resolver registered, these levels SHALL be SuperAdmin only.
5. A denied call SHALL return `OperationResult.Forbidden()`. A call with no authenticated user SHALL return Unauthorized. Nothing is written, and no setting-changed event is published.
6. Existing write rules (allowed levels, min/max, `SessionPolicyWriteValidator`) SHALL run after authorization and remain unchanged.

### Requirement 2: Listing reads need `settings.read`

#### Acceptance Criteria

1. `GetAllForScopeAsync` and `GetGroupAsync` (both overloads) SHALL require `settings.read`.
2. `GetAsync<T>` and `GetTypedValueAsync` SHALL stay open, so login, the session policy, the Keycloak options and other internal code keep working.
3. `settings.read` SHALL be held by TenantAdmin (through the bypass) and SuperAdmin.
4. The settings controller SHALL not change behavior except that the endpoint now returns 403 when the caller lacks the permission.

### Requirement 3: Setup exception

**User story:** As the person running first-time setup, I can configure the application before any user exists.

#### Acceptance Criteria

1. While `IBootstrapStateService.IsCompleteAsync()` is false, `SetAsync` SHALL be allowed without a user only for these keys, only at the System level: `app.identity.name`, `auth.application.default-domain`, `auth.keycloak.public-base-url`, `auth.keycloak.internal-base-url`, `auth.keycloak.shared-realm-name`, `auth.keycloak.admin-client-id`, `auth.keycloak.admin-client-secret`.
2. While setup is incomplete, `EnsureDefinitionAsync` SHALL be allowed without a user for any key. It creates definitions and never sets a value.
3. Any other write, and any delete, during setup SHALL follow Requirements 1 and 2.
4. After setup completes, no exception SHALL apply. The allowlist lives in one place, a constant used by the authorizer, and tests SHALL fail if the wizard writes a key that is not on it.
5. The multi-instance lag of the bootstrap state cache (up to 60 seconds) is accepted and documented.

### Requirement 4: Admin service

#### Acceptance Criteria

1. Create, update and delete for levels, groups and definitions SHALL require the SuperAdmin role, through `[RequiresRole]` on the interface methods and registration with `AddAuthorized`.
2. `GetAllLevelsAsync` SHALL stay open (used by `SessionPolicyProvider` during login). The other admin reads (`GetAllGroupsAsync`, `GetAllDefinitionsAsync`, `GetDefinitionByIdAsync`) SHALL require `settings.read`.
3. Startup seeders and the wizard use `EnsureDefinitionAsync` (on `ISettingsService`), not the admin service, so they are not affected. A test SHALL confirm no internal caller reaches an admin write.

### Requirement 5: Secrets

#### Acceptance Criteria

1. Secret values SHALL stay masked in every API read. No endpoint or permission returns an unmasked value.
2. Writing a secret SHALL follow Requirements 1 and 3 like any other value.
3. Error messages and logs SHALL not contain the value being written.

### Requirement 9: Registration is enforced at startup

#### Acceptance Criteria

1. When the auth module's settings authorizer is registered, the application SHALL refuse to start unless `ISettingsService` and `ISettingsAdminService` are registered through `AddAuthorized`. The error message SHALL name the missing registration.
2. The check SHALL apply in every environment, with no Development exemption.
3. Applications without the auth module SHALL not be affected.

### Requirement 6: Seam for later object-level security

#### Acceptance Criteria

1. `ISettingsAuthorizer` and `ISettingScopeOwnerResolver` SHALL live in `GroundUp.Core` and SHALL not reference the auth or security modules.
2. A future module SHALL be able to replace either by registering its own implementation, with no change to settings code.

### Requirement 7: Permission keys

#### Acceptance Criteria

1. Add `settings.manage.tenant` to `PermissionKeysAuth` and to the permission seeder.
2. Keep `settings.read`. Remove the unused `settings.write` and `settings.delete`, or mark them obsolete (decided in design after checking that no seeded role references them).

### Requirement 8: Tests and documentation

#### Acceptance Criteria

1. Unit tests SHALL cover each rule in Requirements 1 to 4 and the allowlist.
2. Integration tests SHALL cover: tenant A cannot write tenant B's values; a TenantAdmin can write the tenant value for session timeouts but not the System value; a user with no role gets 403 on every write; login, token refresh and the setup wizard still work.
3. `MANUAL_TESTING.md` and `ARCHITECTURE.md` SHALL drop the "no authorization yet (GU-77)" notes and describe the new rules.
4. Test baselines SHALL not get worse: unit 1543 pass / 12 known failures; integration 340 pass / 48 known failures; sample 22/22.

## Open items for the design document

1. **The authorization proxy skips the admin bypass.** `AuthorizationInterceptor` checks `[RequiresPermission]` by reading `GetUserPermissionsAsync` and testing set membership. The SuperAdmin and TenantAdmin bypass exists only in `HasPermissionAsync` and `HasAnyPermissionAsync`, so the proxy would deny a TenantAdmin who lacks the explicit permission. Either the interceptor changes to use the bypassing methods (a small fix to existing behavior, with tests), or the seeded roles must hold every permission. To decide in design.
2. The proxy returns Forbidden (403) for an unauthenticated caller, not Unauthorized (401). Requirement 1.5 asks for 401 on the authorizer path. Decide whether to align the proxy or accept the difference for attribute-protected methods.
3. Whether `settings.write` and `settings.delete` are referenced by seeded roles (Requirement 7.2).
