# Requirements Document

## Introduction

Phase 10B delivers the concrete Keycloak implementation of both identity provider interfaces defined in Phase 10A (`IIdentityProviderService` and `IIdentityProviderAdminService`). It is self-contained — no flow logic, no controllers, no auth flow orchestration. The output is a new project `GroundUp.Auth.Keycloak` that consuming applications opt into via `AddGroundUpAuthKeycloak()`.

The implementation:
- Exchanges authorization codes for tokens via Keycloak's token endpoint
- Validates tokens and retrieves user info via Keycloak's userinfo endpoint
- Performs realm CRUD, client CRUD, and user provisioning via the Keycloak Admin REST API
- Extracts roles from `resource_access` claims in Keycloak tokens
- Resolves all configuration from the settings database (never from appsettings.json) via `IOptionsMonitor<KeycloakOptions>` initialized at startup with cache invalidation on settings change
- Generates admin console deep-link URLs for standard and enterprise tenants

This phase depends on Phase 10A (interfaces, DTOs, docker-compose Keycloak) and Phase 10AB (encrypted settings substrate for the admin client secret). It is referenced from the Sample app (for integration testing) but NOT from `GroundUp.Auth.Services` — keeping the Keycloak dependency opt-in and swappable.

## Glossary

- **Keycloak_Provider**: The `GroundUp.Auth.Keycloak` project — the concrete Keycloak implementation of the identity provider interfaces.
- **KeycloakOptions**: A POCO resolved from `ISettingsService` at startup and refreshed on settings change via `IOptionsMonitor<KeycloakOptions>`. Contains the five Keycloak-related settings keys.
- **KeycloakIdentityProviderService**: The implementation of `IIdentityProviderService` — handles code exchange, token validation, and userinfo retrieval.
- **KeycloakIdentityProviderAdminService**: The implementation of `IIdentityProviderAdminService` — handles realm CRUD, client CRUD, and user provisioning via the Keycloak Admin REST API.
- **KeycloakAdminLinkBuilder**: A service that constructs URLs to the Keycloak admin console for a given realm, useful for deep-linking from the consuming application's admin UI.
- **Admin_Token**: A short-lived access token obtained via `client_credentials` grant using the admin-client-id and admin-client-secret from settings. Used to authenticate Admin REST API calls.
- **Token_Endpoint**: `{base-url}/realms/{realm}/protocol/openid-connect/token` — the Keycloak endpoint for exchanging codes and obtaining tokens.
- **Userinfo_Endpoint**: `{base-url}/realms/{realm}/protocol/openid-connect/userinfo` — the Keycloak endpoint for retrieving authenticated user claims.
- **Admin_REST_API**: `{internal-base-url}/admin/realms/{realm}` — the Keycloak admin API root for realm-scoped management operations.
- **Resource_Access_Claim**: The `resource_access` claim in a Keycloak-issued token that maps client IDs to arrays of roles granted to the user for that client.
- **Polly**: A .NET resilience library used for retry policies on HTTP calls to Keycloak.
- **Testcontainers_Keycloak**: An ephemeral Keycloak Docker container managed by Testcontainers for integration testing.

## Requirements

### Requirement 1: Project Structure and Dependencies

**User Story:** As a GroundUp framework developer, I want a new `GroundUp.Auth.Keycloak` project that follows the existing provider-specific project pattern (like `GroundUp.Data.Postgres`), so that the Keycloak dependency is opt-in and swappable without affecting the core auth module.

#### Acceptance Criteria

