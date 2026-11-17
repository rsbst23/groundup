# Requirements: Authenticated Organization Creation

## Context

Organization creation is currently conflated with identity registration. The `/auth/signup` endpoint with an `organizationName` body triggers the `NewOrganization` flow, which always routes through Keycloak with `kc_action=register`. This breaks for two legitimate scenarios:

1. **A logged-in user wants to create a second organization.** They already have a valid session, but the flow sends them back to Keycloak with `kc_action=register`. Keycloak cannot run a registration action against an already-authenticated SSO session, producing: *"Unexpected error when handling authentication request to identity provider."*
2. **A registered-but-logged-out user wants to create another organization.** `kc_action=register` is wrong for them too — Keycloak already knows their identity.

The root cause is that two distinct concerns are merged into one flow:

- **Authentication** — proving the human's identity at Keycloak. Only needs a Keycloak round-trip when there is no valid session.
- **Organization provisioning** — creating a Tenant, a membership, and a TenantAdmin role. Pure GroundUp domain logic; needs no identity provider.

This spec separates the two by introducing a first-class authenticated organization-creation endpoint and extracting the shared provisioning logic into a reusable service.

## Scope

**In scope:**
- Extract the tenant + membership + TenantAdmin-role provisioning logic out of `NewOrganizationFlowHandler` into a reusable service.
- Add `POST /auth/organizations` — an authenticated endpoint that creates a standard organization for the current user without a Keycloak round-trip.
- Keep the existing one-shot `NewOrganization` signup flow working for brand-new users (it will call the extracted service).

**Out of scope (future work):**
- Enterprise organization creation (dedicated realm + client provisioning). That is a separate platform-admin flow (`EnterpriseFirstAdmin`), documented in `auth-flows-design.md` scenarios 15–16, and remains unimplemented.
- A setting to control whether a newly created org becomes the active tenant automatically.
- Changing the `kc_action=register` behavior of the signup flow for genuinely new users (that case is correct as-is).

## Requirements

### Requirement 1: Reusable organization provisioning service

**User story:** As a framework maintainer, I want the organization-provisioning logic in one reusable place, so that both the signup callback and the authenticated org-creation endpoint produce identical results.

#### Acceptance Criteria

1. WHEN the tenant + membership + TenantAdmin-role creation logic is extracted into a service THEN `NewOrganizationFlowHandler` SHALL call that service instead of containing the logic inline.
2. The service SHALL create, in a single transaction: a `Tenant` (`TenantType.Standard`, `OnboardingMode.InviteOnly`, `RealmName = null`), a `UserTenant` membership for the founding user, a tenant-scoped `TenantAdmin` role (`IsSystem = true`), and a `UserRole` assigning that role to the user.
3. The service SHALL derive the tenant slug from the organization name and SHALL retry with a disambiguated slug on slug collision, up to the existing retry limit.
4. WHEN slug disambiguation is exhausted THEN the service SHALL return a failure result with a `SLUG_COLLISION` error code (no exception propagated to the caller).
5. The service SHALL accept an already-resolved user identifier and external user id; it SHALL NOT perform identity-provider calls or user creation. (User resolution/creation stays with the caller that has the identity context.)
6. The service SHALL return the created tenant's identifier on success.
7. The extraction SHALL preserve the existing behavior of the `NewOrganization` signup flow — all existing unit and integration tests for that flow SHALL continue to pass. One deliberate exception (decided during implementation): when slug disambiguation is exhausted, the callback now returns HTTP **409** (was 400), matching `POST /auth/organizations`.
8. Slug disambiguation SHALL actually work: a retry on the same request after a rejected insert SHALL succeed. (Found during implementation: `BaseRepository` left rejected entities tracked, so every retry failed and slug collisions always ended in `SLUG_COLLISION`. Fixed in the core repositories — see `tasks.md` 2.7.)

### Requirement 2: Authenticated organization-creation endpoint

**User story:** As a logged-in user, I want to create an additional organization without being sent back to the identity provider, so that creating an org is a seamless in-app action.

#### Acceptance Criteria

1. The endpoint SHALL be `POST /auth/organizations` and SHALL require an authenticated GroundUp session (a token with a `tid` claim).
2. WHEN the caller is unauthenticated THEN the endpoint SHALL return HTTP 401.
3. WHEN the caller is a pending-selection principal (Keycloak token, no `tid`) THEN the endpoint SHALL return HTTP 401 — org creation requires a fully established session. (Rationale: the request carries no selected tenant context; the user should complete tenant selection first.)
4. The request body SHALL contain an `organizationName` (required, non-empty, max 200 characters).
5. WHEN `organizationName` is missing, empty, or exceeds 200 characters THEN the endpoint SHALL return HTTP 400.
6. WHEN the request is valid THEN the endpoint SHALL resolve the current user from the session, call the provisioning service, and return HTTP 201 with the created organization's details (at minimum `id`, `name`, `slug`).
7. The current user SHALL become a member of the new organization with the `TenantAdmin` role.
8. The caller's session token SHALL remain scoped to their current tenant — creating a new organization SHALL NOT change the active `tid`. The user switches to the new org via `POST /auth/set-tenant`.
9. WHEN provisioning fails due to slug collision exhaustion THEN the endpoint SHALL return an appropriate error status with the `SLUG_COLLISION` error code.
10. The endpoint SHALL NOT make any identity-provider (Keycloak) round-trip.

### Requirement 3: No behavioral regression to existing flows

**User story:** As a user of the existing signup and login flows, I want them to behave exactly as before, so that the refactor introduces no regressions.

#### Acceptance Criteria

1. The `POST /auth/signup { organizationName }` flow for brand-new users SHALL continue to register at Keycloak and create the organization on callback, exactly as today.
2. The `POST /auth/signup` (no org name) SimpleSignup flow SHALL be unchanged.
3. The `POST /auth/login` flow SHALL be unchanged.
4. All existing auth unit and integration tests SHALL pass after the change.

## Open Questions

None outstanding — design decisions confirmed:
- Keep the one-shot NewOrganization signup for brand-new users.
- New org does not become the active tenant; user switches via `set-tenant`.
- Enterprise org creation is explicitly out of scope.

## Manual Re-Testing Required After Implementation

Because this refactors the verified `NewOrganization` signup flow and adds a new endpoint, the following manual tests should be re-run:
- Phase 2 (New Organization Signup) — confirm brand-new user + org creation still works end-to-end.
- Phase 4 (multi-membership) — now uses `POST /auth/organizations` to create the second org instead of a second Keycloak round-trip.
