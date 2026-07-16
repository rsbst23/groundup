# Implementation Plan: Phase 10C — Auth Dispatcher, Host Resolver, Cookie Writer, Basic Flows

## Overview

This plan implements the first user-facing authentication slice for GroundUp: cookie writer, host-based tenant resolution, OAuth URL building with PKCE/state/nonce, callback flow dispatcher with strategy-pattern handlers (NewOrganization + Login), token refresh middleware, TenantAdmin bypass, host/token reconciliation, and the AuthController. Tasks are ordered by dependency chain and sized for small, reviewable PRs.

## Tasks

- [x] 1. Schema changes, enum rename, and configuration additions
  - [x] 1.1 Rename FlowType.MultiTenantSelection to FlowType.Login and add AuthRoleNames.TenantAdmin
    - Rename `MultiTenantSelection = 5` to `Login = 5` in `FlowType.cs`
    - Update all references across the codebase (validators, tests, documentation)
    - Add `public const string TenantAdmin = "TenantAdmin";` to `AuthRoleNames.cs`
    - _Requirements: 11.1, 11.2, 11.3, 10.1_

  - [x] 1.2 Extend AuthOptions with new configuration properties
    - Add `AbsoluteSessionLifetimeMinutes` (default 480), `FlowStateExpirationMinutes` (default 10), `StateCookieName` (default "AuthState"), `CallbackPath` (default "/auth/callback")
    - Add startup validation: `AbsoluteSessionLifetimeMinutes > 0` AND `>= TokenExpirationMinutes`
    - _Requirements: 9.12, 6.6, 5.5, 4.6_

  - [x] 1.3 Extend AuthFlowState entity with StateToken, CodeVerifier, RedirectUri, OrganizationName
    - Add properties to `AuthFlowState.cs` entity
    - Add EF Fluent API configuration: `StateToken` indexed + unique, max lengths for all new fields
    - Update `AuthFlowStateDto` record with corresponding fields
    - Update `InitiateAuthFlowRequest` record with new fields where applicable
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 16.6_

  - [x] 1.4 Add User.ExternalUserId unique index in UserConfiguration
    - Add `builder.HasIndex(e => e.ExternalUserId).IsUnique()` to `UserConfiguration.cs`
    - _Requirements: 16.6 (User unique index portion)_

  - [x] 1.5 Create EF Core migration for schema changes
    - Generate migration adding AuthFlowState columns (StateToken with unique index, CodeVerifier, RedirectUri, OrganizationName) and User.ExternalUserId unique index
    - Verify migration does NOT re-add the existing Tenant.Slug unique index
    - _Requirements: 16.6, 16.7_

  - [x] 1.6 Write unit tests for AuthOptions startup validation
    - Test that `AbsoluteSessionLifetimeMinutes < TokenExpirationMinutes` fails validation
    - Test that `AbsoluteSessionLifetimeMinutes = 0` fails validation
    - Test that valid combinations pass
    - _Requirements: 9.12_

- [x] 2. Checkpoint — Ensure schema changes compile and migration is valid
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Infrastructure: AuthCookieWriter
  - [x] 3.1 Implement AuthCookieWriter (IAuthCookieWriter)
    - Create `AuthCookieWriter.cs` in `GroundUp.Auth.Services`
    - Read `auth.application.default-domain` from `ISettingsService`
    - Derive Domain attribute: non-empty → `.{domain}`, else omit
    - WriteAuthCookie: set HttpOnly, Secure, SameSite, Path="/", Expires from AuthOptions.TokenExpirationMinutes
    - ClearAuthCookie: same name/domain/path, expiration in the past
    - Throw `ArgumentException` on null/empty/whitespace token
    - _Requirements: 1.1–1.12_

  - [x] 3.2 Write property tests for AuthCookieWriter
    - **Property 1: Cookie Domain Derivation** — verify domain derivation logic for arbitrary setting values
    - **Property 2: Cookie Configuration Propagation** — verify all AuthOptions map correctly to cookie attributes
    - **Property 3: Empty Token Rejection** — verify ArgumentException for null/empty/whitespace tokens
    - **Validates: Requirements 1.2, 1.3, 1.4–1.10, 1.11, 1.12**

  - [x] 3.3 Write unit tests for AuthCookieWriter
    - Test specific SameSite values (Strict, Lax, None)
    - Test domain omission when setting is whitespace-only
    - Test ClearAuthCookie produces same Domain as Write
    - _Requirements: 1.1–1.12_