1. THE Keycloak_Provider SHALL be a class library project at `src/GroundUp.Auth.Keycloak/GroundUp.Auth.Keycloak.csproj` targeting net8.0.
2. THE Keycloak_Provider SHALL reference `GroundUp.Auth.Core` (for `IIdentityProviderAdminService`, DTOs, and entities), `GroundUp.Auth.Services` (for `IIdentityProviderService`), and `GroundUp.Core` (for `ISettingsService`, `OperationResult`, and shared abstractions).
3. THE Keycloak_Provider SHALL NOT reference `GroundUp.Data.Postgres`, `GroundUp.Repositories`, or any EF Core packages — it is a pure HTTP-client project.
4. THE Keycloak_Provider SHALL depend on `Microsoft.Extensions.Http.Polly` (or equivalent Polly integration package) for resilient HTTP calls.
5. THE Keycloak_Provider SHALL depend on `Microsoft.Extensions.Options` for `IOptionsMonitor<KeycloakOptions>`.
6. THE Keycloak_Provider SHALL be added to the `groundup.sln` solution file.
7. THE Keycloak_Provider SHALL enable nullable reference types.

### Requirement 2: KeycloakOptions Configuration from Settings

**User Story:** As a GroundUp framework developer, I want `KeycloakOptions` resolved from the settings database (not appsettings.json) via `IOptionsMonitor<KeycloakOptions>` with startup initialization and cache invalidation, so that all Keycloak configuration is managed through the same operational settings system as everything else and changes propagate without restart.

#### Acceptance Criteria

1. THE Keycloak_Provider SHALL define a `KeycloakOptions` class with the following properties: `PublicBaseUrl` (string), `SharedRealmName` (string), `InternalBaseUrl` (string), `AdminClientId` (string), `AdminClientSecret` (string), `AppClientId` (string).
2. THE KeycloakOptions SHALL be resolved from the following settings keys: `auth.keycloak.public-base-url`, `auth.keycloak.shared-realm-name`, `auth.keycloak.internal-base-url`, `auth.keycloak.admin-client-id`, `auth.keycloak.admin-client-secret`, `auth.keycloak.app-client-id`.
3. WHEN the application starts, THE Keycloak_Provider SHALL read all five settings from `ISettingsService` and populate the initial `KeycloakOptions` instance before any HTTP request is served.
4. WHEN any of the five Keycloak settings changes at runtime (detected via the existing `SettingChangedEvent` or equivalent cache invalidation mechanism), THE Keycloak_Provider SHALL refresh the `KeycloakOptions` instance so that subsequent calls use the updated values.
5. THE KeycloakOptions SHALL be exposed via `IOptionsMonitor<KeycloakOptions>` so that consuming code accesses `.CurrentValue` to always get the latest configuration. IF `.CurrentValue` is accessed before startup initialization completes, THE Keycloak_Provider SHALL throw an `InvalidOperationException` indicating that initialization is still in progress.
6. THE `AdminClientSecret` setting SHALL be stored encrypted at rest (`IsSecret=true`, `IsEncrypted=true`) per the Phase 10AB substrate. The Keycloak provider reads the decrypted value transparently via `ISettingsService`. IF the encryption system is unavailable during startup, THE application SHALL refuse to start.

### Requirement 3: Startup Validation — Fail Fast on Missing Configuration

**User Story:** As a consuming app developer, I want the application to fail fast at startup if any required Keycloak setting is missing or empty, so that misconfiguration is caught immediately rather than discovered at first HTTP call.

#### Acceptance Criteria

1. WHEN the application starts AND any of `PublicBaseUrl`, `SharedRealmName`, `InternalBaseUrl`, `AdminClientId`, `AdminClientSecret`, or `AppClientId` resolves to null, empty, or whitespace, THE Keycloak_Provider SHALL throw a descriptive exception during host startup naming the specific missing setting key(s).
2. WHEN `PublicBaseUrl` or `InternalBaseUrl` resolves to a value that is not a valid absolute URL with http or https scheme, THE Keycloak_Provider SHALL throw a descriptive exception during host startup naming the invalid setting key and the invalid value (without exposing the AdminClientSecret).
3. THE startup validation SHALL run after `ISettingsService` is available (i.e., after database connectivity is established and settings are loaded) but before the application begins accepting HTTP traffic.
4. IF `BootstrapState.IsComplete` is `false` (the application is in setup mode) AND the Keycloak settings are not yet populated (resolve to null/empty), THE startup validation SHALL be skipped — the settings will not exist yet during the setup wizard flow. IF the settings happen to be available during setup mode (e.g., from a previous installation), validation SHALL run normally.

