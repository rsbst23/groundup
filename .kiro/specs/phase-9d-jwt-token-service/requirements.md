# Requirements Document — Phase 9D: JWT Token Service & Auth Session

## Introduction

Phase 9D builds the JWT token generation/validation infrastructure, auth session service (tenant selection and token refresh), authentication middleware, and identity provider interface stubs for the GroundUp framework. This phase replaces the temporary `X-Tenant-Id` header-based tenant resolution with JWT claim-based resolution, introduces cookie-based authentication with CSRF protection, and establishes the `IIdentityProviderService` / `IIdentityProviderAdminService` interfaces that Phase 10 will implement with Keycloak. Together these components complete the GroundUp-issued token lifecycle: generate → validate → refresh → resolve tenant — all enforced transparently via middleware.

## Glossary

- **Token_Service**: The `ITokenService` implementation responsible for generating and validating GroundUp-issued JWT tokens containing user identity, tenant, roles, and permissions claims.
- **Auth_Session_Service**: The `IAuthSessionService` implementation responsible for tenant selection logic and token refresh, orchestrating between user-tenant membership queries and token generation.
- **JWT_Authentication_Middleware**: Middleware that validates GroundUp-issued JWT tokens from cookies or Authorization headers and populates `HttpContext.User` with the authenticated ClaimsPrincipal.
- **Tenant_Resolution_Middleware**: The replacement for the existing `X-Tenant-Id` header-based middleware that reads tenant identity from the authenticated JWT `tid` claim and hydrates `TenantContext`.
- **Identity_Provider_Service**: The `IIdentityProviderService` interface defining external identity provider operations (code exchange, token validation, user info retrieval). Stub only in this phase.
- **Identity_Provider_Admin_Service**: The `IIdentityProviderAdminService` interface defining external identity provider administrative operations (user creation, realm management). Stub only in this phase.
- **AuthOptions**: The existing configuration class extended with JWT signing key, issuer, audience, token expiration, and cookie settings.
- **SetTenantResponseDto**: The DTO returned by the tenant selection flow, containing either a token (tenant selected) or a list of available tenants (selection required).
- **Anti_Forgery_Middleware**: CSRF protection logic that validates anti-forgery tokens on state-changing HTTP methods (POST, PUT, DELETE) when authentication is cookie-based.

## Requirements

### Requirement 1: JWT Token Generation

**User Story:** As a framework consumer, I want to generate GroundUp-issued JWT tokens containing user identity, tenant, and roles, so that authenticated requests carry identity context while permissions are resolved server-side via the cached IPermissionService.

#### Acceptance Criteria

1. WHEN `GenerateTokenAsync(userId, tenantId, additionalClaims)` is called, THE Token_Service SHALL return a signed JWT string containing the standard claims: `sub` (userId), `tid` (tenantId), `email`, `name`, and `roles[]`. Permissions are NOT included in the token — they are resolved server-side via IPermissionService with in-memory caching.
2. THE Token_Service SHALL resolve the user's email and display name from the User entity via the user repository.
3. THE Token_Service SHALL resolve the user's role names within the specified tenant via the IPermissionService or role repository.
4. THE Token_Service SHALL resolve the user's role names within the specified tenant (tenant-scoped roles + system roles) via the role/user-role repositories. Permissions are resolved at runtime by IPermissionService and are NOT embedded in the token.
5. THE Token_Service SHALL sign the token using a FIPS-approved algorithm (default: HMAC-SHA256) with the signing key resolved from the `ISigningKeyProvider` for the specified tenant.
6. THE Token_Service SHALL set the token issuer from `AuthOptions.Issuer`.
7. THE Token_Service SHALL set the token audience from `AuthOptions.Audience`.
8. THE Token_Service SHALL set the token expiration to `AuthOptions.TokenExpirationMinutes` (default: 60 minutes) from the time of issuance.
9. WHEN `additionalClaims` is provided, THE Token_Service SHALL include those claims in the generated token alongside the standard claims.
10. IF the userId does not correspond to a valid user, THEN THE Token_Service SHALL return null.
11. THE Token_Service SHALL include a `kid` (Key ID) header in the generated JWT to identify which signing key was used.