- [x] 4. Infrastructure: HostTenantResolver and middleware
  - [x] 4.1 Create IHostTenantResolver interface and HostResolvedTenant scoped service
    - Create `IHostTenantResolver.cs` interface in `GroundUp.Auth.Services`
    - Create `HostResolvedTenant.cs` scoped service class
    - _Requirements: 2.8, 3.2_

  - [x] 4.2 Implement HostTenantResolver
    - Create `HostTenantResolver.cs` in `GroundUp.Auth.Services`
    - Read `auth.application.default-domain` from `ISettingsService`
    - Strip port from Host, extract single-label subdomain, case-insensitive comparison
    - Call `ITenantRepository.GetBySlugAsync`, return TenantDto if active, else null
    - Return null for multi-level subdomains, bare domain, IP, different domain, empty setting
    - _Requirements: 2.1–2.8_

  - [x] 4.3 Implement HostTenantResolutionMiddleware
    - Create middleware in `GroundUp.Auth.Api/Middleware/`
    - Invoke `IHostTenantResolver.ResolveAsync`, stash result in `HostResolvedTenant`
    - On exception: log Warning, treat as null, continue pipeline
    - Never short-circuits
    - _Requirements: 3.1–3.6_

  - [x] 4.4 Write property tests for HostTenantResolver
    - **Property 4: Host Subdomain Extraction** — verify extraction for valid patterns and null return for invalid patterns
    - **Validates: Requirements 2.2, 2.3, 2.6, 2.7**

  - [x] 4.5 Write unit tests for HostTenantResolver and middleware
    - Test repository returns not-found → null
    - Test repository returns inactive tenant → null
    - Test multi-level subdomain → null
    - Test middleware logs and continues on exception
    - _Requirements: 2.1–2.8, 3.1–3.6_

- [x] 5. Infrastructure: AuthUrlBuilderService
  - [x] 5.1 Create IAuthUrlBuilder interface and supporting DTOs
    - Create `IAuthUrlBuilder.cs` with `BuildAuthorizationUrlAsync` method
    - Create `AuthUrlRequest` and `AuthUrlResult` records
    - _Requirements: 4.1–4.12_

  - [x] 5.2 Implement AuthUrlBuilderService
    - Create `AuthUrlBuilderService.cs` in `GroundUp.Auth.Services`
    - Generate state token: 32+ bytes cryptographic randomness, Base64URL no padding
    - Generate nonce: 32+ bytes cryptographic randomness, Base64URL no padding
    - Generate PKCE code_verifier: 43–128 URL-safe chars, compute code_challenge = Base64URL(SHA256(verifier)) no padding
    - Build URL: `{PublicBaseUrl}/realms/{realm}/protocol/openid-connect/auth` with all query params
    - Realm routing: use host-resolved tenant's RealmName if non-null, else SharedRealmName
    - Return failure OperationResult if PublicBaseUrl or AppClientId is missing
    - _Requirements: 4.1–4.12_

  - [x] 5.3 Write property tests for AuthUrlBuilderService
    - **Property 5: Cryptographic Token Format** — state and nonce are valid Base64URL, decode to ≥32 bytes, no duplicates
    - **Property 6: PKCE Round-Trip Integrity** — verifier is 43–128 chars URL-safe, challenge = Base64URL(SHA256(verifier))
    - **Validates: Requirements 4.2, 4.4, 4.5**

  - [x] 5.4 Write unit tests for AuthUrlBuilderService
    - Test enterprise tenant realm routing (RealmName used)
    - Test shared realm fallback (no host tenant or null RealmName)
    - Test failure when PublicBaseUrl or AppClientId is null/whitespace
    - Test response_type=code, scope=openid email profile, client_id set
    - _Requirements: 4.1, 4.7, 4.8, 4.9, 4.10, 4.12_

