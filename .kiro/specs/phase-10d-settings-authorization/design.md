# Design: Settings Authorization (GU-77)

Implements `requirements.md` in this folder. Rules, permission keys and the setup allowlist are defined there and not repeated here.

## 1. Components

| Component | Project | Role |
|---|---|---|
| `ISettingsAuthorizer` | `GroundUp.Core/Abstractions` | Decides write and delete (by key, level, scope). |
| `ISettingScopeOwnerResolver` | `GroundUp.Core/Abstractions` | Optional. Maps `(levelId, scopeId)` of a level below Tenant to the owning tenant id. |
| `AllowAllSettingsAuthorizer` | `GroundUp.Services/Settings` | Default. Registered with `TryAddScoped` by `AddGroundUpSettings()`. Used only when no auth module is registered. |
| `SettingsAuthorizer` | `GroundUp.Auth.Services/Settings` | Real implementation. Registered by the auth module's `AddGroundUpAuth...` method, replacing the default. |
| `SettingsSetupAllowlist` | `GroundUp.Core` | Constant list of the seven wizard keys. |
| `SettingsAuthorizationStartupValidator` | `GroundUp.Auth.Services` | Hosted service. Fails startup if the static checks are not wired (section 4). |

`ISettingsAuthorizer` has two methods:

```csharp
Task<SettingsAuthorizationResult> AuthorizeWriteAsync(string key, Guid levelId, string? levelName, Guid? scopeId, CancellationToken ct);
Task<SettingsAuthorizationResult> AuthorizeDeleteAsync(string key, Guid levelId, string? levelName, Guid? scopeId, CancellationToken ct);
```

`SettingsAuthorizationResult` is `Allowed`, `Forbidden` or `Unauthenticated`. `SettingsService` maps it to `OperationResult` (Ok, Forbidden, Unauthorized).

Listing reads and admin writes use the existing attributes (D1, Option B). They need no authorizer methods.

## 2. Write and delete flow

`SetAsync`: load definition, then run the authorizer, then the existing validations, then save.
`DeleteValueAsync`: load the value with its definition and level, then run the authorizer, then delete.

Authorizing right after the definition lookup keeps the order stable: a caller without rights learns nothing about validation rules. A missing definition still returns 404 before the check (the key is not a secret).

`SettingsAuthorizer.AuthorizeWriteAsync`:

1. If setup is incomplete and `key` is on the allowlist and the level name is `System` → Allowed. (No user needed.)
2. If `ICurrentUser.UserId == Guid.Empty` → Unauthenticated.
3. If the user has the SuperAdmin system role (`IPermissionService.HasSystemRoleAsync`) → Allowed.
4. If the level name is `Tenant`: owner tenant = `scopeId`. Else if the level is below Tenant: owner tenant from `ISettingScopeOwnerResolver` (none registered or no answer → Forbidden). Any level above Tenant → Forbidden.
5. If owner tenant equals `ITenantContext.TenantId` and `IPermissionService.HasPermissionAsync(userId, "settings.manage.tenant")` → Allowed (this includes the TenantAdmin bypass). Otherwise Forbidden.

"Below Tenant" is decided from the level name: `Tenant` is the tenant level, `System` is above it, and every other level is looked up through the resolver. The resolver returns null for levels it does not own, so an unknown level is denied (fail closed).

Delete runs the same logic with the level and scope of the stored value.

The setup check uses `IBootstrapStateService.IsCompleteAsync()` (cached 60 seconds). The allowlist branch is evaluated first, so a failure to read bootstrap state means the branch is skipped and normal rules apply.

## 3. Static checks (attributes)

- `ISettingsAdminService`: `[RequiresRole("SuperAdmin")]` on create, update and delete for levels, groups and definitions. `[RequiresPermission("settings.read")]` on `GetAllGroupsAsync`, `GetAllDefinitionsAsync`, `GetDefinitionByIdAsync`. `GetAllLevelsAsync` has no attribute.
- `ISettingsService`: `[RequiresPermission("settings.read")]` on both overloads of `GetAllForScopeAsync` and `GetGroupAsync`. All other methods have no attribute.
- The consuming app registers both through `AddAuthorized` (sample `Program.cs` shows it).

`AddAuthorized` registers the concrete class as well as the interface, which does not conflict with `AddGroundUpSettings()` because the interface registration is replaced by the later call.