### Requirement 2: JWT Token Validation

**User Story:** As a framework consumer, I want to validate GroundUp-issued JWT tokens and extract the authenticated identity, so that incoming requests can be authenticated without external provider calls.

#### Acceptance Criteria

1. WHEN `ValidateTokenAsync(token)` is called with a valid, non-expired token, THE Token_Service SHALL return a ClaimsPrincipal containing all claims from the token.
2. WHEN `ValidateTokenAsync(token)` is called with an expired token, THE Token_Service SHALL return null.
3. WHEN `ValidateTokenAsync(token)` is called with a token signed by a different key, THE Token_Service SHALL return null.
4. WHEN `ValidateTokenAsync(token)` is called with a malformed or empty string, THE Token_Service SHALL return null.
5. THE Token_Service SHALL validate the token issuer against `AuthOptions.Issuer`.
6. THE Token_Service SHALL validate the token audience against `AuthOptions.Audience`.
7. THE Token_Service SHALL NOT throw exceptions for invalid tokens — all failure cases return null.
8. THE Token_Service SHALL read the `kid` header from the token and resolve the corresponding validation key from the `ISigningKeyProvider`.

### Requirement 3: Tenant Selection — Single Tenant Auto-Select

**User Story:** As a framework consumer, I want users with exactly one tenant membership to be automatically selected into that tenant, so that single-tenant users skip the tenant selection step.

#### Acceptance Criteria

1. WHEN `SetTenantAsync(userId, null)` is called and the user belongs to exactly one tenant, THE Auth_Session_Service SHALL auto-select that tenant and return a SetTenantResponseDto with `SelectionRequired = false` and a valid token scoped to that tenant.
2. THE Auth_Session_Service SHALL query the user's tenant memberships via IUserTenantRepository.GetAllMembershipsForUserAsync.
3. THE generated token SHALL contain the auto-selected tenant's ID in the `tid` claim.

### Requirement 4: Tenant Selection — Multiple Tenants

**User Story:** As a framework consumer, I want users with multiple tenant memberships to be presented with a selection list, so that they can choose which tenant to operate in.

#### Acceptance Criteria

1. WHEN `SetTenantAsync(userId, null)` is called and the user belongs to multiple tenants, THE Auth_Session_Service SHALL return a SetTenantResponseDto with `SelectionRequired = true` and `AvailableTenants` populated with the user's tenant memberships.
2. THE AvailableTenants list SHALL contain TenantListItemDto entries with Id, Name, and Description for each tenant the user belongs to.
3. THE Auth_Session_Service SHALL resolve tenant names and descriptions by querying the Tenant entities associated with the user's memberships.
4. WHEN `SetTenantAsync(userId, null)` is called and the user belongs to multiple tenants, THE Auth_Session_Service SHALL NOT issue a token (Token property is null in the response).

### Requirement 5: Tenant Selection — Explicit Selection

**User Story:** As a framework consumer, I want users to explicitly select a tenant from their memberships, so that a tenant-scoped token is issued for the chosen tenant.

#### Acceptance Criteria

1. WHEN `SetTenantAsync(userId, tenantId)` is called with a valid tenantId that the user belongs to, THE Auth_Session_Service SHALL return a SetTenantResponseDto with `SelectionRequired = false` and a valid token scoped to the specified tenant.
2. IF the user does not belong to the specified tenant, THEN THE Auth_Session_Service SHALL return `OperationResult.Forbidden("User does not belong to the specified tenant")`.
3. THE Auth_Session_Service SHALL validate tenant membership via IUserTenantRepository before issuing the token.

### Requirement 6: Token Refresh

**User Story:** As a framework consumer, I want to refresh an existing token with fresh roles and permissions, so that authorization changes take effect without requiring re-authentication.

#### Acceptance Criteria