- [x] 6. Checkpoint — Ensure infrastructure components compile and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Flow dispatcher: AuthFlowService and IFlowHandler interface
  - [x] 7.1 Create IFlowHandler interface and supporting DTOs
    - Create `IFlowHandler.cs` with `HandledFlowType` property and `HandleCallbackAsync` method
    - Create `FlowCallbackContext` record (AuthorizationCode, CodeVerifier, RedirectUri, ConsumedState, HostResolvedTenant)
    - Create `FlowResult` record with static factories (Success, TenantSelectionRequired, Error)
    - _Requirements: 5.1, 5.2_

  - [x] 7.2 Create IAuthFlowService interface and supporting DTOs
    - Create `IAuthFlowService.cs` with `InitiateFlowAsync` and `HandleCallbackAsync`
    - Create `FlowInitiationRequest` and `FlowInitiationResult` records
    - _Requirements: 5.3, 6.1–6.9_

  - [x] 7.3 Implement AuthFlowService — flow initiation
    - Create `AuthFlowService.cs` in `GroundUp.Auth.Services`
    - InitiateFlowAsync: create AuthFlowState with FlowType, state token, nonce, code_verifier, redirect_uri, expiration, client IP/user-agent
    - Store organization name when FlowType is NewOrganization
    - Store TenantId when host resolver found a tenant
    - Store realm override for enterprise tenants
    - Set HttpOnly state cookie (StateCookieName) with the state token
    - Call IAuthUrlBuilder, return redirect URL and FlowState ID
    - _Requirements: 6.1–6.9_

  - [x] 7.4 Implement AuthFlowService — callback dispatch
    - HandleCallbackAsync: validate state cookie binding (compare state param vs cookie value)
    - Look up AuthFlowState by StateToken (not by PK)
    - Consume the state via IAuthFlowStateService.ConsumeAsync
    - Handle already-consumed (replay) → 410 Gone
    - Handle expired → error
    - Resolve handler by FlowType from `IEnumerable<IFlowHandler>`
    - Handle no handler → fail with error
    - Pass code, code_verifier, redirect_uri, consumed state, host-resolved tenant to handler
    - _Requirements: 5.4–5.10_

  - [x] 7.5 Write property tests for AuthFlowService dispatch logic
    - **Property 7: State Cookie Binding** — callback succeeds iff state param matches cookie AND matches a Pending state
    - **Property 8: Replay Protection** — consuming an already-consumed state returns 410
    - **Validates: Requirements 5.5, 5.8**

  - [x] 7.6 Write unit tests for AuthFlowService
    - Test initiation creates state with all fields populated
    - Test callback rejects missing state cookie
    - Test callback rejects mismatched state cookie
    - Test callback routes to correct handler by FlowType
    - Test callback returns error when no handler registered
    - Test expired flow state is rejected
    - _Requirements: 5.4–5.10, 6.1–6.9_

- [x] 8. Flow handlers: NewOrganizationFlowHandler
  - [x] 8.1 Implement slug generation utility
    - Create slug derivation from organization name: lowercase alphanumeric + hyphens, no leading/trailing hyphens
    - Implement disambiguate logic: append numeric suffix on collision
    - _Requirements: 7.4, 7.5_

  - [x] 8.2 Write property tests for slug generation
    - **Property 10: Slug Generation Validity and Disambiguation** — derived slug is valid format, disambiguated slug differs and remains valid
    - **Validates: Requirements 7.4, 7.5**

  - [x] 8.3 Implement NewOrganizationFlowHandler
    - Create `NewOrganizationFlowHandler.cs` in `GroundUp.Auth.Services`
    - Exchange code for tokens via IIdentityProviderService.ExchangeCodeForTokensAsync (code_verifier + redirect_uri)
    - Validate id_token nonce against stored nonce → fail on mismatch
    - Retrieve userinfo via IIdentityProviderService.GetUserInfoAsync
    - Derive slug from AuthFlowState.OrganizationName
    - Retry loop wrapping entire transaction on slug unique-constraint violation
    - Inside transaction: create Tenant (Standard), resolve/create User by ExternalUserId (sub), create UserTenant membership, create TenantAdmin role (tenant-scoped, IsSystem=true), assign TenantAdmin to founding user
    - Handle user unique-constraint violation: re-read existing user by ExternalUserId
    - Generate token with auth_time = now via additionalClaims
    - Write auth cookie
    - _Requirements: 7.1–7.13_

  - [x] 8.4 Write property tests for NewOrganizationFlowHandler
    - **Property 9: OIDC Nonce Validation** — nonce validation passes iff id_token nonce equals stored nonce
    - **Validates: Requirements 7.2**

  - [x] 8.5 Write unit tests for NewOrganizationFlowHandler
    - Test code exchange failure → flow marked failed
    - Test nonce mismatch → flow marked failed
    - Test userinfo failure → flow marked failed
    - Test successful flow creates all entities atomically
    - Test slug collision triggers retry with disambiguated slug
    - Test user ExternalUserId collision re-reads existing user
    - _Requirements: 7.1–7.13_

