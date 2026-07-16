# Requirements Document

## Introduction

Phase 10C delivers the first user-facing authentication slice for the GroundUp framework. It implements the auth cookie writer, host-based tenant resolution, authorization URL building with the full OAuth2/OIDC security model (state, nonce, PKCE), the callback flow dispatcher with strategy-pattern handlers, and two concrete callback flow handlers (NewOrganization and Login). It introduces the tenant-scoped TenantAdmin role with centralized permission-bypass logic, renames `FlowType.MultiTenantSelection` to `FlowType.Login`, adds the sliding-window token refresh path with an absolute session lifetime cap, extends the `AuthFlowState` schema, and wires auth controller endpoints in the API layer.

This phase depends on:
- Phase 10A (interfaces: `IAuthCookieWriter`, `IAuthFlowStateService`, `IAuthSessionService`, `AuthFlowState` entity, `AuthOptions`)
- Phase 10AB (settings substrate with `auth.application.default-domain`, cascading `ISettingsService`)
- Phase 10B (Keycloak provider: code exchange, userinfo, JWKS validation)

The output is production code distributed across `GroundUp.Auth.Services`, `GroundUp.Auth.Api`, `GroundUp.Auth.Core`, and `GroundUp.Auth.Data.Postgres` (EF migration), plus integration tests validating the end-to-end flows against Testcontainers Keycloak.

### Context and Non-Functional Notes

These notes record decisions made during design review. They are not standalone 10C functional requirements but constrain the design.

- **Authentication vs. authorization split**: Keycloak performs authentication only. All authorization (roles, permissions, memberships) lives in the GroundUp application database. SuperAdmin, TenantAdmin, and regular users are all application-DB constructs. Keycloak does not require users to hold any roles.
- **Per-request revocation tradeoff**: Per-request validation of the GroundUp session against Keycloak is explicitly avoided for performance reasons. The chosen pattern is a sliding refresh window bounded by an absolute session lifetime cap (Requirements 9 and 13). For standard tenants in 10C, app-side membership deactivation — re-validated on every refresh — is the revocation control. Near-instant enterprise (Active Directory) revocation is deferred to Phase 10E, with this tradeoff documented.
- **Enterprise IdP configuration direction (Phase 10E)**: For enterprise identity-provider configuration, the chosen direction is to redirect the enterprise TenantAdmin to Keycloak's realm-scoped admin console rather than building per-IdP configuration UIs in GroundUp, avoiding re-implementation of capabilities Keycloak already provides. The likely path is to start with Keycloak's native UI and leave the door open to add GroundUp-native IdP configuration later. This will require granting enterprise TenantAdmins a realm-scoped Keycloak admin role at realm provisioning time (10E). Phase 10C makes no decision that blocks this direction; application authorization remains in the application database.

## Glossary

