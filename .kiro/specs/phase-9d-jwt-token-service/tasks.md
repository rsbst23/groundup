# Implementation Plan: Phase 9D — JWT Token Service & Auth Session

## Overview

This plan implements the JWT token lifecycle (generate, validate, refresh), auth session management (tenant selection), authentication middleware, CSRF protection, and identity provider interface stubs. The implementation is broken into 6 task groups that each compile independently and can be reviewed incrementally.

## Tasks

- [x] 1. DTOs and AuthOptions extensions
  - [x] 1.1 Create SigningKeyInfo record in GroundUp.Auth.Core/Dtos
    - `record SigningKeyInfo(string KeyId, byte[] KeyMaterial, string Algorithm = "HS256")`
    - _Requirements: 13.9, 16.7_
  - [x] 1.2 Create SetTenantRequestDto record in GroundUp.Auth.Core/Dtos
    - `record SetTenantRequestDto(Guid? TenantId)`
    - _Requirements: 16.1_
  - [x] 1.3 Create SetTenantResponseDto record in GroundUp.Auth.Core/Dtos
    - `record SetTenantResponseDto(bool SelectionRequired, List<TenantListItemDto>? AvailableTenants, string? Token)`
    - _Requirements: 16.2_
  - [x] 1.4 Create TenantListItemDto record in GroundUp.Auth.Core/Dtos
    - `record TenantListItemDto(Guid Id, string Name, string? Description)`
    - _Requirements: 16.3_
  - [x] 1.5 Create TokenResponseDto record in GroundUp.Auth.Core/Dtos
    - `record TokenResponseDto(string AccessToken, string? RefreshToken, int ExpiresIn, string? IdToken)`
    - _Requirements: 16.4_
  - [x] 1.6 Create ExternalUserInfo record in GroundUp.Auth.Core/Dtos
    - `record ExternalUserInfo(string ExternalUserId, string Email, string? DisplayName, IDictionary<string, string>? Attributes)`
    - _Requirements: 16.5_
  - [x] 1.7 Extend AuthOptions with JWT and cookie properties
    - Add to existing `GroundUp.Auth.Services/Configuration/AuthOptions.cs`: JwtSigningKey, Issuer, Audience, TokenExpirationMinutes, CookieName, CookieSecure, CookieSameSite
    - All properties bindable from "GroundUp:Auth" configuration section
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8_

- [x] 2. Interfaces — ISigningKeyProvider, ITokenService, IAuthSessionService
  - [x] 2.1 Create ISigningKeyProvider interface in GroundUp.Auth.Services
    - `GetSigningKeyAsync(Guid tenantId)` → `Task<SigningKeyInfo>`
    - `GetValidationKeyAsync(string kid)` → `Task<SigningKeyInfo?>`
    - Internal infrastructure, never exposed via API
    - _Requirements: 13.1, 13.2, 13.7_
  - [x] 2.2 Create ITokenService interface in GroundUp.Auth.Services
    - `GenerateTokenAsync(Guid userId, Guid tenantId, IEnumerable<Claim>? additionalClaims = null)` → `Task<string?>`
    - `ValidateTokenAsync(string token)` → `Task<ClaimsPrincipal?>`
    - _Requirements: 19.1, 19.2, 19.3_
  - [x] 2.3 Create IAuthSessionService interface in GroundUp.Auth.Services
    - `SetTenantAsync(Guid userId, Guid? tenantId)` → `Task<OperationResult<SetTenantResponseDto>>`
    - `RefreshTokenAsync(Guid userId, Guid tenantId)` → `Task<OperationResult<string>>`
    - _Requirements: 20.1, 20.2, 20.3_
  - [x] 2.4 Create IIdentityProviderService interface in GroundUp.Auth.Services
    - Stub interface only — Phase 10 implements against Keycloak
    - `ExchangeCodeForTokensAsync`, `ValidateTokenAsync`, `GetUserInfoAsync`
    - _Requirements: 14.1, 14.2, 14.3, 14.4_
  - [x] 2.5 Create IIdentityProviderAdminService interface in GroundUp.Auth.Services
    - Stub interface only — Phase 10 implements against Keycloak
    - `CreateUserAsync`, `DeleteUserAsync`, `CreateRealmAsync`, `ConfigureFederationAsync`
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5_