- [x] 9. Flow handlers: LoginFlowHandler
  - [x] 9.1 Implement LoginFlowHandler
    - Create `LoginFlowHandler.cs` in `GroundUp.Auth.Services`
    - Exchange code, validate nonce, get userinfo (same pattern as NewOrg)
    - Resolve/create user by ExternalUserId (sub)
    - Query active memberships via IUserTenantRepository.GetAllMembershipsForUserAsync
    - Zero memberships + default tenant configured → auto-join (create membership, assign default role from cascade, issue token with auth_time)
    - Zero memberships + no default tenant → access-denied
    - One membership + no host-pin → auto-select, issue token with auth_time, write cookie
    - Two+ memberships + no host-pin → return TenantSelectionRequired (exclude enterprise tenants with RealmName ≠ null), write Keycloak token as auth cookie (no GroundUp token)
    - Host-pinned + member → issue token with auth_time, write cookie
    - Host-pinned + non-member → access-denied
    - Enterprise host-resolved tenant → return "not yet implemented" result
    - _Requirements: 8.1–8.16_

  - [x] 9.2 Write property tests for LoginFlowHandler
    - **Property 13: Tenant Picker Excludes Enterprise Tenants** — picker list contains only tenants with RealmName = null
    - **Property 14: Pending-Selection Authentication** — no GroundUp token issued during selection, principal has sub but no tid
    - **Validates: Requirements 8.9, 8.10**

  - [x] 9.3 Write unit tests for LoginFlowHandler
    - Test auto-join when default tenant configured
    - Test access-denied when zero memberships and no default tenant
    - Test auto-select for single membership
    - Test multi-membership returns tenant list excluding enterprise tenants
    - Test host-pin with member → token issued
    - Test host-pin with non-member → access-denied
    - Test enterprise tenant → not-yet-implemented
    - Test Keycloak token retained (not GroundUp token) during pending-selection
    - _Requirements: 8.1–8.16_

- [x] 10. Checkpoint — Ensure flow handlers compile and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Token refresh and auth_time threading
  - [x] 11.1 Extend IAuthSessionService and AuthSessionService signatures for auth_time
    - Add `DateTimeOffset originalAuthTime` parameter to `RefreshTokenAsync`
    - Add optional `DateTimeOffset? originalAuthTime = null` parameter to `SetTenantAsync`
    - In RefreshTokenAsync: pass auth_time as additional claim to GenerateTokenAsync (preserve original value)
    - In SetTenantAsync: when originalAuthTime is provided, preserve it; when null (first issuance from Keycloak principal), set auth_time = now
    - Add logic to resolve GroundUp user by external sub claim (via IUserRepository.GetByExternalUserIdAsync) for set-tenant from pending-selection principal
    - _Requirements: 9.7, 9.8, 9.9, 12.8_

  - [x] 11.2 Implement TokenRefreshMiddleware
    - Create `TokenRefreshMiddleware.cs` in `GroundUp.Auth.Api/Middleware/`
    - Skip when not authenticated or when token has no `tid` claim (pending-selection / tenant-less)
    - Read `iat` claim → compute token age
    - If age < 50% of TokenExpirationMinutes → skip
    - Read `auth_time` claim → compute elapsed since original auth
    - If elapsed >= AbsoluteSessionLifetimeMinutes → skip (session expired, user must re-auth)
    - Call IAuthSessionService.RefreshTokenAsync(userId, tenantId, authTime)
    - On success: rewrite cookie via IAuthCookieWriter
    - On failure (membership revoked): clear cookie, continue pipeline
    - Never blocks the request — best-effort
    - _Requirements: 9.1–9.11, 13.1–13.8_

  - [x] 11.3 Write property tests for token refresh decision logic
    - **Property 11: auth_time Claim Lifecycle** — initial tokens have auth_time = now; refreshed tokens preserve original auth_time
    - **Property 12: Sliding Refresh Decision Function** — refresh decisions based on tid presence, token age, and absolute cap
    - **Validates: Requirements 9.4, 9.5, 9.6, 9.7, 9.8, 13.8**

  - [x] 11.4 Write unit tests for TokenRefreshMiddleware
    - Test skip when unauthenticated
    - Test skip when no tid claim (pending-selection)
    - Test skip when token age < 50%
    - Test skip when absolute cap reached
    - Test refresh triggered and cookie rewritten when eligible
    - Test cookie cleared when membership revoked
    - _Requirements: 9.1–9.11, 13.1–13.8_