1. WHEN `RefreshTokenAsync(userId, tenantId)` is called, THE Auth_Session_Service SHALL re-validate that the user still belongs to the specified tenant.
2. IF the user no longer belongs to the specified tenant, THEN THE Auth_Session_Service SHALL return `OperationResult.Forbidden("User no longer belongs to the specified tenant")`.
3. WHEN the user still belongs to the tenant, THE Auth_Session_Service SHALL generate a new token with fresh roles and permissions resolved from the current database state.
4. THE refreshed token SHALL have a new expiration time calculated from the moment of refresh.
5. THE Auth_Session_Service SHALL use the Token_Service to generate the refreshed token.

### Requirement 7: JWT Authentication Middleware — Token Extraction

**User Story:** As a framework consumer, I want incoming requests to be authenticated from cookies or Authorization headers, so that the framework supports both browser-based and API-based clients.

#### Acceptance Criteria

1. THE JWT_Authentication_Middleware SHALL read the JWT token from the `AuthToken` cookie (configurable name via `AuthOptions.CookieName`, default: "AuthToken").
2. THE JWT_Authentication_Middleware SHALL read the JWT token from the `Authorization: Bearer {token}` header when no cookie is present.
3. WHEN both a cookie and an Authorization header are present, THE JWT_Authentication_Middleware SHALL use the cookie value (cookie takes precedence).
4. WHEN a valid token is extracted and validated, THE JWT_Authentication_Middleware SHALL set `HttpContext.User` to the ClaimsPrincipal returned by the Token_Service.
5. WHEN no token is present or the token is invalid, THE JWT_Authentication_Middleware SHALL allow the request to proceed with an unauthenticated identity (anonymous access is permitted — authorization is enforced at the service layer).
6. THE JWT_Authentication_Middleware SHALL use the Token_Service for validation of GroundUp-issued tokens.

### Requirement 8: JWT Authentication Middleware — Dual Scheme Support

**User Story:** As a framework consumer, I want the middleware to support both GroundUp-issued tokens and external identity provider tokens, so that the framework can authenticate users from multiple sources.

#### Acceptance Criteria

1. THE JWT_Authentication_Middleware SHALL first attempt to validate the token as a GroundUp-issued token via the Token_Service.
2. IF GroundUp validation fails, THE JWT_Authentication_Middleware SHALL attempt to validate the token via the Identity_Provider_Service.ValidateTokenAsync.
3. WHEN no IIdentityProviderService is registered in DI (Phase 10 not yet integrated), THE JWT_Authentication_Middleware SHALL skip the IdP validation step gracefully.
4. WHEN the IdP validation succeeds, THE JWT_Authentication_Middleware SHALL call IIdentityProviderService.GetUserInfoAsync to populate the ClaimsPrincipal.
5. THE dual scheme approach SHALL allow consuming applications to use GroundUp tokens, IdP tokens, or both simultaneously.

### Requirement 9: Tenant Resolution Middleware — JWT-Based

**User Story:** As a framework consumer, I want tenant context to be resolved from the authenticated JWT claims, so that tenant isolation is enforced based on the authenticated identity rather than a client-supplied header.

#### Acceptance Criteria

1. THE Tenant_Resolution_Middleware SHALL read the tenant identifier from the `tid` claim of the authenticated ClaimsPrincipal (`HttpContext.User`).
2. THE Tenant_Resolution_Middleware SHALL hydrate the scoped `TenantContext.TenantId` with the value from the `tid` claim.
3. WHEN no authenticated user is present or no `tid` claim exists, THE Tenant_Resolution_Middleware SHALL set `TenantContext.TenantId` to `Guid.Empty`.
4. THE Tenant_Resolution_Middleware SHALL NOT read from the `X-Tenant-Id` header — that approach is removed entirely.
5. THE Tenant_Resolution_Middleware SHALL run AFTER the JWT_Authentication_Middleware in the pipeline so that `HttpContext.User` is populated before tenant resolution.
6. THE Tenant_Resolution_Middleware SHALL use the claim type from `AuthOptions.TenantIdClaimType` (default: "tenant_id") to locate the tenant claim.

### Requirement 10: Cookie Security

