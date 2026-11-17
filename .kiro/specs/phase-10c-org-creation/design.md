# Design: Authenticated Organization Creation

## Overview

This design separates organization provisioning from identity registration. It extracts the tenant/membership/role creation logic from `NewOrganizationFlowHandler` into a reusable `IOrganizationProvisioningService`, then adds an authenticated `POST /auth/organizations` endpoint that uses that service directly — with no Keycloak round-trip.

## Architecture

### Before

```
POST /auth/signup {orgName}
  → AuthFlowService (kc_action=register) → Keycloak → callback
    → NewOrganizationFlowHandler
        [inline: exchange code, validate nonce, get userinfo,
         resolve/create user, create tenant+membership+role+userrole,
         issue token, write cookies]
```

Org provisioning logic is embedded inside the handler and only reachable via a Keycloak round-trip.

### After

```
POST /auth/signup {orgName}  (brand-new user — unchanged entry)
  → AuthFlowService (kc_action=register) → Keycloak → callback
    → NewOrganizationFlowHandler
        [exchange code, validate nonce, get userinfo, resolve/create user]
        → IOrganizationProvisioningService.ProvisionAsync(userId, externalUserId, orgName)
        [issue token, write cookies]

POST /auth/organizations {orgName}  (authenticated user — new entry)
  → AuthController.CreateOrganization
      [resolve current userId from session (requires tid)]
      → IOrganizationProvisioningService.ProvisionAsync(userId, externalUserId, orgName)
      → 201 { id, name, slug }   (no token reissue, no cookie change)
```

The provisioning logic becomes a single shared service called by both entry points.

## Components

### 1. `IOrganizationProvisioningService` (new, in `GroundUp.Auth.Services`)

```csharp
public interface IOrganizationProvisioningService
{
    /// <summary>
    /// Provisions a new standard organization: creates the tenant, the founding
    /// user's membership, a tenant-scoped TenantAdmin role, and the role assignment —
    /// all in one transaction. Does NOT perform identity-provider calls or user creation.
    /// </summary>
    Task<OperationResult<OrganizationProvisioningResult>> ProvisionAsync(
        OrganizationProvisioningRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record OrganizationProvisioningRequest(
    Guid UserId,
    string ExternalUserId,
    string OrganizationName);

public sealed record OrganizationProvisioningResult(
    Guid TenantId,
    string Name,
    string Slug);
```

**Responsibilities (moved verbatim from `NewOrganizationFlowHandler`):**
- Derive slug via `SlugGenerator.FromOrganizationName`.
- Slug-collision retry loop (`MaxSlugRetries = 5`, disambiguate with `SlugGenerator.Disambiguate`).
- Transaction via `IUnitOfWork.ExecuteInTransactionAsync`:
  - Create `Tenant` (`TenantType.Standard`, `OnboardingMode.InviteOnly`, `RealmName = null`, `IsActive = true`).
  - Set `TenantContext.TenantId` to the new tenant so tenant-scoped repositories target it.
  - Create `UserTenant` membership for `request.UserId` / `request.ExternalUserId`.
  - Create `TenantAdmin` role (`RoleType.Application`, `IsSystem = true`).
  - Create `UserRole` assigning TenantAdmin to the user.
- Return `OrganizationProvisioningResult` with the new tenant's id, name, and slug.

**Error handling:**
- On slug-collision exhaustion → `OperationResult.Fail("...", 409, ErrorCode: "SLUG_COLLISION")`. The marker `SlugCollisionException` becomes a private detail of this service (moved out of the handler).
- The service does NOT throw for business errors — returns `OperationResult`.

**Dependencies** (all already used by the handler): `ITenantRepository`, `IUserTenantRepository`, `IRoleRepository`, `IUserRoleRepository`, `IUnitOfWork`, `TenantContext`, `ILogger`.

**Key design point — user resolution stays with the caller.** The service receives an already-resolved `UserId` + `ExternalUserId`. It does not call the identity provider or create users. This keeps it free of any authentication concern and makes it callable from the authenticated endpoint (which already knows the user) and the signup callback (which resolves the user from Keycloak userinfo first).

### 2. `NewOrganizationFlowHandler` (modified)

- Keeps steps 1–3 (code exchange, nonce validation, userinfo) and user resolution (`ResolveOrCreateUserAsync`).
- Replaces the inline transaction/slug-retry block with a single call to `_organizationProvisioningService.ProvisionAsync(new(userId, userInfo.ExternalUserId, organizationName))`.
- On provisioning failure, maps the `OperationResult` failure to the existing `FlowResult.Error(...)` shape (e.g., `SLUG_COLLISION`), and calls `MarkFailedAsync` as today.
- Keeps token generation + cookie writing (steps 6–7) unchanged.
- Loses the private `SlugCollisionException` and the inline retry loop (moved to the service).

**Net effect:** the handler shrinks to "authenticate the human, resolve the user, delegate org creation, issue session." Behavior is identical.

### 3. `AuthController.CreateOrganization` (new endpoint)

```
POST /auth/organizations
Body: { "organizationName": "Acme Corp" }
```