- [x] 12. TenantAdmin bypass in PermissionService
  - [x] 12.1 Add TenantAdmin bypass to PermissionService
    - Add `IsTenantAdminInCurrentTenantAsync` private method using `GetByUserIdForTenantAsync` (tenant-scoped query)
    - Insert short-circuit BEFORE cache lookup in `HasPermissionAsync` and `HasAnyPermissionAsync`
    - Never cache the bypass as a concrete permission set
    - Bypass uses `ITenantContext.TenantId` — never leaks cross-tenant
    - _Requirements: 10.4, 10.5, 10.6, 10.7_

  - [x] 12.2 Write property tests for TenantAdmin bypass
    - **Property 15: TenantAdmin Permission Bypass (Tenant-Scoped)** — any permission returns true when user holds TenantAdmin in current tenant
    - **Property 16: TenantAdmin Is Not a System Role** — GetSystemRolesForUserAsync never returns TenantAdmin
    - **Validates: Requirements 10.3, 10.4, 10.7**

  - [x] 12.3 Write unit tests for TenantAdmin bypass
    - Test bypass fires for TenantAdmin in current tenant
    - Test bypass does NOT fire for TenantAdmin in different tenant
    - Test bypass does NOT fire for non-TenantAdmin roles
    - Test bypass short-circuits before cache
    - _Requirements: 10.4–10.7_

  - [x] 12.4 Implement last-admin guard in role-assignment service
    - In the role-assignment service layer, before removing TenantAdmin: count active TenantAdmin holders in the tenant
    - If removing the last one → reject with descriptive error (LAST_ADMIN, 409)
    - Guard lives in service layer only (not repository or controller)
    - _Requirements: 10.8, 10.9_

  - [x] 12.5 Write property tests for last-admin guard
    - **Property 17: Last Admin Guard** — removal rejected when exactly one active TenantAdmin; succeeds when two or more
    - **Validates: Requirements 10.8**