**User Story:** As a framework consumer, I want auth cookies to follow enterprise security best practices, so that token storage in browsers is protected against common web attacks.

#### Acceptance Criteria

1. THE JWT_Authentication_Middleware SHALL set the `HttpOnly` flag on all auth cookies to prevent JavaScript access.
2. THE JWT_Authentication_Middleware SHALL set the `Secure` flag on auth cookies to ensure transmission only over HTTPS.
3. THE JWT_Authentication_Middleware SHALL set `SameSite=Strict` on auth cookies to prevent cross-site request forgery via cookie attachment.
4. THE cookie name SHALL be configurable via `AuthOptions.CookieName` (default: "AuthToken").
5. THE cookie expiration SHALL align with the token expiration configured in `AuthOptions.TokenExpirationMinutes`.
6. WHEN a new token is issued (via SetTenant or Refresh), THE Auth_Session_Service response SHALL include the token string so that the consuming application's controller can set the cookie with the correct security flags.

### Requirement 11: CSRF Protection

**User Story:** As a framework consumer, I want anti-forgery token validation on state-changing requests when using cookie authentication, so that cross-site request forgery attacks are prevented.

#### Acceptance Criteria

1. WHEN authentication is cookie-based (token read from cookie, not Authorization header), THE Anti_Forgery_Middleware SHALL validate an anti-forgery token on POST, PUT, and DELETE requests.
2. WHEN authentication is header-based (Authorization: Bearer), THE Anti_Forgery_Middleware SHALL skip CSRF validation (bearer tokens are not automatically attached by browsers).
3. THE anti-forgery token SHALL be provided via the `X-CSRF-Token` request header.
4. THE framework SHALL provide an endpoint or mechanism for clients to obtain a valid CSRF token.
5. IF a state-changing request uses cookie auth and the CSRF token is missing or invalid, THEN THE Anti_Forgery_Middleware SHALL return HTTP 403 Forbidden.
6. THE Anti_Forgery_Middleware SHALL integrate with ASP.NET Core's built-in anti-forgery infrastructure.

### Requirement 12: AuthOptions Extension

**User Story:** As a framework consumer, I want to configure JWT signing, issuer, audience, expiration, and cookie settings via AuthOptions, so that all token behavior is centrally configurable.

#### Acceptance Criteria

1. THE AuthOptions class SHALL expose `JwtSigningKey` (string, used as the default/fallback signing key when no per-tenant key is configured — no default value).
2. THE AuthOptions class SHALL expose `Issuer` (string, default: "GroundUp").
3. THE AuthOptions class SHALL expose `Audience` (string, default: "GroundUp").
4. THE AuthOptions class SHALL expose `TokenExpirationMinutes` (int, default: 60).
5. THE AuthOptions class SHALL expose `CookieName` (string, default: "AuthToken").
6. THE AuthOptions class SHALL expose `CookieSecure` (bool, default: true).
7. THE AuthOptions class SHALL expose `CookieSameSite` (SameSiteMode, default: Strict).
8. ALL new AuthOptions properties SHALL be bindable from the "GroundUp:Auth" configuration section.

### Requirement 13: ISigningKeyProvider — Per-Tenant Key Isolation

**User Story:** As a framework consumer, I want to use different signing keys per tenant, so that token compromise in one tenant does not affect other tenants' token security.

#### Acceptance Criteria

1. THE `ISigningKeyProvider` interface SHALL define `GetSigningKeyAsync(Guid tenantId)` returning `Task<SigningKeyInfo>` where SigningKeyInfo contains the key material and a key ID (`kid`).
2. THE `ISigningKeyProvider` interface SHALL define `GetValidationKeyAsync(string kid)` returning `Task<SigningKeyInfo?>` for resolving a key by its ID during token validation.
3. THE framework SHALL provide a default `ConfigurationSigningKeyProvider` implementation that returns the `AuthOptions.JwtSigningKey` for all tenants (single-key mode).
4. WHEN a tenant does not have a custom key configured, THE ISigningKeyProvider SHALL fall back to the application-wide default key from AuthOptions.
5. THE Token_Service SHALL use `ISigningKeyProvider.GetSigningKeyAsync(tenantId)` when generating tokens.
6. THE Token_Service SHALL use `ISigningKeyProvider.GetValidationKeyAsync(kid)` when validating tokens, reading the `kid` from the JWT header.
7. THE `ISigningKeyProvider` interface SHALL live in the `GroundUp.Auth.Services` namespace.
8. CONSUMING applications MAY provide their own `ISigningKeyProvider` implementation to integrate with key management systems (AWS KMS, Azure Key Vault, HashiCorp Vault, etc.).
9. THE `SigningKeyInfo` record SHALL contain: `string KeyId`, `byte[] KeyMaterial`, `string Algorithm` (default: "HS256").