**Logic:**
1. If `HttpContext.User.Identity?.IsAuthenticated != true` → 401.
2. Read `tid` claim. If absent/invalid (pending-selection principal) → 401. (Requirement 2.3.)
3. Read `userId` from the `sub`/UserIdClaimType claim (GroundUp tokens carry the GroundUp user id as `sub`). If absent/invalid → 401.
4. Resolve `ExternalUserId`: load the user via `IUserRepository.GetByIdAsync(userId)` to get `ExternalUserId` (needed for the membership row). If not found → 401.
5. Validate `organizationName`: non-empty, ≤ 200 chars → else 400.
6. Call `IOrganizationProvisioningService.ProvisionAsync(new(userId, externalUserId, organizationName))`.
7. On success → `201 Created` with `{ id, name, slug }`.
8. On `SLUG_COLLISION` failure → map to the result's status code (409) with `{ error, errorCode }`.
9. No token reissue, no cookie change — the active `tid` is untouched (Requirement 2.8).

**Why load the user for `ExternalUserId`:** the `UserTenant` membership row stores `ExternalUserId` (the per-realm Keycloak sub). For standard tenants this is the user's shared-realm sub. The controller has the GroundUp `userId` from the token but needs the `ExternalUserId` for the membership; a single `GetByIdAsync` provides it. This matches how the signup callback obtains it from userinfo.

### 4. DI registration

Add to `AuthServiceCollectionExtensions`:
```csharp
services.AddScoped<IOrganizationProvisioningService, OrganizationProvisioningService>();
```

## Data Model

No schema changes. The endpoint and service operate on existing entities (`Tenant`, `UserTenant`, `Role`, `UserRole`) exactly as the current signup flow does.

## API Contract

### `POST /auth/organizations`

**Request:**
```json
{ "organizationName": "Acme Corp" }
```

**Responses:**

| Status | Body | Condition |
|---|---|---|
| 201 | `{ "id": "<guid>", "name": "Acme Corp", "slug": "acme-corp" }` | Success |
| 400 | `{ "error": "Organization name is required." }` | Missing/empty/too-long name |
| 401 | (empty) | Unauthenticated, or pending-selection (no tid), or user not resolvable |
| 409 | `{ "error": "...", "errorCode": "SLUG_COLLISION" }` | Slug disambiguation exhausted |

## Testing Strategy

### Unit tests

**`OrganizationProvisioningService` (new test class):**
- Provisions tenant + membership + TenantAdmin role + user-role in one call (happy path).
- Slug derived from org name; disambiguates on collision; fails with `SLUG_COLLISION` after max retries.
- Returns correct tenant id/name/slug.

**`AuthController.CreateOrganization` (add to `AuthControllerTests`):**
- Unauthenticated → 401.
- Pending-selection (no tid) → 401.
- Missing/empty/too-long name → 400.
- Valid request → 201 with org details; provisioning service called with the resolved user id + external id.
- `SLUG_COLLISION` from service → 409.
- Active tid unchanged (no cookie write, no token reissue) — assert cookie writer not invoked.

**`NewOrganizationFlowHandler` (existing tests):**
- Must continue to pass unchanged — now they exercise the handler delegating to the provisioning service. Where tests asserted inline-created entities, they still assert the same outcome because the service produces identical rows. Substitute `IOrganizationProvisioningService` in handler unit tests to assert the handler calls it with the resolved user and maps failures correctly; keep the end-to-end entity assertions in the integration test.

### Integration tests

- `POST /auth/organizations` happy path: authenticated user creates a second org; assert tenant, membership, TenantAdmin role, and user-role rows exist; assert active tid unchanged.
- Pending-selection token → 401.
- Existing `NewOrganizationFlowTests` must pass unchanged (verifies the extraction preserved behavior).

## Manual Re-Testing Required

- MANUAL_TESTING.md Phase 2 (New Organization Signup) — verify brand-new user + first org still works end-to-end through Keycloak.
- MANUAL_TESTING.md Phase 4 — update to create the second org via `POST /auth/organizations` (authenticated, no Keycloak round-trip) instead of a second signup round-trip. This also resolves the `kc_action=register` error that blocked the old approach.

## Migration / Rollout

No data migration. The change is additive (new service + new endpoint) plus an internal refactor of one handler. The signup and login flows keep their existing public contracts.

## Decisions Recorded

- Keep the one-shot NewOrganization signup for brand-new users (register + create org via Keycloak callback).
- `POST /auth/organizations` does not change the active tenant; user switches via `set-tenant`. A setting to auto-switch is future work.
- Enterprise org creation (realm + client provisioning) is out of scope; it remains a separate unimplemented platform-admin flow.
- The provisioning service is identity-provider-agnostic — it never calls Keycloak and never creates users.
- `ExternalUserId` is resolved by a lookup in `POST /auth/organizations`, NOT carried as a token claim. Rationale: `ExternalUserId` is realm-specific (stored per `UserTenant`, see `auth-flows-design.md`), so a single claim would be ambiguous once enterprise tenants exist; no other request-time endpoint needs the userId → ExternalUserId direction; and it avoids growing every token. Revisit if more endpoints need it.
- Known future consideration (not verified, out of scope): when enterprise tenants are implemented, confirm that `User.ExternalUserId` is still the shared-realm `sub`, since standard orgs always belong to the shared realm.