- [x] 3. Checkpoint — Ensure all tests pass
  - Ensure the solution compiles cleanly with the new DTOs and interfaces. Ask the user if questions arise.

- [x] 4. Service implementations — ConfigurationSigningKeyProvider, TokenService, AuthSessionService
  - [x] 4.1 Create ConfigurationSigningKeyProvider in GroundUp.Auth.Services/Token
    - Default implementation using AuthOptions.JwtSigningKey for all tenants (single-key mode)
    - Returns "default" kid for all tenants
    - Resolves by kid on validation
    - _Requirements: 13.3, 13.4_
  - [x] 4.2 Add System.IdentityModel.Tokens.Jwt package reference to GroundUp.Auth.Services.csproj
    - Required for JWT generation and validation
    - _Requirements: 1.5_
  - [x] 4.3 Create TokenService in GroundUp.Auth.Services/Token
    - GenerateTokenAsync: resolve user, resolve tenant-scoped + system roles, get signing key, build claims (sub, tid, email, name, roles[]), set kid header, sign with HMAC-SHA256, set issuer/audience/expiration from AuthOptions
    - ValidateTokenAsync: read kid from header, resolve validation key, validate signature/issuer/audience/expiration, return ClaimsPrincipal or null — never throw
    - _Requirements: 1.1–1.11, 2.1–2.8_
  - [x] 4.4 Create AuthSessionService in GroundUp.Auth.Services/Token
    - SetTenantAsync: query memberships, auto-select if single, return list if multiple, validate membership for explicit selection, return Forbidden if not a member
    - RefreshTokenAsync: re-validate membership, generate new token with fresh roles, return Forbidden if no longer a member
    - _Requirements: 3.1–3.3, 4.1–4.4, 5.1–5.3, 6.1–6.5_
  - [x] 4.5 Update AddGroundUpAuth() DI registration
    - Register ITokenService → TokenService (scoped)
    - Register IAuthSessionService → AuthSessionService (scoped)
    - Register ISigningKeyProvider → ConfigurationSigningKeyProvider via TryAddScoped (allows consumer override)
    - _Requirements: 17.1, 17.2, 17.3_
  - [ ]* 4.6 Write unit tests for ConfigurationSigningKeyProvider
    - Returns default key for any tenant, resolves by kid "default", returns null for unknown kid
    - _Requirements: 13.3, 13.4_
  - [ ]* 4.7 Write unit tests for TokenService
    - Generation with correct claims, kid header presence, expired token rejection, wrong key rejection, null for invalid user, issuer/audience validation
    - _Requirements: 1.1–1.11, 2.1–2.8_
  - [ ]* 4.8 Write unit tests for AuthSessionService
    - Single tenant auto-select, multi-tenant list, explicit selection, forbidden on non-membership, refresh with re-validation, forbidden on expired membership
    - _Requirements: 3.1–3.3, 4.1–4.4, 5.1–5.3, 6.1–6.5_

- [x] 5. Checkpoint — Ensure all tests pass
  - Ensure all service implementations compile and unit tests pass. Ask the user if questions arise.