### Requirement 14: IIdentityProviderService Interface

**User Story:** As a framework developer, I want to define the identity provider service interface now, so that Phase 10 can implement it against Keycloak without changing the middleware or auth session contracts.

#### Acceptance Criteria

1. THE IIdentityProviderService interface SHALL define `ExchangeCodeForTokensAsync(string code, string redirectUri, string? realm)` returning `Task<TokenResponseDto?>`.
2. THE IIdentityProviderService interface SHALL define `ValidateTokenAsync(string token)` returning `Task<bool>`.
3. THE IIdentityProviderService interface SHALL define `GetUserInfoAsync(string accessToken)` returning `Task<ExternalUserInfo?>`.
4. THE IIdentityProviderService interface SHALL live in the `GroundUp.Auth.Services` namespace.
5. WHEN no implementation of IIdentityProviderService is registered in DI, THE JWT_Authentication_Middleware SHALL handle the missing service gracefully (no exception, skip IdP validation).

### Requirement 15: IIdentityProviderAdminService Interface

**User Story:** As a framework developer, I want to define the identity provider admin service interface now, so that Phase 10 can implement administrative operations (user provisioning, realm management) without changing existing contracts.

#### Acceptance Criteria

1. THE IIdentityProviderAdminService interface SHALL define `CreateUserAsync(string email, string displayName)` returning `Task<string>` (the external user ID).
2. THE IIdentityProviderAdminService interface SHALL define `DeleteUserAsync(string externalUserId)` returning `Task`.
3. THE IIdentityProviderAdminService interface SHALL define `CreateRealmAsync(string realmName, object config)` returning `Task`.
4. THE IIdentityProviderAdminService interface SHALL define `ConfigureFederationAsync(string realmName, object idpConfig)` returning `Task`.
5. THE IIdentityProviderAdminService interface SHALL live in the `GroundUp.Auth.Services` namespace.

### Requirement 16: DTOs

**User Story:** As a framework developer, I want well-defined DTOs for the token and auth session operations, so that the API contracts are clear and strongly typed.

#### Acceptance Criteria

1. THE SetTenantRequestDto SHALL be a record with property `Guid? TenantId`.
2. THE SetTenantResponseDto SHALL be a record with properties: `bool SelectionRequired`, `List<TenantListItemDto>? AvailableTenants`, `string? Token`.
3. THE TenantListItemDto SHALL be a record with properties: `Guid Id`, `string Name`, `string? Description`.
4. THE TokenResponseDto SHALL be a record with properties: `string AccessToken`, `string? RefreshToken`, `int ExpiresIn`, `string? IdToken`.
5. THE ExternalUserInfo SHALL be a record with properties: `string ExternalUserId`, `string Email`, `string? DisplayName`, `IDictionary<string, string>? Attributes`.
6. ALL DTOs SHALL live in the `GroundUp.Auth.Core.Dtos` namespace.
7. ALL DTOs SHALL use the `record` keyword following framework conventions.

### Requirement 17: DI Registration Extension

**User Story:** As a framework consumer, I want the existing `AddGroundUpAuth()` extension to also register the token service and auth session service, so that setup remains a single call.

#### Acceptance Criteria

