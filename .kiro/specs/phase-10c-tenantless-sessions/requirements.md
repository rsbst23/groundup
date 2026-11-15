# Requirements: Sessions Without a Current Tenant

## Context

A signed-in person can currently be in exactly two states: signed in with a current tenant (a GroundUp token carrying a `tid`), or signed out of the API. Two behaviors in the leave flow show the gap:

1. **Leaving your last tenant signs you out of the API** (cookie cleared, `{ "noTenants": true }`). Keycloak stays signed in. The person is left holding a Keycloak session and nothing else.
2. **Leaving the tenant you are currently in switches you to another tenant automatically.** `AuthController.LeaveTenant` takes `remainingMemberships.First()`, and the underlying query has no ordering, so with three or more memberships the tenant you land in is arbitrary.

Neither matches how the product should behave. The rule agreed with the project owner:

> If a user leaves the tenant they are currently logged in to, they are no longer logged in under a current tenant. If a user is logged in to one tenant and leaves a different tenant, they stay logged in under the tenant they were in.

A session with no current tenant is also the right home for other situations that today end in a dead end or an error: a user with zero memberships and no default tenant (403 today), and the multi-membership picker (a Keycloak token is parked in the auth cookie today).

This spec adds that state ("tenantless session") and applies the rule. It must land before the enterprise work, which builds on tenant selection.

### Why the token cannot simply be kept

Leaving does not revoke anything. The GroundUp token is a signed JWT that names the tenant (`tid`) and is valid until it expires (60 minutes by default). Membership is re-checked only when a token is refreshed and, for subdomain requests, by `HostTokenReconciliationMiddleware`. So after leaving, the old token would keep working for that tenant. The leave flow must replace it, not just stop using it.

## Scope

**In scope**
- Leaving the current tenant produces a tenantless session instead of an automatic switch or a sign-out.
- A tenantless session type: what it is, what it can call, how long it lasts.
- Login with zero active memberships and no default tenant produces a tenantless session instead of a 403.
- Letting a tenantless user create an organization.
- Retiring the `noTenants` leave response in favor of a response that works for all cases.

**Out of scope**
- Enterprise tenants and their separate Keycloak realms.
- CSRF protection and cookie hardening (separate spec).
- Invitations and join links (they will build on this state, but are not part of it).
- Token revocation (own spec: `phase-10c-token-revocation`). Tenantless tokens must be covered by its user-wide cutoff.

## Requirements

### Requirement 1: Leaving the current tenant ends the tenant selection

**User story:** As a user who leaves the tenant I am working in, I do not want to be moved into a different tenant I did not choose.

#### Acceptance Criteria

1. WHEN a user leaves the tenant their token is scoped to (`tid` equals the tenant being left) THEN the system SHALL replace their auth cookie with a tenantless token. It SHALL NOT select another tenant for them, regardless of how many other memberships they have.
2. WHEN a user leaves a tenant other than the one their token is scoped to THEN their session SHALL be unchanged (no new cookie).
3. The old tenant-scoped token SHALL NOT be written back to the cookie after a successful leave.
4. The `auth_time` of the tenantless token SHALL equal the `auth_time` of the token it replaces, so the absolute session lifetime still counts from the original sign-in.
5. The leave response SHALL tell the client whether the session still has a current tenant, in a form that works for every case (no tenant left, other tenants left, left a non-current tenant). The existing `noTenants` field SHALL be retired.
6. The last-admin rule is unchanged: a tenant must always keep at least one active TenantAdmin.

### Requirement 2: The tenantless session

**User story:** As a signed-in user with no current tenant, I can see who I am and what I belong to, and I can choose or create a tenant, but I cannot touch tenant data.

#### Acceptance Criteria

