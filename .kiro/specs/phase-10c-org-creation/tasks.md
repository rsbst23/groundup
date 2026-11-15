# Implementation Plan: Authenticated Organization Creation

## Overview

Extracts organization provisioning out of `NewOrganizationFlowHandler` into a reusable `IOrganizationProvisioningService`, then adds `POST /auth/organizations` for authenticated users. See `requirements.md` and `design.md` in this folder.

**Execution rule:** one numbered task group at a time. After each group: build, run tests, commit, then stop for review before starting the next group.

**Reminder:** the sample app must be stopped (Shift+F5) before building, otherwise DLL copy fails with file-lock errors.

## Tasks

- [x] 1. Create `IOrganizationProvisioningService` and its implementation (no behavior change yet)
  - [x] 1.1 Add `OrganizationProvisioningRequest` and `OrganizationProvisioningResult` records in `GroundUp.Auth.Services`
    - Request: `UserId`, `ExternalUserId`, `OrganizationName`
    - Result: `TenantId`, `Name`, `Slug`
  - [x] 1.2 Add `IOrganizationProvisioningService` with `ProvisionAsync(request, cancellationToken)` returning `OperationResult<OrganizationProvisioningResult>`
  - [x] 1.3 Implement `OrganizationProvisioningService` by moving the logic from `NewOrganizationFlowHandler` (copy first; the handler is not touched in this group)
    - Slug derivation via `SlugGenerator.FromOrganizationName`, retry loop (max 5) with `SlugGenerator.Disambiguate`
    - Single `IUnitOfWork.ExecuteInTransactionAsync`: Tenant (`Standard`, `InviteOnly`, `RealmName = null`), set `TenantContext.TenantId`, `UserTenant`, `TenantAdmin` role (`IsSystem = true`), `UserRole`
    - Private `SlugCollisionException` marker moves into this class
    - Slug exhaustion returns `OperationResult.Fail(..., 409, "SLUG_COLLISION")` (no exception escapes)
  - [x] 1.4 Register `IOrganizationProvisioningService` as scoped in `AuthServiceCollectionExtensions`
  - [x] 1.5 Unit tests for `OrganizationProvisioningService` (xUnit + NSubstitute)
    - Happy path creates tenant, membership, TenantAdmin role, user-role and returns correct id/name/slug
    - Slug derived from the organization name
    - Collision on first attempt retries with a disambiguated slug
    - Collision on all attempts returns `SLUG_COLLISION` / 409
    - Tenant context is set to the new tenant before tenant-scoped repository calls
  - [x] 1.6 Build and run the new unit tests; commit