### Requirement 4: DI Registration Extension Method

**User Story:** As a consuming app developer, I want a single `AddGroundUpAuthKeycloak()` extension method that registers the Keycloak provider's services, HTTP clients, and options, so that wiring up is simple and follows established GroundUp conventions.

#### Acceptance Criteria

1. THE Keycloak_Provider SHALL expose `AddGroundUpAuthKeycloak(this IServiceCollection services)` in a `KeycloakServiceCollectionExtensions` class under the `Microsoft.Extensions.DependencyInjection` namespace.
2. WHEN `AddGroundUpAuthKeycloak()` is called, THE extension SHALL register `KeycloakIdentityProviderService` as the implementation of `IIdentityProviderService`.
3. WHEN `AddGroundUpAuthKeycloak()` is called, THE extension SHALL register `KeycloakIdentityProviderAdminService` as the implementation of `IIdentityProviderAdminService`.
4. WHEN `AddGroundUpAuthKeycloak()` is called, THE extension SHALL register `KeycloakAdminLinkBuilder` as a singleton.
5. WHEN `AddGroundUpAuthKeycloak()` is called, THE extension SHALL register a named `HttpClient` for the identity-provider HTTP calls with Polly retry policies scoped only to this named client (retry on 5xx and transient network failures, exponential backoff, maximum 3 retries).
6. WHEN `AddGroundUpAuthKeycloak()` is called, THE extension SHALL register a separate named `HttpClient` for the admin REST API calls with Polly retry policies scoped only to this named client (same policy as above).
7. WHEN `AddGroundUpAuthKeycloak()` is called, THE extension SHALL configure `IOptionsMonitor<KeycloakOptions>` populated from `ISettingsService` at startup with change-notification support.

### Requirement 5: IIdentityProviderService — Authorization Code Exchange

**User Story:** As a GroundUp framework developer, I want the Keycloak provider to exchange an OAuth2 authorization code for tokens via Keycloak's token endpoint, so that auth flow handlers in Phase 10C can complete the login flow after receiving the callback.

#### Acceptance Criteria

1. WHEN `ExchangeCodeForTokensAsync` is called with a valid authorization code, redirect URI, and optional realm, THE KeycloakIdentityProviderService SHALL POST to `{InternalBaseUrl}/realms/{realm}/protocol/openid-connect/token` with grant_type=authorization_code, the code, and the redirect_uri.
2. WHERE the `realm` parameter is null, THE KeycloakIdentityProviderService SHALL use `KeycloakOptions.SharedRealmName` as the realm.
3. WHEN Keycloak responds with HTTP 200 and a valid token response body, THE KeycloakIdentityProviderService SHALL map the response to a `TokenResponseDto` containing `AccessToken`, `RefreshToken`, `ExpiresIn`, and `IdToken`. IF the HTTP 200 response contains malformed JSON or missing required fields, THE KeycloakIdentityProviderService SHALL throw an exception (this indicates a Keycloak version incompatibility or implementation bug, not a recoverable error).
4. WHEN Keycloak responds with a non-success HTTP status (e.g., 400 for invalid code, 401 for bad credentials), THE KeycloakIdentityProviderService SHALL return null (per the interface contract).
5. THE code exchange request SHALL include the `client_id` parameter. Phase 10B SHALL add a `string? clientId = null` parameter to the `IIdentityProviderService.ExchangeCodeForTokensAsync` interface method. When null, the implementation defaults to a configured app client ID from `KeycloakOptions` (new property: `AppClientId`, resolved from setting `auth.keycloak.app-client-id`).
6. Phase 10B SHALL add a `string? codeVerifier = null` parameter to the `IIdentityProviderService.ExchangeCodeForTokensAsync` interface method. WHEN the `codeVerifier` is non-null, THE request SHALL include the PKCE `code_verifier` parameter. WHEN the `codeVerifier` is null, THE request SHALL NOT include the `code_verifier` (non-PKCE flow).

