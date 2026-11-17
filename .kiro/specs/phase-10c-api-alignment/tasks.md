# Implementation Plan: Phase 10C API Alignment — Standard Flow Changes

## Overview

Aligns the auth API endpoints with the auth-flows-design.md document. Key changes: AuthController returns JSON (not 302 redirects), rename `/auth/register` to `/auth/signup`, add `kc_action=register` for sign-up flows, add SimpleSignup flow handler, add new endpoints (`/auth/me/tenants`, `/auth/tenants/{id}/leave`), and fix Keycloak realm configuration.

Reference document: `.kiro/specs/phase-10-cleanup/auth-flows-design.md`

## Tasks

- [x] 1. AuthController: Convert 302 redirects to JSON responses
  - [x] 1.1 Change `GET /auth/login` to `POST /auth/login` returning `200 { redirectUrl }` instead of 302
    - Accept POST with optional JSON body (for future parameters)
    - Return `{ "redirectUrl": "https://keycloak/..." }` as JSON
    - Remove the 302 Redirect response
    - Keep the state cookie setting behavior (still needs HttpOnly cookie for CSRF binding)
  - [x] 1.2 Change `GET /auth/register` to `POST /auth/signup` returning `200 { redirectUrl }`
    - Rename endpoint from "register" to "signup"
    - Accept `{ "organizationName": "..." }` in the request body (optional — null means simple signup)
    - Return `{ "redirectUrl": "https://keycloak/..." }` as JSON
    - Remove the 302 Redirect response
  - [x] 1.3 Change `GET /auth/callback` to return JSON instead of 302 redirect on success
    - On success (single tenant): return `200 { token, tenant, user }` + Set-Cookie
    - On success (picker needed): return `200 { requiresTenantSelection: true, tenants: [...], user }`
    - On error: return appropriate error status + JSON body
    - Keep the callback as GET (Keycloak redirects here with query params)
  - [x] 1.4 Update AuthController unit tests for new response format
    - Update all existing tests to expect JSON responses instead of 302
    - Add tests for the POST method change on login/signup

- [x] 2. Add `kc_action=register` to authorize URL for sign-up flows
  - [x] 2.1 Extend AuthUrlBuilderService to accept a `isSignup` flag
    - When `isSignup = true`, append `&kc_action=register` to the authorize URL
    - This ensures users land on Keycloak's registration form directly
  - [x] 2.2 Pass `isSignup = true` from AuthFlowService when FlowType is NewOrganization or SimpleSignup
  - [x] 2.3 Update AuthUrlBuilder unit tests and property tests

- [x] 3. Add SimpleSignup flow type and handler
  - [x] 3.1 Add `SimpleSignup = 7` to FlowType enum
  - [x] 3.2 Create SimpleSignupFlowHandler
    - Implements IFlowHandler with HandledFlowType = FlowType.SimpleSignup
    - Exchange code for tokens (same as other handlers)
    - Validate nonce
    - Get userinfo
    - Find/Create User by sub
    - Read `auth.application.default-tenant-slug` setting
    - If no default tenant configured → return error
    - Lookup default tenant by slug
    - Read `auth.application.default-role` (cascading)
    - Create UserTenant membership (if not already a member)
    - Assign default role (if configured and not already assigned)
    - Generate token scoped to default tenant
    - Write auth cookie
  - [x] 3.3 Register SimpleSignupFlowHandler in DI
  - [x] 3.4 Update AuthFlowService to route SimpleSignup vs NewOrganization
    - When `POST /auth/signup` has `organizationName` → FlowType.NewOrganization
    - When `POST /auth/signup` has no organizationName → FlowType.SimpleSignup
  - [x] 3.5 Write unit tests for SimpleSignupFlowHandler
  - [x] 3.6 Write property tests for SimpleSignup logic
  - [x] 3.7 Seed `auth.application.default-tenant-slug` setting definition in DefaultAuthSettingsSeeder

- [x] 4. Add `GET /auth/me/tenants` endpoint
  - [x] 4.1 Add endpoint to AuthController
    - Requires authentication (returns 401 if not authenticated)
    - Returns list of user's active tenant memberships
    - For standard tenants: `{ id, name, slug, type: "standard", switchable: true, current: bool }`
    - For enterprise tenants: `{ id, name, slug, type: "enterprise", switchable: false, subdomain }`
    - Enterprise tenants marked as `switchable: false` (requires re-auth via subdomain)
  - [x] 4.2 Write unit tests for the endpoint

- [x] 5. Add `POST /auth/tenants/{id}/leave` endpoint
  - [x] 5.1 Add endpoint to AuthController
    - Requires authentication
    - Validates user has membership in the specified tenant
    - Checks last-admin guard (cannot leave if sole TenantAdmin)
    - Deactivates UserTenant membership (soft delete: IsActive = false)
    - If user was viewing the left tenant: generate token for next available tenant
    - If no remaining memberships: clear cookie, return `{ noTenants: true }`
  - [x] 5.2 Write unit tests for the endpoint

- [x] 6. Fix Keycloak realm.json for development
  - [x] 6.1 Enable self-registration in realm.json
    - Set `"registrationAllowed": true`
    - Set `"loginWithEmailAllowed": true` (already true)
  - [x] 6.2 Ensure http redirect URIs are included (already done in previous commit — verify)
  - [x] 6.3 Add `auth.application.default-tenant-slug` and `auth.application.signup-creates-org` settings to DefaultAuthSettingsSeeder

- [x] 7. Checkpoint — Build and run all unit tests
  - Ensure all tests pass after changes

- [x] 8. Update integration tests
  - [x] 8.1 Update NewOrganizationFlowTests for new API format (POST + JSON responses)
  - [x] 8.2 Update LoginFlowTests for new API format
  - [~] 8.3 Add integration test for SimpleSignup flow
  - [~] 8.4 Add integration test for /auth/me/tenants
  - [~] 8.5 Add integration test for /auth/tenants/{id}/leave

- [x] 9. Final checkpoint — All tests pass

## Notes

- The callback endpoint stays as GET (Keycloak redirects to it with query params)
- The state cookie is still set by the API (HttpOnly, short-lived) for CSRF binding
- POST /auth/logout stays the same (already returns JSON)
- POST /auth/set-tenant stays the same (already returns JSON)
- POST /auth/refresh stays the same (already returns JSON)
- GET /auth/me stays the same (already returns JSON)
- The `signup-creates-org` setting is a consuming-app configuration — GroundUp determines behavior based on whether `organizationName` is provided in the request body

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "2.1", "3.1"] },
    { "id": 1, "tasks": ["1.4", "2.2", "2.3", "3.2", "3.3", "3.4", "3.7"] },
    { "id": 2, "tasks": ["3.5", "3.6", "4.1", "5.1", "6.1", "6.2", "6.3"] },
    { "id": 3, "tasks": ["4.2", "5.2"] },
    { "id": 4, "tasks": ["7"] },
    { "id": 5, "tasks": ["8.1", "8.2", "8.3", "8.4", "8.5"] },
    { "id": 6, "tasks": ["9"] }
  ]
}
```
