# Implementation Plan: Sessions Without a Current Tenant

## Overview

Adds a tenantless session (signed in, no current tenant), applies the leave rule, and makes tenant-scoped data reject tenantless callers with 403. See `requirements.md` and `design.md` in this folder.

**Execution rule:** one numbered task group at a time. After each group: build, run tests, commit, then stop for review before starting the next group.

**Reminder:** the sample app must be stopped (Shift+F5) before building, otherwise DLL copy fails with file-lock errors.

**Baseline to compare against:** auth unit 1 known failure (`KeycloakStartupValidatorPropertyTests.StartAsync_BootstrapIncompletePartialSettings_ThrowsForMissing`); full unit 13 known failures; auth integration 7 known failures (`AuthorizationProxyIntegrationTests`, `PermissionResolutionTests`, four AuthFlowState tests, `SecurityControlsTests.Callback_Login_CookieDomainAttribute_IsDotPrefixedDefaultDomain`).

## Tasks

- [x] 1. Tenantless token and session-service issuance
  - [x] 1.1 Add `ITokenService.GenerateTenantlessTokenAsync(Guid userId, IEnumerable<Claim>? additionalClaims = null)` and implement it in `TokenService`
    - Claims: `sub`, `email`, `name`, plus additional claims. No `tid`, no `role` (not even system roles)
    - Signing key from `GetSigningKeyAsync(Guid.Empty)`; document on `ISigningKeyProvider` that `Guid.Empty` means the system key
    - Returns null for an unknown or inactive user
  - [x] 1.2 Add `IAuthSessionService.IssueTenantlessSessionAsync(Guid userId, DateTimeOffset? originalAuthTime)` returning `OperationResult<string>`; `auth_time` = original or now
  - [x] 1.3 Add `AuthSessionService.SetTenantAsync` absolute-lifetime check: when `originalAuthTime` is supplied and `now - originalAuthTime >= AbsoluteSessionLifetimeMinutes`, return `Unauthorized`
  - [x] 1.4 Unit tests
    - Tenantless token has `sub`/`email`/`name`/`auth_time`/`iat`, no `tid`, no `role`
    - Tenantless token validates through `ValidateTokenAsync`
    - Unknown and inactive users return null
    - `IssueTenantlessSessionAsync` preserves a supplied `auth_time` and sets now when none is supplied
    - `SetTenantAsync` rejects a session past the absolute lifetime and accepts one just inside it
    - Generated tenant-scoped tokens carry `iat` (guards the revocation spec's assumption)
  - [x] 1.5 Build, run auth unit tests, commit

- [x] 2. Reject tenantless callers at the tenant repository
  - [x] 2.1 Add `ErrorCodes.TenantRequired` and a resource string for its message
  - [x] 2.2 In `BaseTenantRepository`, return `OperationResult.Forbidden` with `TenantRequired` (403) before any database access when `TenantContext.TenantId == Guid.Empty`, for `GetAllAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync` and export
  - [x] 2.3 Unit tests: each entry point returns 403 `TENANT_REQUIRED` for an empty tenant and does not touch the database; behavior with a real tenant is unchanged
  - [x] 2.4 Run the **full** unit and integration suites. Fix every newly broken caller by using the existing explicit-tenant or bypass methods, never by weakening the guard. List each fixed caller in the commit message
  - [x] 2.5 Commit

- [x] 3. Principal classification, `FlowResult` and `LoginFlowHandler`
  - [x] 3.1 Add `AuthConstants.ExternalIdpAuthenticationType` (`"ExternalIdP"`) and use it in `JwtAuthenticationMiddleware`; add an `IsExternalIdp(ClaimsPrincipal)` helper
  - [x] 3.2 `FlowResult`: add `IsTenantless` and a `NoCurrentTenant()` factory; `TenantSelectionRequired(list)` also sets `IsTenantless`
  - [x] 3.3 `LoginFlowHandler`
    - 2+ standard tenants: issue a tenantless token, write the auth cookie **and** the `IdToken` cookie, return `TenantSelectionRequired(list)`. Stop writing the Keycloak access token
    - 0 memberships and no default tenant configured: issue a tenantless token, write both cookies, return `NoCurrentTenant()`
    - A configured default tenant that does not resolve to an active tenant stays `ACCESS_DENIED` 403. Enterprise-only memberships stay 403
  - [x] 3.4 Unit tests for each branch above, including that no Keycloak token is written to the cookie
  - [x] 3.5 Build, run auth unit tests, commit

- [x] 4. `AuthController` changes
  - [x] 4.1 `Callback`: for `NoCurrentTenant`, respond `{ requiresTenantSelection: false, tenants: [] }`; picker response unchanged
  - [x] 4.2 `ResolveGroundUpUserId()`: return null for an identity-provider principal; use the user id from `sub` for GroundUp principals with or without `tid`
  - [x] 4.3 `SetTenant`: classify with `IsExternalIdp`; a GroundUp principal (tenantless or not) uses its own user id and `auth_time`; map the new absolute-lifetime rejection to 401
  - [x] 4.4 `CreateOrganization`: remove the `tid` requirement; keep the GroundUp-user and active-user checks; the session stays tenantless
  - [x] 4.5 `LeaveTenant`
    - Left tenant equals the session's `tid`: issue a tenantless token with the same `auth_time`, write the cookie, respond `{ success: true, currentTenantId: null }`. If issuing fails, clear the cookie and still report success
    - Otherwise: no cookie change, respond `{ success: true, currentTenantId: <tid or null> }`
    - Remove `noTenants` and `remainingMemberships.First()`
  - [x] 4.6 Update `AuthControllerTests`; add cases for leaving with 0, 1 and 3 other memberships (never an automatic switch), leaving a non-current tenant, tenantless `set-tenant`, tenantless `organizations`, identity-provider principal handling
  - [x] 4.7 Build, run auth unit tests, commit

- [x] 5. Integration tests
  - [x] 5.1 Rewrite `PendingSelectionTests` for the tenantless token (cookie holds a GroundUp token, not Keycloak's)
  - [x] 5.2 `LeaveTenantTests`: leave current tenant with 0, 1 and 3 other memberships (tenantless afterwards, no switch); leave a non-current tenant (cookie unchanged); old `noTenants` assertions replaced
  - [x] 5.3 New `TenantlessSessionTests`: `me` (null tenant), `me/tenants` (none current), `set-tenant` from tenantless, `organizations` from tenantless (201, still tenantless, `set-tenant` enters it), `logout`
  - [x] 5.4 A tenant-scoped endpoint over HTTP returns 403 `TENANT_REQUIRED` for a tenantless session and does not insert a row (already covered for the sample `/api/projects` endpoint in group 2 by `TenantIsolationTests.NoCurrentTenant_CreateAndRead_Return403TenantRequiredAndWriteNothing`; add an auth-suite equivalent only if it adds coverage)
  - [x] 5.5 Login with zero memberships and no default tenant returns a tenantless session (in `LoginFlowTests`)
  - [x] 5.6 Run the full auth integration suite; only the 7 known failures remain; commit

- [ ] 6. Documentation and final checkpoint
  - [ ] 6.1 `MANUAL_TESTING.md`: Phase 3 (login with no memberships and no default tenant), Phase 7 (leave current vs other tenant, no automatic switch, new response shape), and a new tenantless-session section
  - [ ] 6.2 Update `auth-flows-design.md` for the tenantless state and the new leave behavior
  - [ ] 6.3 Document the guard's limit: custom services that never use a tenant-aware repository must check `ITenantContext.TenantId` themselves
  - [ ] 6.4 Full build, full unit and integration runs compared against the baseline; list follow-ups
  - [ ] 6.5 Commit and stop for review

## Follow-ups found during this spec (not part of it)

- 41 failing `GroundUp.Tests.Integration.Setup.*` tests. They fail on a clean checkout with `System.InvalidOperationException: Scheme already exists: BootstrapAdminToken` while the test host builds its authentication options. Needs its own investigation.
- 13 failing unit tests (11 `SetupWizard*`, `KeycloakStartupValidatorPropertyTests.StartAsync_BootstrapIncompletePartialSettings_ThrowsForMissing`, `DefaultScopeChainProviderTests.GetScopeChainAsync_TenantLevelNotFound_ReturnsEmptyList`).
- 7 failing auth integration tests (listed in the baseline above).

## Manual re-testing required after implementation

- Phase 7: leave the current tenant with other memberships and confirm no current tenant and no automatic move
- Phase 3: sign in as a user with no memberships and no default tenant
- Tenantless section: `me`, `me/tenants`, `organizations`, `set-tenant`, `logout`