- **AuthCookieWriter**: The concrete implementation of `IAuthCookieWriter` that writes/clears the authentication cookie, deriving its Domain attribute from the `auth.application.default-domain` setting.
- **HostTenantResolver**: The service (`IHostTenantResolver` interface + implementation) that resolves incoming HTTP Host headers to tenants (returned as `TenantDto`) by matching subdomains against the configured default domain and looking up by `Tenant.Slug`.
- **HostTenantResolutionMiddleware**: ASP.NET Core middleware that runs early in the pipeline, invokes `IHostTenantResolver`, and stashes the resolved tenant (`TenantDto`) in a scoped service for downstream consumption.
- **HostResolvedTenant**: A scoped service holding the tenant resolved from the request Host header. Its `Tenant` property holds a `TenantDto` (or null when no tenant was resolved from the host). It is populated by `HostTenantResolutionMiddleware` and is distinct from the JWT-derived `TenantContext`.
- **Pending-selection authentication**: The authenticated state during the multi-membership tenant-selection window. When a user has two or more eligible memberships and no host-pinning, `LoginFlowHandler` does NOT issue a GroundUp token. Instead, the authentication cookie retains the Keycloak token (already validated at callback), which `JwtAuthenticationMiddleware` authenticates via its IdP-fallback path as an identity-only principal. That Keycloak-token principal carries the external `sub` claim but NO `tid` tenant claim, so repositories receive no tenant context and the principal grants no tenant-scoped data access. The user calls `POST /auth/set-tenant` to select a tenant, at which point the FIRST full tenant-scoped GroundUp token is issued.
- **Host/Token reconciliation**: The behavioral comparison between the host-resolved tenant (`HostResolvedTenant`, set from the request Host header) and the JWT-derived tenant (`TenantContext`, set from the token `tid` claim) on authenticated requests. When the two disagree, the system must not serve the host tenant's resources under the token's existing tenant context; the resolution depends on whether the host-resolved tenant is standard (shared-realm) or enterprise (`RealmName` set) and whether the user is a member. See Requirement 18.
- **AuthUrlBuilderService**: A service (`IAuthUrlBuilder` interface + implementation) that constructs Keycloak authorization URLs with the state token, PKCE parameters, OIDC nonce, redirect_uri, and realm routing.
- **AuthFlowService**: The orchestrator service (`IAuthFlowService` interface + implementation) that initiates callback-based flows (creates `AuthFlowState` rows) and dispatches callbacks to the appropriate `IFlowHandler`.
- **FlowHandler**: An implementation of the `IFlowHandler` strategy interface, resolved from DI by `FlowType`. Each handler encapsulates the logic for one callback-based authentication flow.
- **NewOrganizationFlowHandler**: The handler for `FlowType.NewOrganization` — atomically creates a new tenant, user, membership, TenantAdmin role, and role assignment, then issues a token.
- **LoginFlowHandler**: The handler for `FlowType.Login` — resolves user memberships and handles auto-join, auto-select, the standard-tenant picker, and host-pinning scenarios.
- **TenantAdmin**: A well-known, tenant-scoped role created once per tenant (belongs to its own tenant, carries that tenant's `TenantId`, marked immutable via `IsSystem=true`). A user holding TenantAdmin in a given tenant bypasses all permission checks within that tenant. The bypass logic is centralized exclusively in `PermissionService` and is always tenant-scoped — it never leaks across tenants. TenantAdmin is NOT a global `RoleType=System` role.
- **SuperAdmin**: The only truly global role, seeded under the well-known System tenant with `RoleType=System`. SuperAdmin is the role surfaced by `IUserRoleRepository.GetSystemRolesForUserAsync()` (which bypasses tenant filtering); TenantAdmin must never appear in that global query.
- **StateToken**: A separate, cryptographically-random, unguessable token used as the OAuth `state` parameter. It is distinct from the `AuthFlowState` UUIDv7 primary key (which is timestamp-based and partially predictable and therefore unsuitable as a security value). The StateToken is persisted on the `AuthFlowState` row (indexed) and is also written to a short-lived HttpOnly cookie for browser-binding CSRF protection.
- **StateCookie**: A short-lived, HttpOnly cookie set at flow initiation holding the StateToken, compared against the callback's `state` query parameter to bind the callback to the originating browser.
- **Nonce (OIDC)**: The value stored in `AuthFlowState.Nonce`, sent in the authorize request and validated against the `nonce` claim of the returned `id_token` at callback. It is distinct from the state token and PKCE.
- **PKCE**: Proof Key for Code Exchange — an OAuth2 extension preventing authorization code interception. A `code_verifier` (43–128 URL-safe characters) is generated and stored on the `AuthFlowState`; the `code_challenge` is `Base64URL(SHA256(code_verifier))` without padding, with `code_challenge_method=S256`.
- **Host_Pinning**: When the host resolver identifies a specific tenant from the subdomain, the login flow is pinned to that tenant — no picker is shown and non-members receive access denied.
- **Default_Tenant**: A configurable setting (`auth.application.default-tenant-slug`) that enables auto-join for new users with zero memberships in single-tenant deployments.
- **DefaultRole_Cascade**: The cascading setting `auth.application.default-role`, resolved via `ISettingsService` cascade (tenant level → application level → system). Determines the role assigned to auto-joined and non-founding users. When no level provides a value, no role is assigned.
- **AbsoluteSessionLifetime**: The configurable cap `AuthOptions.AbsoluteSessionLifetimeMinutes` (default 480 minutes / 8 hours), measured from the original authentication time. Sliding refresh may re-issue tokens only until this cap is reached.
- **AuthController**: The API controller exposing authentication endpoints (`/auth/login`, `/auth/register`, `/auth/callback`, `/auth/me`, `/auth/set-tenant`, `/auth/refresh`, `/auth/logout`).
- **Last_Admin_Guard**: A rule preventing removal of the final active TenantAdmin from a tenant, enforced in the role-assignment service layer.

## Requirements

### Requirement 1: IAuthCookieWriter Implementation

**User Story:** As a GroundUp framework developer, I want a concrete `IAuthCookieWriter` implementation that writes secure authentication cookies with domain derived from the `auth.application.default-domain` setting, so that tokens are stored safely in the browser and shared across subdomains when configured.

#### Acceptance Criteria

1. THE AuthCookieWriter SHALL read the `auth.application.default-domain` setting from `ISettingsService` to determine the cookie Domain attribute.
2. WHEN `auth.application.default-domain` resolves to a non-empty, non-whitespace value (e.g., "sampleapp.com"), THE AuthCookieWriter SHALL set the cookie Domain attribute to `.{domain}` (prepending a dot for parent-domain scoping).
3. WHEN `auth.application.default-domain` resolves to null, empty, or whitespace-only, THE AuthCookieWriter SHALL omit the Domain attribute (host-only cookie behavior).
4. THE AuthCookieWriter SHALL use `AuthOptions.CookieName` (default: "AuthToken") as the cookie name.
5. THE AuthCookieWriter SHALL set the cookie value to the `token` string parameter passed to `WriteAuthCookie`.
6. THE AuthCookieWriter SHALL set the cookie Secure flag to the value of `AuthOptions.CookieSecure` (default: true).
7. THE AuthCookieWriter SHALL set the cookie SameSite attribute to the value of `AuthOptions.CookieSameSite` (default: Strict).
8. THE AuthCookieWriter SHALL set the cookie Expires to `AuthOptions.TokenExpirationMinutes` (default: 60) minutes from the current UTC time, producing a persistent cookie that survives browser restarts until expiration.
9. THE AuthCookieWriter SHALL set the cookie HttpOnly flag to true (tokens must not be accessible to client-side JavaScript).
10. THE AuthCookieWriter SHALL set the cookie Path to "/" (available to all routes).
11. WHEN `ClearAuthCookie` is called, THE AuthCookieWriter SHALL delete the cookie by appending a Set-Cookie header with the same name (`AuthOptions.CookieName`), same Domain (derived from the setting using the same logic as `WriteAuthCookie`), same Path ("/"), and an expiration date in the past.
12. IF the `token` parameter passed to `WriteAuthCookie` is null or empty, THEN THE AuthCookieWriter SHALL throw an `ArgumentException` (a caller must never write an empty authentication cookie).

### Requirement 2: IHostTenantResolver Service

**User Story:** As a GroundUp framework developer, I want a host-based tenant resolution service that maps incoming HTTP Host headers to tenant entities via subdomain matching, so that multi-tenant applications can identify the target tenant from the URL without explicit tenant selection.

#### Acceptance Criteria

1. THE HostTenantResolver SHALL read the `auth.application.default-domain` setting from `ISettingsService` to determine the base domain for subdomain matching.
2. WHEN the incoming Host header matches the pattern `{subdomain}.{default-domain}`, THE HostTenantResolver SHALL strip any port number from the Host header before matching, extract the single label immediately preceding the default-domain as the candidate tenant slug, and perform case-insensitive comparison for both the domain suffix and the slug lookup.
3. WHEN a candidate slug is extracted, THE HostTenantResolver SHALL call `ITenantRepository.GetBySlugAsync` with the slug (lowercased) to look up the tenant.
4. WHEN the tenant lookup succeeds and the tenant's `IsActive` property is true, THE HostTenantResolver SHALL return the resolved `TenantDto`.
5. IF the tenant lookup fails (slug not found or tenant `IsActive` is false), THEN THE HostTenantResolver SHALL return null (no tenant resolved).
6. WHEN the incoming Host header does not match `{subdomain}.{default-domain}` (e.g., bare domain, IP address, different domain, or multi-level subdomain such as `a.b.{default-domain}`), THE HostTenantResolver SHALL return null.
7. IF `auth.application.default-domain` is empty or null, THEN THE HostTenantResolver SHALL return null for all requests (host resolution is disabled).
8. THE HostTenantResolver SHALL be registered as a scoped service implementing `IHostTenantResolver`.

### Requirement 3: HostTenantResolutionMiddleware

**User Story:** As a GroundUp framework developer, I want middleware that runs early in the HTTP pipeline to resolve the tenant from the Host header and make it available to downstream services, so that flow handlers and controllers can access the host-resolved tenant without re-parsing the host.

#### Acceptance Criteria

1. THE HostTenantResolutionMiddleware SHALL invoke `IHostTenantResolver.ResolveAsync` with the current request's `HttpContext.Request.Host` value on every request.
2. WHEN the resolver returns a tenant, THE HostTenantResolutionMiddleware SHALL set the `Tenant` property of the scoped `HostResolvedTenant` service to the resolved tenant (`TenantDto`), making it available to downstream components via dependency injection.
3. WHEN the resolver returns null, THE HostTenantResolutionMiddleware SHALL leave the `HostResolvedTenant.Tenant` property as null (indicating no tenant was resolved from the host).
4. IF `IHostTenantResolver.ResolveAsync` throws an exception, THEN THE HostTenantResolutionMiddleware SHALL treat the resolution as null (no tenant resolved), log the exception at Warning level, and continue the pipeline without short-circuiting.
5. THE HostTenantResolutionMiddleware SHALL run before `JwtAuthenticationMiddleware` in the pipeline (host resolution is independent of authentication state).
6. THE HostTenantResolutionMiddleware SHALL call the next middleware regardless of resolution outcome (it never short-circuits the pipeline).

### Requirement 4: AuthUrlBuilderService

**User Story:** As a GroundUp framework developer, I want a service that builds Keycloak authorization URLs with a state token, PKCE, OIDC nonce, and realm routing, so that flow initiation endpoints can redirect the user to the correct identity provider with three independent, non-interchangeable security controls.

#### Acceptance Criteria

1. THE AuthUrlBuilderService SHALL build URLs targeting `{KeycloakOptions.PublicBaseUrl}/realms/{realm}/protocol/openid-connect/auth`.
2. THE AuthUrlBuilderService SHALL generate a cryptographically-random, unguessable state token of at least 32 bytes of randomness, encoded as a Base64URL string without padding, and set the `state` query parameter to that token.
3. THE AuthUrlBuilderService SHALL NOT use the `AuthFlowState.Id` (UUIDv7 primary key) as the `state` value, because UUIDv7 is timestamp-based and partially predictable.
4. THE AuthUrlBuilderService SHALL generate a PKCE code_verifier consisting of a cryptographically random string between 43 and 128 characters (inclusive) using only unreserved URL-safe characters (A-Z, a-z, 0-9, `-`, `.`, `_`, `~`) and compute the `code_challenge` as `Base64URL(SHA256(code_verifier))` without padding, with `code_challenge_method=S256`.
5. THE AuthUrlBuilderService SHALL generate an OIDC `nonce` parameter of at least 32 bytes of randomness, encoded as a Base64URL string without padding, and include it in the URL query parameters.
6. THE AuthUrlBuilderService SHALL set `redirect_uri` to the configured callback URL resolved from `AuthOptions` (e.g., `/auth/callback`), expressed as an absolute URL.
7. THE AuthUrlBuilderService SHALL set `client_id` to `KeycloakOptions.AppClientId`.
8. THE AuthUrlBuilderService SHALL set `response_type=code` and `scope=openid email profile`.
9. WHEN the host-resolved tenant (provided via `IHostTenantResolver`) has a non-null `RealmName`, THE AuthUrlBuilderService SHALL use that tenant's `RealmName` as the realm segment in the authorization URL.
10. IF the host-resolved tenant has a null `RealmName` OR no tenant is host-resolved, THEN THE AuthUrlBuilderService SHALL use `KeycloakOptions.SharedRealmName` as the realm segment in the authorization URL.
11. THE AuthUrlBuilderService SHALL return a result containing the fully constructed authorization URL, the generated state token, the generated nonce, the generated code_verifier, and the resolved redirect_uri, so that the caller can persist these values on the `AuthFlowState` for validation and code exchange.
12. IF `KeycloakOptions.PublicBaseUrl` or `KeycloakOptions.AppClientId` is null or whitespace at the time of URL construction, THEN THE AuthUrlBuilderService SHALL return a failure `OperationResult` indicating the missing configuration rather than constructing a malformed URL.

### Requirement 5: IFlowHandler Strategy Interface and Dispatcher

**User Story:** As a GroundUp framework developer, I want a strategy-pattern dispatcher that routes OAuth callbacks to the correct flow handler based on the `FlowType` stored in the `AuthFlowState`, so that each callback-based flow type is handled by a dedicated, testable handler without a monolithic switch statement.

#### Acceptance Criteria

1. THE `IFlowHandler` interface SHALL define a `FlowType HandledFlowType { get; }` property identifying which flow type the handler processes.
2. THE `IFlowHandler` interface SHALL define a `Task<FlowResult> HandleCallbackAsync(FlowCallbackContext context)` method that executes the flow logic.
3. THE AuthFlowService SHALL resolve all registered `IFlowHandler` implementations from DI at dispatch time, and the registered handlers SHALL be limited to the callback-based flows `FlowType.NewOrganization` and `FlowType.Login` for Phase 10C.
4. WHEN a callback arrives, THE AuthFlowService SHALL look up the `AuthFlowState` by the state token carried in the callback's `state` query parameter (not by primary key).
5. WHEN the callback is processed, THE AuthFlowService SHALL compare the callback's `state` query parameter against the StateToken held in the HttpOnly state cookie, and IF the cookie is missing OR the values do not match, THEN THE AuthFlowService SHALL reject the callback with an error result (CSRF browser-binding failure).
6. WHEN the state token is valid, THE AuthFlowService SHALL consume the `AuthFlowState` (via `IAuthFlowStateService.ConsumeAsync`) and route to the handler matching the consumed state's `FlowType`.
7. IF no handler is registered for the consumed `FlowType`, THEN THE AuthFlowService SHALL mark the flow as failed and return an error result.
8. WHEN a consumed `AuthFlowState` has already been consumed (replay attack), THE AuthFlowService SHALL return HTTP 410 Gone.
9. WHEN a consumed `AuthFlowState` has expired, THE AuthFlowService SHALL return an error indicating the flow has expired.
10. THE AuthFlowService SHALL pass the authorization code, the stored code_verifier, the stored redirect_uri, and the consumed `AuthFlowState` (including the stored nonce) to the resolved handler via `FlowCallbackContext`, so the handler can perform code exchange and validate the id_token nonce.

### Requirement 6: AuthFlowService — Flow Initiation

**User Story:** As a GroundUp framework developer, I want the `AuthFlowService` to create `AuthFlowState` rows with all required security metadata when a flow is initiated, so that the callback can be correlated and validated back to the originating request with full context.

#### Acceptance Criteria

1. WHEN a flow is initiated, THE AuthFlowService SHALL create an `AuthFlowState` row with the specified `FlowType`, the generated state token, the generated nonce, expiration, client IP, and user-agent.
2. THE AuthFlowService SHALL store the PKCE code_verifier on the `AuthFlowState` so it is available during callback processing.
3. THE AuthFlowService SHALL store the exact redirect_uri used at authorize time on the `AuthFlowState` so the identical value can be reused at code exchange.
4. WHEN the initiated flow is `FlowType.NewOrganization`, THE AuthFlowService SHALL store the supplied organization name on the `AuthFlowState` before redirecting to Keycloak.
5. THE AuthFlowService SHALL set a short-lived HttpOnly cookie containing the state token at flow initiation, for browser-binding validation at callback.
6. THE AuthFlowService SHALL set `AuthFlowState.ExpiresAt` to a configurable duration from now (default: 10 minutes).
7. WHEN the host resolver has identified a tenant, THE AuthFlowService SHALL store the resolved `TenantId` on the `AuthFlowState`.
8. WHEN a realm override is needed (enterprise tenant with `RealmName`), THE AuthFlowService SHALL store the realm in `AuthFlowState.Realm`.
9. THE AuthFlowService SHALL call `IAuthUrlBuilder` to construct the redirect URL and return both the URL and the persisted `AuthFlowState.Id`.

### Requirement 7: NewOrganizationFlowHandler

**User Story:** As a GroundUp framework developer, I want a flow handler that creates a new tenant and sets up the founding user with TenantAdmin access when a user completes the "register organization" flow, so that new organizations can be self-service provisioned atomically through the standard OAuth callback.

#### Acceptance Criteria

1. WHEN the NewOrganizationFlowHandler receives a callback, THE handler SHALL exchange the authorization code for tokens via `IIdentityProviderService.ExchangeCodeForTokensAsync` using the stored code_verifier and the stored redirect_uri.
2. WHEN code exchange succeeds, THE handler SHALL validate the `nonce` claim of the returned `id_token` against the nonce stored on the `AuthFlowState`, and IF the values do not match, THEN THE handler SHALL mark the flow as failed and return an error result.
3. WHEN nonce validation succeeds, THE handler SHALL retrieve user information via `IIdentityProviderService.GetUserInfoAsync`.
4. THE handler SHALL derive the tenant slug from the organization name stored on the `AuthFlowState`.
5. WHEN the derived slug collides with an existing tenant slug, THE handler SHALL deterministically disambiguate the slug (e.g., append a numeric suffix) so that two organizations with the same name do not conflict.
5a. THE handler SHALL rely on a database unique constraint on the tenant slug (see Requirement 16) as the authoritative guard against duplicate slugs, and IF a unique-constraint violation occurs on tenant insertion (a concurrent registration claimed the same slug), THEN THE handler SHALL re-disambiguate the slug and retry the insertion, so that concurrent registrations of the same organization name cannot both succeed with the same slug.
6. THE handler SHALL resolve or create the user record by matching on `ExternalUserId` (the Keycloak `sub` claim); email SHALL be stored as display/contact data only and SHALL NOT be used as the match key.
7. THE handler SHALL perform all entity creation for the flow — tenant (`TenantType=Standard`), user (when new), `UserTenant` membership, the tenant-scoped TenantAdmin role, and the founding user's TenantAdmin role assignment — atomically within a single transaction via `IUnitOfWork.ExecuteInTransactionAsync`, so that a partial failure leaves no orphaned data.
8. THE handler SHALL create the TenantAdmin role scoped to the new tenant (the role carries the new tenant's `TenantId`, `IsSystem=true`, name=`TenantAdmin`) and SHALL NOT create it as a global `RoleType=System` role.
9. THE handler SHALL assign the TenantAdmin role to the founding user within the new tenant (founding users always receive TenantAdmin to prevent lockout).
10. THE handler SHALL generate a GroundUp JWT token scoped to the new tenant via `ITokenService.GenerateTokenAsync`, including the original authentication timestamp claim (see Requirement 9).
11. THE handler SHALL write the authentication cookie via `IAuthCookieWriter.WriteAuthCookie`.
12. IF code exchange fails (returns null), THEN THE handler SHALL mark the flow as failed and return an error result.
13. IF userinfo retrieval fails (returns null), THEN THE handler SHALL mark the flow as failed and return an error result.

### Requirement 8: LoginFlowHandler

**User Story:** As a GroundUp framework developer, I want a login flow handler that resolves user memberships and handles single-tenant auto-select, multi-tenant picker, host-pinning, and default-tenant auto-join scenarios, so that the login experience adapts to the deployment topology and user state.

#### Acceptance Criteria

1. WHEN the LoginFlowHandler receives a callback, THE handler SHALL exchange the authorization code for tokens via `IIdentityProviderService.ExchangeCodeForTokensAsync` using the stored code_verifier and the stored redirect_uri.
2. WHEN code exchange succeeds, THE handler SHALL validate the `nonce` claim of the returned `id_token` against the nonce stored on the `AuthFlowState`, and IF the values do not match, THEN THE handler SHALL mark the flow as failed and return an error result.
3. WHEN nonce validation succeeds, THE handler SHALL retrieve user information via `IIdentityProviderService.GetUserInfoAsync`.
4. THE handler SHALL resolve or create the user record by matching on `ExternalUserId` (the Keycloak `sub` claim); email SHALL be used as display/contact data only and SHALL NOT be used as the match key.
5. THE handler SHALL query active memberships for the user via `IUserTenantRepository.GetAllMembershipsForUserAsync`.
6. WHEN the user has zero active memberships AND a default tenant is configured (setting `auth.application.default-tenant-slug` resolves to a valid tenant), THE handler SHALL auto-join the user to the default tenant, assign the role resolved from the `auth.application.default-role` cascade (tenant → application → system), and issue a token scoped to that tenant.
7. WHEN the user has zero active memberships AND no default tenant is configured, THE handler SHALL return an access-denied result.
8. WHEN the user has exactly one active membership AND no host-pinning is active, THE handler SHALL auto-select that tenant, issue a token scoped to it, and write the cookie.
9. WHEN the user has two or more active memberships AND no host-pinning is active, THE handler SHALL return a tenant-selection-required result whose tenant list includes only the user's active memberships in tenants where `RealmName` is null (shared-realm / standard tenants).
9a. WHEN the handler returns a tenant-selection-required result (Requirement 8.9), THE handler SHALL NOT issue a GroundUp token; instead THE handler SHALL retain the Keycloak token (already validated at callback) as the authentication cookie via `IAuthCookieWriter.WriteAuthCookie`, so the user stays authenticated as an identity-only principal (no `tid`) for the duration of tenant selection and can call `POST /auth/set-tenant` before any tenant is selected.
9b. THE retained Keycloak token SHALL grant NO tenant-scoped data access: because the Keycloak-token principal carries no `tid` claim, downstream repositories SHALL receive no tenant context, and the principal SHALL authenticate identity only.
10. THE handler SHALL exclude every tenant with a non-null `RealmName` from the tenant-selection list, because enterprise tenants are reached through their own subdomains/realms and are always tenant-pinned.
11. WHEN host-pinning is active (host resolver found a tenant), THE handler SHALL validate the user has an active membership in that specific tenant.
12. WHEN host-pinning is active AND the user is a member of the host-resolved tenant, THE handler SHALL issue a token scoped to that tenant and write the cookie (no picker shown).
13. WHEN host-pinning is active AND the user is NOT a member of the host-resolved tenant, THE handler SHALL return an access-denied result.
14. WHEN the host-resolved tenant has a non-null `RealmName` (enterprise tenant), THE handler SHALL detect this condition and return a "not yet implemented" result (enterprise flow handlers land in Phase 10E).
15. WHERE a role is supplied as an optional parameter on the originating user-creating or membership-creating request, THE handler SHALL assign that role at creation time, overriding the `auth.application.default-role` cascade.
16. THE handler SHALL set the `auth_time` claim to the current UTC time of the original authentication on every GroundUp token it issues — including the auto-join token (Requirement 8.6), the auto-select token (Requirement 8.8), and the host-pinned token (Requirement 8.12) — consistent with Requirement 7.10 and Requirements 9.7 and 9.8. THE pending-selection path (Requirement 8.9a) issues no GroundUp token and therefore sets no `auth_time` claim.

### Requirement 9: Token Refresh (Sliding Window with Absolute Cap)

**User Story:** As a GroundUp framework developer, I want app-side token refresh that reissues GroundUp tokens when they pass the halfway point of their lifetime, bounded by an absolute session lifetime, so that active sessions maintain continuous access without re-authentication while still expiring eventually.

#### Acceptance Criteria

1. THE token refresh path SHALL be implemented as a pure application-side JWT re-issue via `IAuthSessionService.RefreshTokenAsync`, the token refresh middleware (Requirement 13), and the `POST /auth/refresh` endpoint (Requirement 12).
2. THE token refresh path SHALL NOT involve a Keycloak round-trip, an authorization code, an `AuthFlowState`, or a `FlowCallbackContext`, and SHALL NOT be registered as an `IFlowHandler`.
3. THE `FlowType.TokenRefresh` enum value SHALL remain defined for completeness and audit purposes but SHALL have no dispatcher handler.
4. WHEN a refresh is triggered AND the current token has passed 50% of its `AuthOptions.TokenExpirationMinutes` lifetime AND the original authentication time is within `AuthOptions.AbsoluteSessionLifetimeMinutes`, THE refresh SHALL reissue a new token.
5. WHEN a refresh is triggered AND the current token has NOT passed 50% of its lifetime, THE refresh SHALL return the existing token unchanged (no rewrite needed).
6. WHEN a refresh is triggered AND the elapsed time since the original authentication time meets or exceeds `AuthOptions.AbsoluteSessionLifetimeMinutes`, THE refresh SHALL stop reissuing and SHALL require the user to re-authenticate through Keycloak (a normal login redirect).
7. EVERY GroundUp initial token issuance — NewOrganization (Requirement 7.10), Login auto-join (Requirement 8.6), Login auto-select (Requirement 8.8), Login host-pinned (Requirement 8.12), and the `set-tenant` selection token (Requirement 12.8) — SHALL set the `auth_time` claim to the current UTC time at original authentication, so that the refresh logic can compute elapsed time since the original login. (No GroundUp token is issued during pending tenant selection; see Requirement 8.9a.)
8. WHEN a token is reissued during refresh OR re-issued during a `set-tenant` re-selection where a prior GroundUp token already exists (e.g., switching tenants), THE reissue SHALL preserve the original `auth_time` claim value from the prior token (the absolute cap is measured from the original authentication, not from each reissue). WHEN `set-tenant` issues the FIRST GroundUp token of the session from a pending-selection Keycloak-token principal (Requirement 12.8), there is no prior GroundUp `auth_time` to preserve, so `auth_time` SHALL be set to the current UTC time.
9. BEFORE reissuing a token, THE refresh SHALL re-validate that the user still has an active membership in the current tenant via `IAuthSessionService.RefreshTokenAsync`.
10. IF membership re-validation fails (user removed from tenant or membership deactivated), THEN THE refresh SHALL clear the cookie and return an access-denied result.
11. WHEN a new token is issued, THE refresh SHALL rewrite the authentication cookie with the new token via `IAuthCookieWriter.WriteAuthCookie`.
12. THE `AuthOptions.AbsoluteSessionLifetimeMinutes` value SHALL be validated at application startup to be greater than 0 AND greater than or equal to `AuthOptions.TokenExpirationMinutes` (an absolute session cap smaller than a single token lifetime is invalid), and IF the value fails this validation, THEN startup SHALL fail fast via the existing `ValidateOnStart` options-validation pattern.

### Requirement 10: TenantAdmin Tenant-Scoped Role

**User Story:** As a GroundUp framework developer, I want a well-known, tenant-scoped TenantAdmin role that bypasses all permission checks within its own tenant, with the bypass logic centralized in `PermissionService`, so that tenant owners have full access without scattering privilege-escalation checks across the codebase and without leaking access across tenants.

#### Acceptance Criteria

1. THE `AuthRoleNames` class SHALL define a `TenantAdmin` constant with value "TenantAdmin".
2. THE TenantAdmin role SHALL be created once per tenant, scoped to that tenant (carrying the tenant's `TenantId`), and marked immutable via `IsSystem=true`.
3. THE TenantAdmin role SHALL NOT be created as a global `RoleType=System` role, and SHALL NOT be returned by `IUserRoleRepository.GetSystemRolesForUserAsync()` (which bypasses tenant filtering and is reserved for the global SuperAdmin role).
4. WHEN `PermissionService.HasPermissionAsync` or `HasAnyPermissionAsync` is called, THE PermissionService SHALL determine whether the user holds the TenantAdmin role in the current tenant using the tenant-scoped role query (never a cross-tenant query) and, when held, SHALL return true regardless of the specific permission being checked.
5. THE PermissionService SHALL short-circuit the TenantAdmin bypass BEFORE consulting the permission cache, and SHALL NOT cache the bypass as a concrete permission set, so that newly added permissions are automatically covered.
6. THE TenantAdmin bypass logic SHALL exist exclusively in `PermissionService` — no other service, controller, or middleware SHALL contain TenantAdmin-specific bypass logic.
7. THE TenantAdmin bypass SHALL NOT apply cross-tenant — a user with TenantAdmin in Tenant A SHALL NOT bypass permissions when operating in Tenant B.
8. WHEN removing a TenantAdmin assignment, THE role-assignment service SHALL enforce a last-admin guard: IF the user is the last active TenantAdmin in the tenant, THEN THE removal SHALL be rejected with a descriptive error.
9. THE last-admin guard SHALL be enforced in the role-assignment service (not in the repository or controller layer).

### Requirement 11: FlowType Enum Rename

**User Story:** As a GroundUp framework developer, I want to rename `FlowType.MultiTenantSelection` to `FlowType.Login` for clarity, so that the enum value reflects the actual flow semantics (login with membership resolution) rather than an implementation detail.

#### Acceptance Criteria

1. THE `FlowType` enum SHALL rename the value `MultiTenantSelection = 5` to `Login = 5` (same integer value, no breaking changes).
2. THE rename SHALL update all references in the codebase (service interfaces, handlers, tests, documentation).
3. THE integer value SHALL remain 5 to maintain database compatibility with any existing `AuthFlowState` rows.

### Requirement 12: AuthController Endpoints

**User Story:** As a GroundUp framework developer, I want an `AuthController` that exposes login, registration, callback, user info, tenant selection, refresh, and logout endpoints, so that consuming applications have a standard HTTP API for all authentication operations.

#### Acceptance Criteria

1. THE AuthController SHALL expose `GET /auth/login` that initiates the Login flow, creates an `AuthFlowState`, sets the state cookie, builds the Keycloak redirect URL, and returns an HTTP 302 redirect.
2. THE AuthController SHALL expose `GET /auth/register` that accepts the organization name as a parameter (Keycloak's registration page does not collect it), initiates the NewOrganization flow, stores the organization name on the `AuthFlowState`, sets the state cookie, builds the Keycloak redirect URL, and returns an HTTP 302 redirect.
3. THE AuthController SHALL expose `GET /auth/callback` with `code` and `state` query parameters that dispatches to the appropriate `IFlowHandler` via `IAuthFlowService`.
4. WHEN the callback flow handler returns a success with a token, THE AuthController SHALL redirect the user to the configured post-login URL (or a default path).
5. WHEN the callback flow handler returns a tenant-selection-required result, THE AuthController SHALL return the tenant list as JSON (HTTP 200) for the frontend picker to consume.
6. WHEN the callback flow handler returns an error, THE AuthController SHALL return the appropriate HTTP error status (400, 401, 403, or 410 for replay).
7. THE AuthController SHALL expose `GET /auth/me` that returns the current authenticated user's claims (userId, email, displayName, tenantId, roles) from the JWT. WHEN no authenticated user is present, THE endpoint SHALL return HTTP 401.
7a. WHEN the authenticated principal is a pending-selection Keycloak-token principal (a valid IdP-fallback principal with no `tid` claim, per Requirement 8.9a), THE `GET /auth/me` endpoint SHALL return HTTP 200 with the user identity derived from the external claims and a null/absent tenant, so the multi-membership picker can render before a tenant is selected.
8. THE AuthController SHALL expose `POST /auth/set-tenant` that accepts a `tenantId` body parameter and derives the user from the authenticated principal of the current token. WHEN the principal is a pending-selection Keycloak-token (IdP-fallback) principal, THE endpoint SHALL resolve the GroundUp user by the external `sub` claim via `IUserRepository.GetByExternalUserIdAsync`; otherwise THE endpoint SHALL derive the `userId` from the GroundUp principal. THE endpoint SHALL delegate to `IAuthSessionService.SetTenantAsync(userId, tenantId)` (which validates the user's membership in the requested tenant). WHEN successful AND a prior GroundUp token already exists (a tenant re-selection), THE endpoint SHALL issue a full tenant-scoped token that preserves the original `auth_time` claim (per Requirement 9.8); WHEN successful from a pending-selection Keycloak-token principal, THE endpoint SHALL issue the FIRST full tenant-scoped GroundUp token with `auth_time` set to the current UTC time (per Requirement 9.8). In both cases THE endpoint SHALL write the new token cookie and return HTTP 200. WHEN the user is not a member of the requested tenant, THE endpoint SHALL return HTTP 403.
9. THE AuthController SHALL expose `POST /auth/refresh` that triggers an explicit token refresh. WHEN refresh succeeds, THE endpoint SHALL write the new token cookie and return HTTP 200. WHEN the user is no longer a member, THE endpoint SHALL clear the cookie and return HTTP 403.
10. THE AuthController SHALL expose `POST /auth/logout` that clears the local authentication cookie via `IAuthCookieWriter.ClearAuthCookie` AND performs OIDC RP-initiated logout by calling Keycloak's `end_session_endpoint` so that the Keycloak SSO session is terminated, then returns HTTP 200. For Phase 10C, THE end-session call SHALL use the standard `client_id` plus `post_logout_redirect_uri` parameters (the GroundUp JWT does not carry the Keycloak `id_token`, and the `id_token` is not persisted in Phase 10C). THE realm used for end-session in Phase 10C SHALL be the shared realm.
11. THE Phase 10C logout SHALL NOT use `id_token_hint`-based logout. THE `id_token_hint`-based end-session flow, if adopted later, would require storing the Keycloak `id_token` server-side keyed by session, which is explicitly out of scope for Phase 10C.
12. THE new state-changing endpoints (`POST /auth/set-tenant`, `POST /auth/refresh`, `POST /auth/logout`) are cookie-authenticated and SHALL therefore be subject to the existing `CsrfProtectionMiddleware` (antiforgery validation on cookie-authenticated POST/PUT/DELETE requests).
13. THE authentication flow SHALL make an antiforgery (CSRF) token available to the client after authentication — for example via the callback success path or the `GET /auth/me` response — so that the client can supply the antiforgery token required to invoke `POST /auth/set-tenant`, `POST /auth/refresh`, and `POST /auth/logout`. The exact mechanism (e.g., a readable cookie plus request header) is a design decision; the intent is that the framework's own CSRF protection SHALL NOT deadlock these flows.
14. THE AuthController SHALL contain ZERO business logic — all logic is delegated to service layer interfaces.

### Requirement 13: Token Refresh Middleware (Sliding Expiration with Absolute Cap)

**User Story:** As a GroundUp framework developer, I want middleware that automatically refreshes tokens on authenticated requests when they pass the halfway point of their lifetime, bounded by the absolute session lifetime, so that active users maintain seamless sessions without explicit refresh calls while sessions still expire eventually.

#### Acceptance Criteria

1. THE TokenRefreshMiddleware SHALL run after `JwtAuthenticationMiddleware` (requires an authenticated user with claims).
2. WHEN an authenticated request arrives AND the token's issued-at timestamp indicates the token has passed 50% of `AuthOptions.TokenExpirationMinutes` AND the `auth_time` claim indicates the elapsed time since original authentication is within `AuthOptions.AbsoluteSessionLifetimeMinutes`, THE middleware SHALL trigger a token refresh.
3. WHEN the elapsed time since the `auth_time` claim meets or exceeds `AuthOptions.AbsoluteSessionLifetimeMinutes`, THE middleware SHALL NOT refresh the token (the user must re-authenticate through Keycloak via a normal login redirect).
4. WHEN the refresh succeeds, THE middleware SHALL rewrite the cookie with the new token and continue the request pipeline (the response includes the Set-Cookie header).
5. WHEN the refresh fails (membership revoked), THE middleware SHALL clear the cookie and continue the request pipeline (downstream authorization will reject the request naturally).
6. THE middleware SHALL NOT block the request — refresh is best-effort and the current token remains valid until its actual expiration.
7. THE middleware SHALL NOT refresh tokens on unauthenticated requests or requests with tokens that have not reached the halfway point.
8. WHEN the authenticated token has no `tid` claim (for example a pending-selection Keycloak-token principal per Requirement 8.9a, or any other tenant-less state), THE middleware SHALL skip token refresh.

### Requirement 14: Sample App Wiring

**User Story:** As a GroundUp framework developer, I want the sample application to demonstrate the complete auth flow (login, register, callback, me, set-tenant, refresh, logout), so that consuming developers have a working reference implementation.

#### Acceptance Criteria

1. THE GroundUp.Sample project SHALL register the auth module services and the auth middleware via the standard extension methods in its startup pipeline.
2. THE GroundUp.Sample project SHALL configure the auth endpoints via the standard `UseGroundUpAuth()` extension method (updated to include the new middleware).
3. THE GroundUp.Sample project SHALL configure a test domain in its development settings so that host-based resolution and cookie domain behavior can be verified locally.

### Requirement 15: Integration Tests

**User Story:** As a GroundUp framework developer, I want integration tests that validate the complete auth flows against a real Keycloak instance and real database, so that the dispatcher, handlers, cookie writer, host resolver, and security controls are verified end-to-end.

#### Acceptance Criteria

1. THE integration tests SHALL use Testcontainers Keycloak (same version as `docker-compose.yml`) and Testcontainers Postgres for a real database.
2. THE integration tests SHALL verify the NewOrganization flow end-to-end: initiate → redirect to Keycloak → simulate callback → verify tenant created, user created (matched by `sub`), tenant-scoped TenantAdmin assigned, all entities created atomically, and cookie written.
3. THE integration tests SHALL verify the Login flow with a single membership: initiate → callback → verify auto-select, token issued, cookie written.
4. THE integration tests SHALL verify the Login flow with multiple memberships: initiate → callback → verify tenant-selection-required response returned, AND verify the tenant list contains only shared-realm (standard, `RealmName` null) tenants and excludes any enterprise tenant the user is a member of.
5. THE integration tests SHALL verify host-pinned login: request from `acme.{domain}` → callback → verify membership validated for the "acme" tenant specifically.
6. THE integration tests SHALL verify the enterprise routing stub: a host-resolved enterprise tenant (`RealmName` set) returns a "not yet implemented" result in Phase 10C.
7. THE integration tests SHALL verify the cross-subdomain cookie: when `auth.application.default-domain` is set, the cookie Domain attribute is `.{domain}`.
8. THE integration tests SHALL verify replay attack protection: consuming an `AuthFlowState` a second time returns HTTP 410 Gone.
9. THE integration tests SHALL verify state cookie binding: a callback whose `state` query parameter does not match the HttpOnly state cookie is rejected.
10. THE integration tests SHALL verify OIDC nonce validation: a callback whose `id_token` nonce does not match the stored nonce is rejected.
11. THE integration tests SHALL verify sliding token refresh: issue a token, advance time past 50%, trigger refresh, verify a new cookie is written with a new token.
12. THE integration tests SHALL verify the absolute session cap: a session whose `auth_time` is past `AuthOptions.AbsoluteSessionLifetimeMinutes` is not refreshed and requires re-authentication.
13. THE integration tests SHALL verify the last-admin guard: attempting to remove the sole TenantAdmin from a tenant is rejected.
14. THE integration tests SHALL verify the TenantAdmin permission bypass: a user with the TenantAdmin role returns true for any permission check within that tenant, and does not bypass permissions in a different tenant.
15. THE integration tests SHALL verify the pending-selection flow: a user with two or more eligible memberships stays authenticated via the retained Keycloak token (no GroundUp token, no `tid` claim), `GET /auth/me` succeeds returning user identity with no tenant, and a subsequent `POST /auth/set-tenant` for a tenant the user belongs to resolves the GroundUp user by the external `sub` claim, issues the first full tenant-scoped GroundUp token, and writes a full tenant-scoped cookie.
16. THE integration tests SHALL verify host/JWT reconciliation for a standard tenant: when the host-resolved tenant is a standard (shared-realm, `RealmName` null) tenant the authenticated user is a member of and it differs from the token `tid`, the user can switch tenant context via the tenant-selection / `set-tenant` path without re-authenticating against Keycloak.
17. THE integration tests SHALL verify host/JWT reconciliation for an enterprise tenant: when the host-resolved tenant is an enterprise tenant (`RealmName` set) and differs from the token `tid`, the system requires a fresh login pinned to that tenant's realm.
18. THE integration tests SHALL verify host/JWT reconciliation for a non-member: when the host-resolved tenant differs from the token `tid` and the user is not a member of the host-resolved tenant, the system returns an access-denied result.

### Requirement 16: AuthFlowState Schema Additions and Migration

**User Story:** As a GroundUp framework developer, I want the `AuthFlowState` entity extended with the fields required by the corrected security model and the NewOrganization flow, so that state-token lookup, PKCE, redirect_uri reuse, and organization capture are durably persisted.

#### Acceptance Criteria

1. THE `AuthFlowState` entity SHALL add a `StateToken` field holding the random OAuth state value, and the field SHALL be indexed to support lookup by state token at callback.
2. THE `AuthFlowState` entity SHALL add a `CodeVerifier` field holding the PKCE code_verifier used at code exchange.
3. THE `AuthFlowState` entity SHALL add a `RedirectUri` field holding the exact redirect_uri used at authorize time, so the identical value is reused at code exchange (OAuth requires the two to match).
4. THE `AuthFlowState` entity SHALL add an `OrganizationName` field (a general-purpose display-name/metadata field) capturing the organization name for the NewOrganization flow at registration time.
5. THE corresponding DTO and the `InitiateAuthFlowRequest` SHALL be extended to carry the new fields where applicable.
6. THE schema changes SHALL be delivered as a Postgres EF Core migration in `GroundUp.Auth.Data.Postgres`, with entity configuration using the Fluent API (not data annotations).
7. THE tenant schema SHALL enforce a database unique constraint on the tenant slug (delivered via the EF Core migration and Fluent API configuration), so that concurrent registrations cannot persist two tenants with the same slug; this constraint is the authoritative backing for the slug disambiguation retry in Requirement 7.5a.

### Requirement 17: Framework DI Registration

**User Story:** As a GroundUp framework developer, I want all new framework components registered in the auth module's DI extension and middleware pipeline, so that consuming applications obtain the complete auth slice through the standard `AddGroundUpAuth` and `UseGroundUpAuth` entry points.

#### Acceptance Criteria

1. THE `AuthServiceCollectionExtensions.AddGroundUpAuth` extension SHALL register the `IAuthCookieWriter` implementation.
2. THE `AddGroundUpAuth` extension SHALL register `IHostTenantResolver` and the scoped `HostResolvedTenant` service.
3. THE `AddGroundUpAuth` extension SHALL register `IAuthUrlBuilder` and `IAuthFlowService`.
4. THE `AddGroundUpAuth` extension SHALL register the callback flow handlers (`NewOrganizationFlowHandler` and `LoginFlowHandler`) as `IFlowHandler` implementations.
5. THE `UseGroundUpAuth` middleware pipeline SHALL register `HostTenantResolutionMiddleware` before `JwtAuthenticationMiddleware`.
6. THE `UseGroundUpAuth` middleware pipeline SHALL register `TokenRefreshMiddleware` after `JwtAuthenticationMiddleware`.
7. THE registrations in this requirement SHALL be framework-level registration, distinct from the sample-app wiring in Requirement 14.

### Requirement 18: Host/Token Tenant Reconciliation

**User Story:** As a GroundUp framework developer, I want authenticated requests where the host-resolved tenant differs from the JWT `tid` to be denied access to the host tenant's data until the user deliberately switches tenant context, so that no cross-tenant data access occurs silently — even when the user is a member of both tenants.

#### Acceptance Criteria

1. WHEN an authenticated request has a host-resolved tenant (`HostResolvedTenant.Tenant` is non-null) AND the JWT `tid` claim does not match that host-resolved tenant's id, THE system SHALL deny access to the host tenant's resources — the request SHALL NOT be served using either the token's tenant context or the host-resolved tenant's context. The user's existing token does NOT grant access to the host-resolved tenant's data, regardless of whether the user is a member of that tenant.
2. WHEN the mismatched host-resolved tenant is a standard tenant (shared-realm, `RealmName` null) AND the user is an active member of that tenant, THE system SHALL indicate that a deliberate tenant switch is required (e.g., redirect to the tenant-selection screen or return a response directing the client to call `POST /auth/set-tenant`). The user SHALL NOT need to re-authenticate against Keycloak because both tenants share the same realm — a deliberate `set-tenant` call re-issues a token scoped to the new tenant, which is sufficient.
3. WHEN the mismatched host-resolved tenant is an enterprise tenant (`RealmName` set), THE system SHALL require a fresh login pinned to that tenant's realm (a new authentication), as if the user were reaching that subdomain unauthenticated.
4. IF the user is NOT an active member of the mismatched host-resolved tenant, THEN THE system SHALL return an access-denied result.
5. THE reconciliation SHALL compare the host-resolved tenant (`HostResolvedTenant`, populated by `HostTenantResolutionMiddleware`) against the JWT-derived tenant context (`TenantContext`, populated by `JwtTenantResolutionMiddleware` from the `tid` claim); the exact enforcement point in the pipeline is a design decision and SHALL remain at the behavioral level in this requirement.