### Requirement 6: IIdentityProviderService — Token Validation

**User Story:** As a GroundUp framework developer, I want the Keycloak provider to validate tokens issued by Keycloak (checking signature, expiration, and issuer), so that auth middleware can trust tokens presented by clients.

#### Acceptance Criteria

1. WHEN `ValidateTokenAsync` is called with a token string, THE KeycloakIdentityProviderService SHALL validate the JWT signature against Keycloak's JWKS endpoint (`{InternalBaseUrl}/realms/{realm}/protocol/openid-connect/certs`).
2. THE KeycloakIdentityProviderService SHALL cache the JWKS keys in memory and refresh them when a token presents a `kid` not found in the current cache.
3. WHEN the token is expired (`exp` claim in the past), THE KeycloakIdentityProviderService SHALL return false.
4. WHEN the token issuer (`iss` claim) does not match the expected Keycloak realm issuer URL, THE KeycloakIdentityProviderService SHALL return false.
5. WHEN the token signature is invalid or the token is malformed, THE KeycloakIdentityProviderService SHALL return false without throwing.
6. WHEN the token is valid (signature, expiration, and issuer all pass), THE KeycloakIdentityProviderService SHALL return true.

### Requirement 7: IIdentityProviderService — User Info Retrieval

**User Story:** As a GroundUp framework developer, I want the Keycloak provider to retrieve user information from Keycloak's userinfo endpoint, so that auth flow handlers can obtain the user's profile claims after a successful login.

#### Acceptance Criteria

1. WHEN `GetUserInfoAsync` is called with a valid access token, THE KeycloakIdentityProviderService SHALL GET from `{InternalBaseUrl}/realms/{realm}/protocol/openid-connect/userinfo` with the access token in the Authorization: Bearer header.
2. WHEN Keycloak responds with HTTP 200, THE KeycloakIdentityProviderService SHALL map the response to an `ExternalUserInfo` record containing `ExternalUserId` (from the `sub` claim), `Email`, `DisplayName` (from `name` or `preferred_username`), and `Attributes` (remaining claims as key-value pairs).
3. WHEN Keycloak responds with HTTP 401 (invalid or expired token), THE KeycloakIdentityProviderService SHALL return null.
4. WHEN Keycloak responds with any other non-success status, THE KeycloakIdentityProviderService SHALL return null and log the failure at Warning level.
5. THE realm used for the userinfo call SHALL default to `KeycloakOptions.SharedRealmName` unless a realm-specific overload is provided.

### Requirement 8: IIdentityProviderAdminService — Admin Token Acquisition

**User Story:** As a GroundUp framework developer, I want the Keycloak admin service to acquire short-lived admin tokens via `client_credentials` grant using the configured admin-client-id/secret, so that all admin API calls are authenticated without storing long-lived credentials in memory.

#### Acceptance Criteria

1. WHEN any admin operation is invoked, THE KeycloakIdentityProviderAdminService SHALL acquire an admin access token by POSTing to `{InternalBaseUrl}/realms/{SharedRealmName}/protocol/openid-connect/token` with grant_type=client_credentials, client_id=AdminClientId, and client_secret=AdminClientSecret.
2. THE KeycloakIdentityProviderAdminService SHALL cache the admin token in memory and reuse it for subsequent calls until it expires (based on the `expires_in` value minus a safety margin of 30 seconds).
3. WHEN the cached admin token has expired or is at or within the safety margin of expiration, THE KeycloakIdentityProviderAdminService SHALL acquire a fresh token before making the admin API call.
4. WHEN token acquisition fails (Keycloak returns non-success), THE KeycloakIdentityProviderAdminService SHALL return a failure `OperationResult` with a descriptive error message (without exposing the client secret in logs or error messages). WHEN token acquisition succeeds but the subsequent admin API call fails, THE KeycloakIdentityProviderAdminService SHALL also return a failure `OperationResult`.
5. WHEN `KeycloakOptions` changes at runtime (e.g., secret rotation), THE KeycloakIdentityProviderAdminService SHALL invalidate the cached admin token so the next call acquires a fresh token with the new credentials.