- [x] 13. Checkpoint — Ensure token refresh and TenantAdmin bypass compile and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 14. HostTokenReconciliationMiddleware
  - [x] 14.1 Implement HostTokenReconciliationMiddleware
    - Create `HostTokenReconciliationMiddleware.cs` in `GroundUp.Auth.Api/Middleware/`
    - Runs after JwtTenantResolutionMiddleware (reads both HostResolvedTenant and TenantContext)
    - Exempt `/auth/*` paths and tenant-selection UI path — never deny these
    - When HostResolvedTenant is null or matches token tid → proceed normally
    - When mismatch + standard tenant (RealmName null) + user is member → deny with TENANT_SWITCH_REQUIRED (409)
    - When mismatch + enterprise tenant (RealmName set) → deny with REAUTH_REQUIRED (401)
    - When mismatch + user not a member → deny with Forbidden (403)
    - When unauthenticated → proceed (no token to compare)
    - _Requirements: 18.1–18.5_

  - [x] 14.2 Write property tests for HostTokenReconciliationMiddleware
    - **Property 18: Host/Token Mismatch Denial** — denied for data paths on mismatch; exempt for /auth/* paths
    - **Validates: Requirements 18.1**

  - [x] 14.3 Write unit tests for HostTokenReconciliationMiddleware
    - Test match → proceed
    - Test no host tenant → proceed
    - Test unauthenticated → proceed
    - Test standard mismatch with member → 409
    - Test enterprise mismatch → 401
    - Test non-member mismatch → 403
    - Test /auth/* path exemption → proceed despite mismatch
    - _Requirements: 18.1–18.5_

- [x] 15. AuthController
  - [x] 15.1 Implement AuthController
    - Create `AuthController.cs` in `GroundUp.Auth.Api/Controllers/`
    - Route: `[Route("auth")]`
    - GET /auth/login → InitiateFlowAsync(Login) → 302 redirect
    - GET /auth/register → validate org name (required, max length) → InitiateFlowAsync(NewOrganization) → 302 redirect
    - GET /auth/callback → HandleCallbackAsync(code, state) → redirect on success, JSON on picker, error status on failure
    - GET /auth/me → return claims from HttpContext.User (userId, email, displayName, tenantId, roles); 401 if unauthenticated; 200 with null tenant for pending-selection principal
    - POST /auth/set-tenant → resolve user (by sub for Keycloak principal, by userId for GroundUp principal) → SetTenantAsync → write cookie → 200
    - POST /auth/refresh → RefreshTokenAsync → write cookie → 200; 403 on revoked membership
    - POST /auth/logout → ClearAuthCookie + Keycloak end_session (client_id + post_logout_redirect_uri, shared realm) → 200
    - ZERO business logic — all delegation to service interfaces
    - Provision antiforgery token on callback success and picker response
    - _Requirements: 12.1–12.14_

  - [x] 15.2 Write unit tests for AuthController
    - Test GET /auth/login returns 302
    - Test GET /auth/register requires org name
    - Test GET /auth/callback routes correctly
    - Test GET /auth/me returns 401 when unauthenticated
    - Test GET /auth/me returns identity with null tenant for pending-selection
    - Test POST /auth/set-tenant resolves user by sub for Keycloak principal
    - Test POST /auth/logout clears cookie and calls end_session
    - _Requirements: 12.1–12.14_

- [x] 16. DI registration and pipeline wiring
  - [x] 16.1 Update AuthServiceCollectionExtensions
    - Register `IAuthCookieWriter` → `AuthCookieWriter`
    - Register `IHostTenantResolver` → `HostTenantResolver`
    - Register `HostResolvedTenant` as scoped
    - Register `IAuthUrlBuilder` → `AuthUrlBuilderService`
    - Register `IAuthFlowService` → `AuthFlowService`
    - Register `IFlowHandler` → `NewOrganizationFlowHandler` (scoped)
    - Register `IFlowHandler` → `LoginFlowHandler` (scoped)
    - _Requirements: 17.1–17.4_

  - [x] 16.2 Update UseGroundUpAuth middleware pipeline
    - Add `HostTenantResolutionMiddleware` BEFORE JwtAuthenticationMiddleware
    - Add `TokenRefreshMiddleware` AFTER JwtAuthenticationMiddleware
    - Add `HostTokenReconciliationMiddleware` AFTER JwtTenantResolutionMiddleware, BEFORE CsrfProtectionMiddleware
    - Final order: HostTenantResolution → JwtAuth → TokenRefresh → JwtTenantResolution → HostTokenReconciliation → Csrf
    - _Requirements: 17.5, 17.6_

- [x] 17. Checkpoint — Ensure AuthController and DI wiring compile and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 18. Sample app wiring
  - [x] 18.1 Update GroundUp.Sample Program.cs and configuration
    - Ensure `AddGroundUpAuth` and `UseGroundUpAuth` are called with the updated pipeline
    - Configure a test domain in `appsettings.Development.json` for host resolution and cookie domain
    - Verify the sample app compiles and starts with the new middleware stack
    - _Requirements: 14.1, 14.2, 14.3_

- [x] 19. Integration tests
  - [x] 19.1 Set up integration test infrastructure for auth flows
    - Configure Testcontainers Keycloak (same version as docker-compose.yml) and Postgres
    - Set up WebApplicationFactory with TimeProvider injection for controllable time
    - Create shared test fixtures for auth flow testing
    - _Requirements: 15.1_

  - [x] 19.2 Write integration tests for NewOrganization flow
    - Verify end-to-end: initiate → redirect → callback → tenant created, user matched by sub, TenantAdmin assigned, atomicity, cookie written
    - Verify slug collision handling with concurrent registrations
    - _Requirements: 15.2_

  - [x] 19.3 Write integration tests for Login flow
    - Verify single-membership auto-select (token issued, cookie written)
    - Verify multi-membership picker (tenant list excludes enterprise tenants)
    - Verify host-pinned login (membership validated for specific tenant)
    - Verify enterprise routing stub returns "not yet implemented"
    - _Requirements: 15.3, 15.4, 15.5, 15.6_

  - [x] 19.4 Write integration tests for security controls
    - Verify cross-subdomain cookie domain attribute
    - Verify replay attack → 410 Gone
    - Verify state cookie binding rejection
    - Verify nonce mismatch rejection
    - _Requirements: 15.7, 15.8, 15.9, 15.10_

  - [x] 19.5 Write integration tests for token refresh and session lifetime
    - Verify sliding refresh: advance time past 50%, trigger refresh, new cookie written
    - Verify absolute session cap: auth_time past cap → no refresh, re-auth required
    - _Requirements: 15.11, 15.12_

  - [x] 19.6 Write integration tests for TenantAdmin and last-admin guard
    - Verify TenantAdmin bypass: true for any permission in own tenant, does not bypass in different tenant
    - Verify last-admin guard: removal of sole TenantAdmin is rejected
    - _Requirements: 15.13, 15.14_

  - [x] 19.7 Write integration tests for pending-selection and set-tenant flows
    - Verify pending-selection: retained Keycloak token (no GroundUp token, no tid), GET /auth/me succeeds with null tenant
    - Verify POST /auth/set-tenant resolves user by external sub, issues first full token, writes cookie
    - _Requirements: 15.15_

  - [x] 19.8 Write integration tests for host/token reconciliation
    - Verify standard tenant mismatch: user can switch via set-tenant without re-auth
    - Verify enterprise tenant mismatch: requires fresh login to enterprise realm
    - Verify non-member mismatch: access denied
    - Verify /auth/* paths are exempt from reconciliation denial
    - _Requirements: 15.16, 15.17, 15.18_

- [x] 20. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (18 properties)
- Unit tests validate specific examples and edge cases
- Integration tests validate end-to-end flows against real infrastructure (Testcontainers Keycloak + Postgres)
- The slug retry loop MUST wrap the entire transaction (Postgres aborts tx on unique-constraint violation)
- Pending-selection uses the retained Keycloak token — no interim GroundUp token
- auth_time threading requires extending IAuthSessionService signatures (optional DateTimeOffset parameter)
- TokenRefreshMiddleware skips tokens with no `tid` claim
- HostTokenReconciliationMiddleware MUST exempt `/auth/*` paths
- Logout uses client_id + post_logout_redirect_uri (no id_token storage in 10c)
- TenantAdmin is tenant-scoped (NOT RoleType=System), uses IsSystem=true, bypass in PermissionService short-circuits BEFORE cache

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "1.4"] },
    { "id": 2, "tasks": ["1.5", "1.6"] },
    { "id": 3, "tasks": ["3.1", "4.1", "5.1"] },
    { "id": 4, "tasks": ["3.2", "3.3", "4.2", "5.2"] },
    { "id": 5, "tasks": ["4.3", "4.4", "4.5", "5.3", "5.4"] },
    { "id": 6, "tasks": ["7.1", "7.2"] },
    { "id": 7, "tasks": ["7.3", "7.4"] },
    { "id": 8, "tasks": ["7.5", "7.6", "8.1"] },
    { "id": 9, "tasks": ["8.2", "8.3"] },
    { "id": 10, "tasks": ["8.4", "8.5", "9.1"] },
    { "id": 11, "tasks": ["9.2", "9.3"] },
    { "id": 12, "tasks": ["11.1", "12.1"] },
    { "id": 13, "tasks": ["11.2", "11.3", "11.4", "12.2", "12.3", "12.4"] },
    { "id": 14, "tasks": ["12.5", "14.1"] },
    { "id": 15, "tasks": ["14.2", "14.3", "15.1"] },
    { "id": 16, "tasks": ["15.2", "16.1", "16.2"] },
    { "id": 17, "tasks": ["18.1"] },
    { "id": 18, "tasks": ["19.1"] },
    { "id": 19, "tasks": ["19.2", "19.3"] },
    { "id": 20, "tasks": ["19.4", "19.5"] },
    { "id": 21, "tasks": ["19.6", "19.7"] },
    { "id": 22, "tasks": ["19.8"] }
  ]
}
```