- [x] 2. Refactor `NewOrganizationFlowHandler` to delegate to the service
  - [x] 2.1 Inject `IOrganizationProvisioningService` into the handler; remove the now-unused tenant, role, user-role and unit-of-work dependencies. Keep `IUserRepository` (user resolution) and `TenantContext` (see 2.2)
  - [x] 2.2 Replace the inline transaction and slug-retry block with a single `ProvisionAsync` call
    - The service restores `TenantContext` to its previous value before returning, so after a successful call the handler sets `_tenantContext.TenantId` to the new tenant id itself (preserves today's behavior for token generation)
    - On failure: `MarkFailedAsync` and return `FlowResult.Error` carrying the service's error code (e.g. `SLUG_COLLISION`), same as today
    - **Behavior change to be aware of:** the founding user is resolved/created before provisioning, outside the provisioning transaction. If provisioning fails, a newly created user row remains (with zero memberships, which the Login flow already handles). Previously the user creation was inside the transaction and rolled back
  - [x] 2.3 Remove the handler's private `SlugCollisionException` and `MaxSlugRetries`
  - [x] 2.4 Reorganize handler unit tests
    - Substitute `IOrganizationProvisioningService`; assert it is called with the resolved user id, external id and organization name
    - Assert provisioning failure maps to `FlowResult.Error` and marks the flow failed
    - Keep tests for code-exchange failure, nonce mismatch, userinfo failure, token generation, and cookie writing
    - Entity-level assertions (rows created) now live in the service tests (1.5) and the integration tests
  - [x] 2.5 Run the existing `NewOrganizationFlowTests` integration tests (Docker required) to prove behavior is unchanged
  - [x] 2.6 Build, run all auth unit tests, commit
  - [x] 2.7 Fix rejected entities staying tracked after a failed save (found while running 2.5)
    - Root cause: `BaseRepository` caught `DbUpdateException` but left the entity Added/Modified, so every later `SaveChanges` on the same context failed. Slug retries therefore never succeeded.
    - Added `BaseRepository.DiscardRejectedChanges` and called it from every repository `DbUpdateException` handler: `BaseRepository` and `BaseTenantRepository` Add/Update, `TenantRepository` Add/Update overrides, `RoleRepository` and `PolicyRepository` junction assign/remove
    - New SQLite-backed `BaseRepositoryConflictTests` (the InMemory provider cannot enforce unique indexes)
    - `NewOrganizationFlowTests` happy-path and collision tests now use unique org names (the fixture database is shared)
    - Not changed: `SettingsService` and `IdentityBootstrapService` catch `DbUpdateException` inside their own transactions; review separately
  - **Manual re-test needed after this group:** MANUAL_TESTING.md Phase 2 (brand-new user + first org through Keycloak)

- [x] 3. Add `POST /auth/organizations` to `AuthController`
  - [x] 3.1 Add `CreateOrganizationRequestDto(string? OrganizationName)` in `GroundUp.Auth.Core.Dtos` (next to `SignupRequestDto`)
  - [x] 3.2 Inject `IOrganizationProvisioningService` into `AuthController`; update the `CreateController` helper in `AuthControllerTests`
  - [x] 3.3 Implement `CreateOrganization`
    - 401 if unauthenticated, if no valid `tid` claim (pending-selection), or if the `sub` user id cannot be parsed
    - Load the user with `IUserRepository.GetByIdAsync` to obtain `ExternalUserId`; 401 if not found or inactive
    - The name is trimmed before validation and storage
    - 400 if `organizationName` is null, empty or whitespace, or longer than 200 characters (same inline check style as `Signup`)
    - Call `ProvisionAsync`; on success return `201` with `{ id, name, slug }`
    - On failure return the result's status code with `{ error, errorCode }`
    - No cookie write and no token reissue (the active `tid` is unchanged)
  - [x] 3.4 Controller unit tests
    - Unauthenticated returns 401
    - Pending-selection (no `tid`) returns 401
    - Unparseable `sub` returns 401
    - User not found returns 401
    - Missing, empty, whitespace and over-200-character names return 400
    - Valid request returns 201 with id, name and slug, and the service receives the resolved user id and external id
    - `SLUG_COLLISION` from the service returns 409
    - Cookie writer is never invoked (active tenant unchanged)
  - [x] 3.5 Build, run all auth unit tests, commit

- [x] 4. Integration tests for `POST /auth/organizations` (Docker required)
  - [x] 4.1 Add `CreateOrganizationTests` using `AuthFlowTestBase` and `Fixture.GenerateTestToken`
    - Happy path: user in tenant A creates org B; assert 201, and that tenant, membership, TenantAdmin role and user-role rows exist for B
    - Active tenant unchanged: no `AuthToken` Set-Cookie on the response
    - Created org appears in `GET /auth/me/tenants` for that user
    - Unauthenticated returns 401
    - Pending-selection token (no `tid`) returns 401
    - Invalid name returns 400
    - Slug collision produces a disambiguated slug for the second org with the same name
  - [x] 4.2 Build, run the new integration tests plus the existing auth integration tests, commit

- [x] 5. Update MANUAL_TESTING.md
  - [x] 5.1 Phase 4.1: create the second org with `POST /auth/organizations` (authenticated, no Keycloak round-trip) instead of a second signup
    - Note that the new org does not change the active tenant; use `set-tenant` to switch
    - Note the `kc_action=register` problem for already-authenticated users and that this endpoint avoids it
  - [x] 5.2 Add a short "Create another organization" subsection covering the 401, 400 and 201 cases (error table in 4.1)
  - [x] 5.3 Commit
  - Also updated: Phase 7 rewritten for the last-TenantAdmin rule (regular member leaves; sole admin is blocked)
  - Correction: an earlier revision told readers to send POSTs from PowerShell because of a supposed CSRF 403. Manual testing showed cookie POSTs succeed, because antiforgery services are not registered and `CsrfProtectionMiddleware` silently skips. The guide is back on Swagger.

## Follow-ups (outside this spec)

- **Signup now uses `prompt=create`, not `kc_action=register`** (Keycloak upgraded 26.0.7 → 26.3; `prompt=create` needs 26.1+). `requirements.md` and `design.md` in this folder describe the old parameter and the "Unexpected error" seen when a logged-in user went through signup again. The cause was never isolated. `kc_action=register` is not a valid required action, which is the likely culprit, but that is unconfirmed. Still open: what Keycloak does when a user who already has an SSO session hits `POST /auth/signup` with `prompt=create`. Check by hand (log in, then call signup) and record the result here. It does not change the design: logged-in users create organizations with `POST /auth/organizations`.
- **`KeycloakFixture` (integration tests) still runs `keycloak:26.0`.** Bump it to 26.3 so tests and dev match.
- **CSRF protection spec (agreed with the user).** CSRF protection is currently inactive: nothing calls `AddAntiforgery()`, so `CsrfProtectionMiddleware` skips every request, and even when registered, `ProvisionAntiforgeryToken` discards the request token so a front end could never send it. Requirements must keep every cookie HttpOnly (compliance scanners flag non-HttpOnly cookies), state the expected header name explicitly (the middleware doc says `X-CSRF-Token`; nothing configures it), and add integration tests for the cookie path a real front end uses. Also review: broad cookie Domain (`.<default-domain>`), persistent cookie expiry, SameSite Lax vs Strict.
- **Pre-existing failing tests not investigated:** `AuthorizationProxyIntegrationTests`, `PermissionResolutionTests`, four AuthFlowState repository/sweeper tests, `SecurityControlsTests` (still expects a 302), plus 13 unit tests (Setup wizard, `KeycloakStartupValidatorPropertyTests`, `DefaultScopeChainProviderTests`).
- **Transaction-scoped `DbUpdateException` handlers** in `SettingsService` and `IdentityBootstrapService` were not reviewed for the change-tracker issue fixed in `BaseRepository`.

- [ ] 6. Final checkpoint
  - [ ] 6.1 Full solution build and the full auth unit and integration suites pass (pre-existing unrelated failures noted separately)
  - [ ] 6.2 Manual re-test list for the user: Phase 2 (signup still works), Phase 4 (new endpoint, picker, set-tenant), Phase 6 (tenant list shows both orgs)

## Notes

- Enterprise organization creation (realm and client provisioning) is out of scope. `auth-flows-design.md` scenarios 15–16 still describe it and it remains unimplemented.
- Validation follows the existing inline pattern used by `Signup`. If the project later moves auth request validation to FluentValidation validators in `.Core`, both endpoints should move together.
- `ExternalUserId` is looked up in the endpoint rather than carried as a token claim (see Decisions Recorded in `design.md`).

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "1.4"] },
    { "id": 2, "tasks": ["1.5", "1.6"] },
    { "id": 3, "tasks": ["2.1", "2.2", "2.3"] },
    { "id": 4, "tasks": ["2.4", "2.5", "2.6"] },
    { "id": 5, "tasks": ["3.1", "3.2", "3.3"] },
    { "id": 6, "tasks": ["3.4", "3.5"] },
    { "id": 7, "tasks": ["4.1", "4.2"] },
    { "id": 8, "tasks": ["5.1", "5.2", "5.3"] },
    { "id": 9, "tasks": ["6.1", "6.2"] }
  ]
}
```