### Requirement 9: IIdentityProviderAdminService — Realm CRUD

**User Story:** As a GroundUp framework developer, I want the Keycloak admin service to implement realm CRUD operations against the Keycloak Admin REST API, so that Phase 10E can create and manage per-tenant enterprise realms.

#### Acceptance Criteria

1. WHEN `CreateRealmAsync` is called, THE KeycloakIdentityProviderAdminService SHALL POST to `{InternalBaseUrl}/admin/realms` with the realm representation and return an `OperationResult<RealmDto>` with the created realm.
2. WHEN `GetRealmAsync` is called, THE KeycloakIdentityProviderAdminService SHALL GET from `{InternalBaseUrl}/admin/realms/{realmName}` and map the response to a `RealmDto`.
3. WHEN `GetRealmAsync` is called AND Keycloak returns HTTP 404, THE KeycloakIdentityProviderAdminService SHALL return `OperationResult.NotFound`.
4. WHEN `UpdateRealmAsync` is called, THE KeycloakIdentityProviderAdminService SHALL PUT to `{InternalBaseUrl}/admin/realms/{realmName}` with the updated fields and return the updated `RealmDto`.
5. WHEN `DeleteRealmAsync` is called, THE KeycloakIdentityProviderAdminService SHALL DELETE `{InternalBaseUrl}/admin/realms/{realmName}` and return a success `OperationResult`.
6. WHEN `DeleteRealmAsync` is called AND Keycloak returns HTTP 404, THE KeycloakIdentityProviderAdminService SHALL return `OperationResult.NotFound`.
7. WHEN any realm operation encounters an unexpected HTTP error (5xx, network timeout after retries), THE KeycloakIdentityProviderAdminService SHALL return a failure `OperationResult` with the HTTP status and a descriptive message.

### Requirement 10: IIdentityProviderAdminService — Client CRUD

**User Story:** As a GroundUp framework developer, I want the Keycloak admin service to implement client CRUD operations scoped to a realm, so that Phase 10E can create per-tenant OAuth clients and Phase 10C can manage the shared-realm client configuration.

#### Acceptance Criteria

1. WHEN `CreateClientAsync` is called, THE KeycloakIdentityProviderAdminService SHALL POST to `{InternalBaseUrl}/admin/realms/{realmName}/clients` with the client representation (clientId, redirectUris, PKCE setting, publicClient=false for confidential clients).
2. WHEN Keycloak returns HTTP 201 for client creation, THE KeycloakIdentityProviderAdminService SHALL retrieve the created client by clientId to obtain the full representation including the generated secret, and return an `OperationResult<IdentityProviderClientDto>`.
3. WHEN `GetClientAsync` is called, THE KeycloakIdentityProviderAdminService SHALL GET from `{InternalBaseUrl}/admin/realms/{realmName}/clients?clientId={clientId}` and return the matching client as an `IdentityProviderClientDto`.
4. WHEN `GetClientAsync` is called AND no client with the specified clientId exists, THE KeycloakIdentityProviderAdminService SHALL return `OperationResult.NotFound`.
5. WHEN `UpdateClientAsync` is called, THE KeycloakIdentityProviderAdminService SHALL PUT to `{InternalBaseUrl}/admin/realms/{realmName}/clients/{internalId}` with the updated fields and return the updated `IdentityProviderClientDto`.
6. WHEN `DeleteClientAsync` is called, THE KeycloakIdentityProviderAdminService SHALL DELETE `{InternalBaseUrl}/admin/realms/{realmName}/clients/{internalId}` and return a success `OperationResult`.
7. WHEN `CreateClientAsync` is called AND a client with the same clientId already exists in the realm, THE KeycloakIdentityProviderAdminService SHALL return a conflict-style failure `OperationResult`.