1. WHEN `services.AddGroundUpAuth()` is called, THE extension method SHALL register ITokenService as a scoped service.
2. WHEN `services.AddGroundUpAuth()` is called, THE extension method SHALL register IAuthSessionService as a scoped service.
3. THE existing registrations (IPermissionService, ICurrentUser, ITenantContext, cache handlers) SHALL remain unchanged.
4. THE AddGroundUpAuth extension SHALL validate that `AuthOptions.JwtSigningKey` is configured and throw a descriptive exception at startup if missing.

### Requirement 18: Middleware Pipeline Integration

**User Story:** As a framework consumer, I want the authentication and tenant resolution middleware to integrate into the existing `UseGroundUpMiddleware()` pipeline, so that no additional setup is required.

#### Acceptance Criteria

1. THE `UseGroundUpMiddleware()` extension SHALL register middleware in the order: CorrelationId → JWT Authentication → Tenant Resolution (JWT-based) → CSRF Protection → Exception Handling.
2. THE JWT_Authentication_Middleware SHALL run before Tenant_Resolution_Middleware so that `HttpContext.User` is populated before tenant claim extraction.
3. THE Anti_Forgery_Middleware SHALL run after authentication so it can determine whether the request was cookie-authenticated.
4. THE existing `TenantResolutionMiddleware` (X-Tenant-Id header-based) SHALL be replaced by the new JWT-based implementation.
5. WHEN `AuthOptions.JwtSigningKey` is not configured (auth module not fully set up), THE JWT_Authentication_Middleware SHALL skip token validation and allow all requests through as unauthenticated.

### Requirement 19: ITokenService Interface Definition

**User Story:** As a framework developer, I want a clean interface for the token service, so that it can be mocked in tests and potentially swapped for alternative implementations.

#### Acceptance Criteria

1. THE ITokenService interface SHALL define `GenerateTokenAsync(Guid userId, Guid tenantId, IEnumerable<Claim>? additionalClaims = null)` returning `Task<string?>`.
2. THE ITokenService interface SHALL define `ValidateTokenAsync(string token)` returning `Task<ClaimsPrincipal?>`.
3. THE ITokenService interface SHALL live in the `GroundUp.Auth.Services` namespace.
4. THE ITokenService interface SHALL be registered as a scoped service in the DI container.

### Requirement 20: IAuthSessionService Interface Definition

**User Story:** As a framework developer, I want a clean interface for the auth session service, so that it can be mocked in tests and consumed by auth controllers in Phase 10.

#### Acceptance Criteria

1. THE IAuthSessionService interface SHALL define `SetTenantAsync(Guid userId, Guid? tenantId)` returning `Task<OperationResult<SetTenantResponseDto>>`.
2. THE IAuthSessionService interface SHALL define `RefreshTokenAsync(Guid userId, Guid tenantId)` returning `Task<OperationResult<string>>`.
3. THE IAuthSessionService interface SHALL live in the `GroundUp.Auth.Services` namespace.
4. THE IAuthSessionService interface SHALL be registered as a scoped service in the DI container.

### Requirement 21: TenantContext Middleware Integration

**User Story:** As a framework developer, I want the existing `TenantContext` dual-registration pattern (concrete + interface) to continue working, so that the JWT-based middleware can set the tenant and repositories can read it via ITenantContext.

#### Acceptance Criteria

1. THE `AddGroundUpApi()` extension SHALL continue to register `TenantContext` as a concrete scoped service and `ITenantContext` as a scoped alias pointing to the same instance.
2. THE JWT-based Tenant_Resolution_Middleware SHALL resolve the scoped `TenantContext` instance and set its `TenantId` property from the JWT claim.
3. WHEN `AddGroundUpAuth()` is called, THE extension SHALL NOT re-register ITenantContext (the JwtTenantContext registration from 9C is superseded by the middleware-based approach for HTTP scenarios).
4. THE `JwtTenantContext` class SHALL remain available for scenarios where middleware is not used (SDK-only, direct service calls with a pre-populated HttpContext).
5. WHEN both `AddGroundUpApi()` and `AddGroundUpAuth()` are called, THE middleware-based TenantContext (set by Tenant_Resolution_Middleware) SHALL take precedence for HTTP requests.