- [x] 6. Middleware — JwtAuthenticationMiddleware, JwtTenantResolutionMiddleware, CsrfProtectionMiddleware
  - [x] 6.1 Add project reference from GroundUp.Api to GroundUp.Auth.Services
    - Required for middleware to access ITokenService, IIdentityProviderService, AuthOptions
    - _Requirements: 18.1_
  - [x] 6.2 Create JwtAuthenticationMiddleware in GroundUp.Api/Middleware
    - Extract token from cookie (AuthOptions.CookieName) first, then Authorization: Bearer header
    - Cookie takes precedence over header
    - Validate via ITokenService.ValidateTokenAsync
    - If GroundUp validation fails, try IIdentityProviderService (if registered in DI)
    - If IdP valid, call GetUserInfoAsync, build ClaimsPrincipal
    - If no token or all validation fails, proceed with anonymous identity
    - If JwtSigningKey not configured, skip all validation
    - Store auth source (cookie vs header) in HttpContext.Items for CSRF middleware
    - _Requirements: 7.1–7.6, 8.1–8.5, 10.1–10.5, 18.5_
  - [x] 6.3 Create JwtTenantResolutionMiddleware in GroundUp.Api/Middleware
    - Read tid claim from HttpContext.User using AuthOptions.TenantIdClaimType
    - Hydrate scoped TenantContext.TenantId
    - Set Guid.Empty if unauthenticated or no tid claim
    - _Requirements: 9.1–9.6_
  - [x] 6.4 Create CsrfProtectionMiddleware in GroundUp.Api/Middleware
    - Skip on GET/HEAD/OPTIONS (safe methods)
    - Skip if auth is bearer-based (not vulnerable to CSRF)
    - Enforce on cookie-auth + POST/PUT/DELETE: validate X-CSRF-Token header via IAntiforgery
    - Return 403 if missing or invalid
    - _Requirements: 11.1–11.6_
  - [x] 6.5 Update UseGroundUpMiddleware() pipeline ordering
    - New order: CorrelationId → JwtAuthentication → JwtTenantResolution → CsrfProtection → ExceptionHandling
    - Remove old TenantResolutionMiddleware from pipeline
    - Update XML doc comments
    - _Requirements: 18.1, 18.2, 18.3, 18.4_
  - [x] 6.6 Remove old TenantResolutionMiddleware
    - Delete `src/GroundUp.Api/Middleware/TenantResolutionMiddleware.cs`
    - Remove associated unit test file if it only tests the old middleware
    - _Requirements: 9.4, 18.4_
  - [x] 6.7 Verify TenantContext dual-registration pattern still works
    - Ensure AddGroundUpApi() still registers TenantContext + ITenantContext alias
    - Ensure JwtTenantResolutionMiddleware resolves the concrete TenantContext to set TenantId
    - Ensure AddGroundUpAuth() does not re-register ITenantContext (JwtTenantContext remains for SDK-only scenarios)
    - _Requirements: 21.1–21.5_
  - [ ]* 6.8 Write unit tests for JwtAuthenticationMiddleware
    - Cookie extraction, header extraction, cookie precedence, anonymous passthrough, missing signing key skip, IdP fallback when registered, IdP skip when not registered
    - _Requirements: 7.1–7.6, 8.1–8.5_
  - [ ]* 6.9 Write unit tests for JwtTenantResolutionMiddleware
    - tid claim extraction, Guid.Empty on unauthenticated, correct claim type from AuthOptions
    - _Requirements: 9.1–9.6_
  - [ ]* 6.10 Write unit tests for CsrfProtectionMiddleware
    - Skip on GET, skip on bearer auth, enforce on cookie + POST, reject missing token, reject invalid token
    - _Requirements: 11.1–11.6_

- [x] 7. Checkpoint — Ensure all tests pass
  - Ensure all middleware compiles, pipeline is correct, and unit tests pass. Ask the user if questions arise.

- [ ] 8. Integration tests and property tests
  - [ ]* 8.1 Write property test for token round-trip
    - **Property 1: Token round-trip** — For any valid userId and tenantId, `ValidateTokenAsync(GenerateTokenAsync(userId, tenantId))` returns a ClaimsPrincipal with matching sub and tid claims
    - **Validates: Requirements 1.1, 2.1**
  - [ ]* 8.2 Write property test for key isolation
    - **Property 2: Key isolation** — A token signed with Tenant A's key cannot be validated using Tenant B's key
    - **Validates: Requirements 2.3, 13.1**
  - [ ]* 8.3 Write property test for tenant membership invariant
    - **Property 4: Tenant membership invariant** — SetTenantAsync never issues a token for a tenant the user doesn't belong to
    - **Validates: Requirements 3.1, 5.2**
  - [ ]* 8.4 Write integration tests for end-to-end auth flow
    - Generate token → make authenticated request → verify ICurrentUser and ITenantContext populated correctly
    - Tenant selection: seed user with multiple tenants → verify selection flow
    - Token refresh: modify roles → refresh → verify new token has updated roles
    - CSRF: cookie-authenticated POST without CSRF token → 403
    - _Requirements: 7.4, 9.2, 11.5_

- [x] 9. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task group (1–2, 4, 6, 8) compiles independently and can be reviewed as a separate PR
- The old TenantResolutionMiddleware is removed in task 6.6 — integration tests that rely on X-Tenant-Id header will need updating
- IIdentityProviderService and IIdentityProviderAdminService are interface stubs only — no implementation until Phase 10
- Property tests validate the correctness properties defined in the design document
- Cookie security flags (HttpOnly, Secure, SameSite) are documented in the middleware but the actual cookie-setting happens in the consuming application's controller — the AuthSessionService returns the token string