### Requirement 11: IIdentityProviderAdminService — User Provisioning

**User Story:** As a GroundUp framework developer, I want the Keycloak admin service to implement user provisioning operations, so that auth flows (invitations, first-admin signup) can create Keycloak accounts before the user logs in.

#### Acceptance Criteria

1. WHEN `ProvisionUserAsync` is called, THE KeycloakIdentityProviderAdminService SHALL POST to `{InternalBaseUrl}/admin/realms/{realmName}/users` with the user representation (email as username, email, firstName/lastName derived from DisplayName, enabled=true).
2. WHEN Keycloak returns HTTP 201 for user creation, THE KeycloakIdentityProviderAdminService SHALL extract the user ID from the Location header and return an `OperationResult<ProvisionedUserDto>`.
3. WHEN the `ProvisionUserRequest` includes `InitialPassword` with a non-null value, THE KeycloakIdentityProviderAdminService SHALL set the user's password via PUT to `{InternalBaseUrl}/admin/realms/{realmName}/users/{userId}/reset-password` with `temporary` set to the value of `RequirePasswordReset`.
4. WHEN the `ProvisionUserRequest` specifies `RequirePasswordReset=true` AND no `InitialPassword` is provided, THE KeycloakIdentityProviderAdminService SHALL create the user with a required-action of `UPDATE_PASSWORD` so Keycloak prompts on first login.
5. WHEN `ProvisionUserAsync` is called AND a user with the same email already exists in the realm, THE KeycloakIdentityProviderAdminService SHALL return a conflict-style failure `OperationResult` (Keycloak returns HTTP 409).
6. WHEN `SetUserCredentialsAsync` is called, THE KeycloakIdentityProviderAdminService SHALL PUT to `{InternalBaseUrl}/admin/realms/{realmName}/users/{externalUserId}/reset-password` with the credentials DTO.
7. WHEN `DeleteUserAsync` is called, THE KeycloakIdentityProviderAdminService SHALL DELETE `{InternalBaseUrl}/admin/realms/{realmName}/users/{externalUserId}` and return a success `OperationResult`.
8. WHEN `DeleteUserAsync` is called AND Keycloak returns HTTP 404, THE KeycloakIdentityProviderAdminService SHALL return `OperationResult.NotFound`.

### Requirement 12: Role Extraction from Resource Access Claims

**User Story:** As a GroundUp framework developer, I want the Keycloak provider to extract roles from the `resource_access` claim in Keycloak tokens, so that the permission system can map external Keycloak roles to internal GroundUp permissions.

#### Acceptance Criteria

1. THE Keycloak_Provider SHALL expose a method (or utility class) that extracts roles for a given client ID from the `resource_access` claim in a Keycloak JWT.
2. WHEN the `resource_access` claim is present AND contains an entry for the specified client ID, THE extractor SHALL return the list of role strings from the `roles` array within that client entry.
3. WHEN the `resource_access` claim is missing OR does not contain an entry for the specified client ID, THE extractor SHALL return an empty list (not null).
4. THE role extraction SHALL work on both access tokens and ID tokens that contain the `resource_access` claim.
5. THE role extraction SHALL handle malformed `resource_access` claims gracefully by returning an empty list and logging at Debug level.

### Requirement 13: KeycloakAdminLinkBuilder — Admin Console Deep Links

**User Story:** As a consuming app developer, I want a service that builds correct admin console URLs for navigating directly to Keycloak's admin UI, so that application administrators can deep-link to realm, client, or user management pages without constructing URLs manually.

#### Acceptance Criteria