**Known gap in the proxy, handled here:** `AuthorizationInterceptor` checks `[RequiresPermission]` against the permission set and skips the SuperAdmin and TenantAdmin bypass. A TenantAdmin without an explicit `settings.read` would be denied.
- Correction found in task group 1: the TenantAdmin role is created with no policies (`OrganizationProvisioningService`), so it relies on the bypass alone. SuperAdmin's `FullAccess` policy links every seeded permission, so it is covered.
- Decision (approved): fix `AuthorizationInterceptor` inside GU-77 (task 3.3). Every `[RequiresPermission]` check, in all three code paths (OperationResult, non-generic, exception), SHALL use `IPermissionService.HasPermissionAsync` per permission (AND semantics kept), so the SuperAdmin and TenantAdmin bypass applies. `[RequiresRole]` is unchanged. Tests cover SuperAdmin, TenantAdmin of the current tenant, TenantAdmin of another tenant (denied), a user with the explicit permission, and a user with none. The change affects every service registered with `AddAuthorized`, so the existing `AuthorizationProxyIntegrationTests` and unit tests must still pass.

The proxy returns 403 for an unauthenticated caller on attributed methods. The authorizer path returns 401. This difference is accepted and documented.

## 4. Enforcing registration at startup

If an app registers the auth module but not `AddAuthorized` for the settings services, the static checks would be open. `SettingsAuthorizationStartupValidator` (hosted service, same pattern as `AuthCookieStartupValidator`) prevents that:

- It runs only when the real `SettingsAuthorizer` is registered (that means the auth module is in use).
- It creates a scope and resolves `ISettingsService` and `ISettingsAdminService`. If either instance is a `SettingsService` or `SettingsAdminService` (not the proxy), it throws `InvalidOperationException` with a message naming the missing `AddAuthorized<...>()` call.
- The exception stops the host at startup, in every environment. No Development exemption (same rule as CSRF).
- The proxy instance is a `DispatchProxy` subclass, so it is never of the concrete type. This makes the check reliable.

Apps without the auth module are not affected (D6: allow all).

## 5. Permission keys and seeding

- Add `SettingsManageTenant = "settings.manage.tenant"` to `PermissionKeysAuth` and `All`.
- Remove `SettingsWrite` and `SettingsDelete` from the constants and `All`. Existing database rows stay and are harmless (no code requires them). Mentioned in release notes.
- `DefaultPermissionSeeder` already handles `All`.

## 6. Test impact

- `SessionPolicyIntegrationTests` writes and deletes settings with no user. After this change it runs as SuperAdmin (existing test token helper) or sets the setup state to incomplete. Chosen in task group 4.
- Unit tests that construct `SettingsService` need an `ISettingsAuthorizer`. A test helper uses `AllowAllSettingsAuthorizer`.
- The wizard allowlist test reads the keys the wizard writes (by running the wizard steps against a recording authorizer) and compares them to `SettingsSetupAllowlist`.

## 7. Seam for object-level security

Both interfaces live in Core and have no auth or security references. A future Security module registers its own `ISettingScopeOwnerResolver` or replaces `ISettingsAuthorizer`. Settings code does not change.

## 8. Risks

| Risk | Handling |
|---|---|
| Wizard step writes a key missing from the allowlist and setup breaks | Allowlist test (section 6). Manual run of the wizard in task group 4. |
| Bootstrap state cache lags on multi-instance setups | Accepted (requirement 3.5). Only affects the first 60 seconds after setup. |
| A custom level is named `Tenant` by an unrelated app | Documented. The resolver can override. |
| Cached effective values after a denied write | A denied write never reaches the save or the event, so no cache invalidation happens. |
| Secret in logs | The authorizer logs key, level and user id only, never the value. |

## 9. Task groups (each ends with build, tests, commit, and a stop for review)

1. **Core and permission keys.** `ISettingsAuthorizer`, `ISettingScopeOwnerResolver`, `SettingsAuthorizationResult`, `SettingsSetupAllowlist`, `AllowAllSettingsAuthorizer` plus registration, new permission key, removal of the two unused keys, seeder check for TenantAdmin and SuperAdmin grants. Unit tests.
2. **Real authorizer.** `SettingsAuthorizer` in Auth.Services with registration, unit tests for every rule and the setup branch.
3. **Wire into services.** `SettingsService` calls the authorizer in `SetAsync` and `DeleteValueAsync`. Attributes on both interfaces. `SettingsAuthorizationStartupValidator`. Sample `Program.cs` uses `AddAuthorized` for both services. Unit tests.
4. **Integration tests and docs.** Tenant isolation, TenantAdmin and System values, 403 for plain users, login and wizard still work, startup validator fails when `AddAuthorized` is missing. Fix `SessionPolicyIntegrationTests`. Update `MANUAL_TESTING.md`, `ARCHITECTURE.md`, Jira, `BUILD_PHASES.md` (future document-security note).