1. A tenantless session SHALL identify the user (user id, email, display name) and SHALL carry no `tid` and no roles.
2. Tenantless sessions SHALL be able to call:
   - `GET /auth/me`: returns the identity with `tenantId` null.
   - `GET /auth/me/tenants`: returns the user's active memberships (possibly empty), none marked `current`.
   - `POST /auth/set-tenant`: selects one of the user's memberships and returns a tenant-scoped token, preserving `auth_time`.
   - `POST /auth/organizations`: creates an organization (see Requirement 4).
   - `POST /auth/logout`.
3. Tenant-scoped endpoints SHALL reject a tenantless session with `403 Forbidden`. The caller is authenticated but has no tenant, so 401 would be wrong.
4. A tenantless session SHALL expire with the same limits as any other session: token lifetime, and the absolute session lifetime measured from `auth_time`.
5. A tenantless session SHALL NOT be refreshed into a tenant-scoped session without the user choosing a tenant through `set-tenant`.

### Requirement 3: Login with no tenant to enter

**User story:** As a user who has no memberships, I can still sign in and decide what to do next.

#### Acceptance Criteria

1. WHEN a user signs in with zero active memberships and a default tenant is configured THEN the existing behavior SHALL stand: the user is added to the default tenant (reusing a previous membership row if they left it earlier) and receives a token for it.
2. WHEN a user signs in with zero active memberships and **no** default tenant is configured THEN the system SHALL issue a tenantless session instead of returning `403 ACCESS_DENIED`.
3. WHEN a user signs in with two or more standard-tenant memberships THEN the existing picker response SHALL stand. The picker SHALL use the same tenantless GroundUp-issued token instead of parking a Keycloak token in the cookie (decided; see Decisions).
4. Enterprise tenants remain excluded from standard sign-in and from the picker.

### Requirement 4: A tenantless user can create an organization

**User story:** As someone with no tenants, I can start a new organization rather than being stuck.

#### Acceptance Criteria

1. `POST /auth/organizations` SHALL accept a tenantless session. This replaces Requirement 2.3 of the org-creation spec, which rejected sessions without a `tid`.
2. The response and the created rows SHALL be identical to the tenant-scoped case (201 with `{ id, name, slug }`; the caller becomes TenantAdmin).
3. The session SHALL remain tenantless after creation. The user enters the new organization with `POST /auth/set-tenant`.
4. An unauthenticated caller SHALL still receive 401.

### Requirement 5: Tests and manual verification

#### Acceptance Criteria

1. Integration tests SHALL cover: leaving the current tenant with zero, one and **three** other memberships (never an automatic switch); leaving a non-current tenant (session unchanged); `set-tenant` from a tenantless session; `POST /auth/organizations` from a tenantless session; tenant-scoped endpoints rejecting a tenantless session; login with zero memberships and no default tenant.
2. A tenantless token SHALL be rejected by tenant-scoped endpoints in an integration test, not only in unit tests.
3. MANUAL_TESTING.md SHALL be updated: Phase 7 (leave) and Phase 3 (login with no memberships), plus a new section for the tenantless session.

## Decisions

Agreed with the project owner:

1. **Tenantless token (was Q1):** a GroundUp-issued token without `tid`. The token service gets a new path for it.
2. **Picker (was Q2):** the multi-membership picker uses the same tenantless token.
3. **Roles on leave (was Q3):** leaving removes the user's role assignments in that tenant, so rejoining starts clean. Implemented in commit e4e2143 (`UserTenantRepository.DeactivateMembershipAsync`).
4. **No-membership login (was Q5):** confirmed. Zero memberships and no default tenant gives a tenantless session. Tenant-scoped endpoints answer a tenantless session with 403.

## Open Questions

None. The per-request membership question was resolved by splitting it into its own spec, `phase-10c-token-revocation` (cutoff list checked in memory, shorter token lifetime, ICache-backed store in Phase 15).

## Manual Re-Testing Required After Implementation

- Phase 7 (leave): leave the current tenant with other memberships and confirm you are left with no current tenant and are not moved.
- Phase 3 (login) with a user who has no memberships and no default tenant.
- A new tenantless-session section: `me`, `me/tenants`, `organizations`, `set-tenant`, `logout`.