1. THE KeycloakAdminLinkBuilder SHALL expose methods to build URLs for: realm overview, client list, client detail, user list, user detail, and identity provider configuration page.
2. THE KeycloakAdminLinkBuilder SHALL use `KeycloakOptions.PublicBaseUrl` as the base for all generated URLs (since the admin console is accessed from the browser, not from internal services).
3. WHEN building a URL for a standard tenant (shared realm), THE KeycloakAdminLinkBuilder SHALL use `KeycloakOptions.SharedRealmName` as the realm in the URL path.
4. WHEN building a URL for an enterprise tenant (per-tenant realm), THE KeycloakAdminLinkBuilder SHALL accept the enterprise realm name as a parameter and use it in the URL path.
5. THE generated URLs SHALL follow the Keycloak admin console URL pattern: `{PublicBaseUrl}/admin/{realmName}/console/` with appropriate sub-paths for clients, users, and identity providers.
6. THE KeycloakAdminLinkBuilder SHALL be registered as a singleton since it is stateless and only reads from `IOptionsMonitor<KeycloakOptions>`.

### Requirement 14: HTTP Client Resilience with Polly

**User Story:** As a GroundUp framework developer, I want all HTTP calls to Keycloak to use Polly retry policies, so that transient network failures and brief Keycloak unavailability do not immediately fail operations.

#### Acceptance Criteria

1. THE Keycloak_Provider SHALL configure Polly retry policies on all named HttpClients registered via `AddGroundUpAuthKeycloak()`.
2. THE retry policy SHALL retry on HTTP 5xx responses and transient network exceptions (connection refused, timeout).
3. THE retry policy SHALL NOT retry on HTTP 4xx responses (these are client errors that will not succeed on retry). IF a 4xx response occurs during an already-initiated retry sequence for a prior transient error, the retry sequence SHALL continue for the original transient error.
4. THE retry policy SHALL use exponential backoff with jitter: base delay of 1 second, maximum 3 retry attempts.
5. THE retry policy SHALL log each retry attempt at Warning level with the attempt number, delay, and the HTTP status or exception that triggered the retry.
6. WHEN all retry attempts are exhausted, THE Keycloak_Provider SHALL return a failure `OperationResult` (for admin operations) or null/false (for identity-provider operations per their interface contracts) — never throw an unhandled exception to callers. THE system MAY return failure before exhausting all retries if it determines further attempts will not succeed (e.g., Keycloak is returning a consistent non-transient error).

### Requirement 15: Integration Tests with Testcontainers Keycloak

**User Story:** As a GroundUp framework developer, I want integration tests that run against a real ephemeral Keycloak instance managed by Testcontainers, so that the provider implementation is validated against actual Keycloak behavior rather than mocked HTTP responses.

#### Acceptance Criteria

1. THE integration test project SHALL use a shared Testcontainers Keycloak fixture that starts a Keycloak container once per test class collection, pre-imports the `groundup` realm from `keycloak/realm.json`, and tears down after all tests complete.
2. THE integration tests SHALL verify that `ExchangeCodeForTokensAsync` successfully exchanges a valid authorization code for tokens against the running Keycloak instance (using Keycloak's direct-access grant to simulate the code exchange in a test-friendly way, or by performing the full flow via HTTP).
3. THE integration tests SHALL verify realm CRUD round-trips: create a realm, get it, update its display name, verify the update, then delete it and verify it is gone.
4. THE integration tests SHALL verify client CRUD: create a client in the test realm, get it, verify redirect URIs and PKCE settings, update it, then delete it.
5. THE integration tests SHALL verify user provisioning: provision a user with an initial password and `RequirePasswordReset=true`, verify the user exists in Keycloak, set new credentials, then delete the user.
6. THE integration tests SHALL verify role extraction: create a user, assign roles in the test realm, obtain a token for that user, and verify that `resource_access` role extraction returns the expected roles.
7. THE integration tests SHALL verify `KeycloakAdminLinkBuilder` returns correctly formed URLs.
8. THE integration tests SHALL verify startup validation: confirm that the provider throws on missing or invalid settings.
9. THE Testcontainers fixture SHALL use the same Keycloak image version pinned in `docker-compose.yml` (26.x).

