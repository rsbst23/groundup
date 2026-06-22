# Implementation Plan: Phase 10B — GroundUp.Auth.Keycloak Provider

## Overview

Implements the concrete Keycloak provider as a new project `GroundUp.Auth.Keycloak`. The implementation follows dependency order: project setup → options/validation → internal models → IdP service → admin service → utilities → DI registration → unit tests → integration tests. Each task group targets ~5 files for reviewability.

## Tasks

- [x] 1. Project setup and interface modification
  - [x] 1.1 Create `src/GroundUp.Auth.Keycloak/GroundUp.Auth.Keycloak.csproj` targeting net8.0
    - Add project references: GroundUp.Auth.Core, GroundUp.Auth.Services, GroundUp.Core
    - Add NuGet packages: Microsoft.Extensions.Http.Polly, Microsoft.Extensions.Options, Microsoft.IdentityModel.Tokens, System.IdentityModel.Tokens.Jwt
    - Enable nullable reference types, implicit usings
    - Add project to groundup.sln
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7_

  - [x] 1.2 Modify `IIdentityProviderService.ExchangeCodeForTokensAsync` signature
    - Add `string? clientId = null` and `string? codeVerifier = null` as trailing optional parameters
    - Update XML docs to document the new parameters
    - Fix any callers in GroundUp.Auth.Api that reference the old signature (should be compatible due to optional params)
    - _Requirements: 5.5, 5.6_

- [x] 2. Options and startup validation
  - [x] 2.1 Create `KeycloakOptions` class
    - Define all six properties: PublicBaseUrl, SharedRealmName, InternalBaseUrl, AdminClientId, AdminClientSecret, AppClientId
    - All string properties default to string.Empty
    - Place in root namespace `GroundUp.Auth.Keycloak`
    - _Requirements: 2.1_

  - [x] 2.2 Create `KeycloakOptionsSetup` implementing `IConfigureOptions<KeycloakOptions>` and `IOptionsChangeTokenSource<KeycloakOptions>`
    - Inject `IServiceProvider` (singleton, creates scopes to resolve scoped `ISettingsService`)
    - Read six settings keys via `ISettingsService` in `Configure` method
    - Expose `IChangeToken` that invalidates when `SettingChangedEvent` fires for any Keycloak key
    - _Requirements: 2.2, 2.3, 2.4, 2.5_

  - [x] 2.3 Create `KeycloakStartupValidator` as `IHostedService`
    - Validate all six settings are non-null/non-whitespace
    - Validate PublicBaseUrl and InternalBaseUrl are valid absolute URIs with http/https scheme
    - Skip validation when `BootstrapState.IsComplete` is false and settings are empty
    - Throw `InvalidOperationException` naming missing/invalid keys on failure
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 3. Checkpoint — Verify project compiles
  - Ensure the project compiles with `dotnet build`. Ask the user if questions arise.

- [x] 4. Internal Keycloak models and HTTP helpers
  - [x] 4.1 Create internal Keycloak API response/request records
    - `KeycloakTokenResponse` — token endpoint response with JsonPropertyName attributes
    - `KeycloakRealmRepresentation` — realm GET/POST body (subset)
    - `KeycloakClientRepresentation` — client GET/POST body (subset)
    - `KeycloakUserRepresentation` — user GET/POST body (subset)
    - `KeycloakCredentialRepresentation` — credential PUT body
    - All records are `internal` with `[JsonPropertyName]` attributes
    - Place in `Models/` subfolder
    - _Requirements: 5.3, 9.1, 10.1, 11.1_

  - [x] 4.2 Create `JwksCache` internal class
    - `ConcurrentDictionary<string, CachedKeySet>` keyed by issuer URL
    - `SemaphoreSlim` for serialized refresh
    - `GetKeysForIssuerAsync` — returns cached keys or fetches from `{issuerUrl}/protocol/openid-connect/certs`
    - `RefreshKeysForIssuerAsync` — force-refresh, rate-limited to once per 30s per issuer
    - Uses `KeycloakIdp` named HttpClient
    - _Requirements: 6.1, 6.2_

  - [x] 4.3 Create `AdminTokenCache` internal class
    - `SemaphoreSlim` for thread-safe token acquisition
    - Cache token with expiry minus 30-second safety margin
    - `GetTokenAsync` — returns cached or acquires via `client_credentials` POST
    - `InvalidateCache` on options change (subscribe via `IOptionsMonitor.OnChange`)
    - Implements `IDisposable` to release semaphore
    - Never exposes `AdminClientSecret` in error messages or logs
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

- [x] 5. Checkpoint — Verify project compiles with models and caches
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Identity provider service implementation
  - [x] 6.1 Create `KeycloakIdentityProviderService` implementing `IIdentityProviderService`
    - Inject `IHttpClientFactory`, `IOptionsMonitor<KeycloakOptions>`, `JwksCache`, `ILogger`
    - Implement `ExchangeCodeForTokensAsync`:
      - POST to `{InternalBaseUrl}/realms/{realm}/protocol/openid-connect/token`
      - Form-encoded: grant_type, code, redirect_uri, client_id, optional code_verifier
      - Defaults realm to SharedRealmName, clientId to AppClientId
      - Return `TokenResponseDto` on 200, null on non-success
      - Throw on malformed JSON in 200 response
    - Implement `ValidateTokenAsync`:
      - Peek `iss` from token payload without full validation
      - Validate `iss` starts with `{InternalBaseUrl}/realms/`
      - Validate signature via JWKS, lifetime, issuer
      - On unknown `kid`: trigger JWKS refresh, retry once
      - Return true/false, never throw
    - Implement `GetUserInfoAsync`:
      - GET `{InternalBaseUrl}/realms/{SharedRealmName}/protocol/openid-connect/userinfo`
      - Map to `ExternalUserInfo` (sub → ExternalUserId, email → Email, name/preferred_username → DisplayName, rest → Attributes)
      - Return null on 401 or other non-success, log Warning
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 7.1, 7.2, 7.3, 7.4, 7.5_

- [x] 7. Admin service implementation
  - [x] 7.1 Create `KeycloakIdentityProviderAdminService` — realm CRUD methods
    - Inject `IHttpClientFactory`, `IOptionsMonitor<KeycloakOptions>`, `AdminTokenCache`, `ILogger`
    - All methods: acquire admin token first, return failure OperationResult if token is null
    - `CreateRealmAsync` — POST to `/admin/realms`
    - `GetRealmAsync` — GET from `/admin/realms/{realmName}`, NotFound on 404
    - `UpdateRealmAsync` — PUT to `/admin/realms/{realmName}`
    - `DeleteRealmAsync` — DELETE `/admin/realms/{realmName}`, NotFound on 404
    - Map responses to `RealmDto`, failure to `OperationResult.Fail`
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7_

  - [x] 7.2 Add client CRUD methods to `KeycloakIdentityProviderAdminService`
    - `CreateClientAsync` — POST to `/admin/realms/{realmName}/clients`, re-fetch to get secret
    - `GetClientAsync` — GET `/admin/realms/{realmName}/clients?clientId={clientId}`, NotFound if empty
    - `UpdateClientAsync` — PUT to `/admin/realms/{realmName}/clients/{internalId}`
    - `DeleteClientAsync` — DELETE `/admin/realms/{realmName}/clients/{internalId}`
    - Map to `IdentityProviderClientDto`, extract PKCE from attributes
    - Handle 409 conflict for duplicate clientId
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7_

  - [x] 7.3 Add user provisioning methods to `KeycloakIdentityProviderAdminService`
    - `ProvisionUserAsync` — POST to `/admin/realms/{realmName}/users`, extract userId from Location header
    - Handle InitialPassword: PUT `/users/{userId}/reset-password` with temporary flag
    - Handle RequirePasswordReset without password: set requiredActions=["UPDATE_PASSWORD"]
    - `SetUserCredentialsAsync` — PUT `/users/{externalUserId}/reset-password`
    - `DeleteUserAsync` — DELETE `/users/{externalUserId}`, NotFound on 404
    - Handle 409 conflict for duplicate email
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8_

- [x] 8. Checkpoint — Verify project compiles with all services
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Admin link builder, role extractor, and DI registration
  - [x] 9.1 Create `ResourceAccessRoleExtractor` static class
    - `ExtractRoles(string token, string clientId)` — parse token payload, navigate resource_access
    - `ExtractRolesFromClaims(IEnumerable<Claim> claims, string clientId)` — parse from claim collection
    - Return empty list on any failure (missing claim, malformed JSON, wrong structure)
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

  - [x] 9.2 Create `KeycloakAdminLinkBuilder` class
    - Inject `IOptionsMonitor<KeycloakOptions>`
    - Methods: RealmOverview, ClientList, ClientDetail, UserList, UserDetail, IdentityProviderConfig
    - URL pattern: `{PublicBaseUrl}/admin/{realmName}/console/#/{subPath}`
    - Default realm to SharedRealmName when parameter is null
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6_

  - [x] 9.3 Create `KeycloakServiceCollectionExtensions` with `AddGroundUpAuthKeycloak()`
    - Register KeycloakOptionsSetup as singleton (IConfigureOptions + IOptionsChangeTokenSource)
    - Register KeycloakStartupValidator as hosted service
    - Register named HttpClients "KeycloakIdp" and "KeycloakAdmin" with 10s timeout and Polly retry
    - Polly: exponential backoff with jitter, 3 retries, 5xx + transient only, log at Warning
    - Register AdminTokenCache and JwksCache as singletons
    - Register KeycloakIdentityProviderService as scoped IIdentityProviderService
    - Register KeycloakIdentityProviderAdminService as scoped IIdentityProviderAdminService
    - Register KeycloakAdminLinkBuilder as singleton
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 14.1, 14.2, 14.3, 14.4, 14.5, 14.6_

- [x] 10. Checkpoint — Full build verification
  - Ensure the solution compiles cleanly with `dotnet build groundup.sln`. Ask the user if questions arise.

- [ ] 11. Unit tests — property-based (FsCheck) and standard
  - [-] 11.1 Create `ResourceAccessRoleExtractorPropertyTests.cs`
    - **Property 15: Role Extraction From resource_access (Happy Path)**
    - **Property 16: Role Extraction Returns Empty for Missing or Malformed Claims**
    - **Validates: Requirements 12.1, 12.2, 12.3, 12.5**
    - Custom Arbitrary generators for resource_access JSON structures
    - _Requirements: 12.1, 12.2, 12.3, 12.5_

  - [-] 11.2 Create `KeycloakAdminLinkBuilderPropertyTests.cs`
    - **Property 17: Admin Link Builder URL Pattern Correctness**
    - **Validates: Requirements 13.2, 13.3, 13.4, 13.5**
    - Generate valid PublicBaseUrl + realm name combinations, verify URL pattern
    - _Requirements: 13.2, 13.3, 13.4, 13.5_

  - [-] 11.3 Create `KeycloakStartupValidatorPropertyTests.cs`
    - **Property 3: Startup Validation Names Missing or Invalid Keys**
    - **Validates: Requirements 3.1, 3.2**
    - Generate subsets of missing/invalid settings, verify exception message names each key
    - _Requirements: 3.1, 3.2_

  - [ ] 11.4 Create `AdminTokenCachePropertyTests.cs`
    - **Property 10: Admin Token Caching With Expiry-Based Refresh**
    - **Property 11: Admin Token Failure Does Not Expose Secrets**
    - **Validates: Requirements 8.2, 8.3, 8.4**
    - Mock HttpMessageHandler, verify cache behavior with generated expiry values
    - _Requirements: 8.2, 8.3, 8.4_

  - [ ] 11.5 Create `KeycloakIdentityProviderServiceTests.cs`
    - **Property 4: Token Endpoint Request Construction**
    - **Property 5: Token Response Mapping**
    - **Property 6: Non-Success Code Exchange Returns Null**
    - **Property 9: Userinfo Response Mapping**
    - **Validates: Requirements 5.1, 5.3, 5.4, 5.5, 5.6, 7.2**
    - Mock HttpMessageHandler, verify request construction with generated inputs
    - Standard xUnit tests for ValidateTokenAsync with known JWTs
    - _Requirements: 5.1, 5.3, 5.4, 5.5, 5.6, 6.3, 6.4, 6.5, 7.2_

  - [ ] 11.6 Create `KeycloakIdentityProviderAdminServiceTests.cs`
    - **Property 12: Admin API URL Construction for Realm Operations**
    - **Property 13: Admin API URL Construction for Client Operations**
    - **Property 14: User Provisioning Location Header Extraction**
    - **Validates: Requirements 9.1, 9.2, 9.4, 9.5, 10.1, 10.3, 11.2**
    - Mock HttpMessageHandler, verify URL construction and response mapping
    - Standard xUnit tests for error handling (404, 409, 5xx)
    - _Requirements: 9.1, 9.2, 9.4, 9.5, 10.1, 10.3, 11.2_

  - [ ]* 11.7 Create `PollyRetryPolicyPropertyTests.cs`
    - **Property 18: Retry Occurs on 5xx, Not on 4xx**
    - **Property 19: All Retries Exhausted Returns Failure Without Throwing**
    - **Validates: Requirements 14.2, 14.3, 14.6**
    - Generate HTTP status codes, verify retry/no-retry behavior
    - _Requirements: 14.2, 14.3, 14.6_

  - [ ]* 11.8 Create `KeycloakOptionsSetupTests.cs`
    - **Property 1: Settings-to-Options Mapping Roundtrip**
    - **Property 2: Options Refresh on Settings Change**
    - **Validates: Requirements 2.2, 2.4**
    - Mock ISettingsService, verify option values match generated settings
    - _Requirements: 2.2, 2.4_

- [ ] 12. Checkpoint — Verify all unit tests pass
  - Run `dotnet test` for the unit test project. Ensure all tests pass, ask the user if questions arise.

- [ ] 13. Integration tests with Testcontainers Keycloak
  - [ ] 13.1 Create `KeycloakFixture` and `KeycloakCollection` shared fixture
    - Start Keycloak 26.x container via Testcontainers
    - Import `groundup` realm from `keycloak/realm.json`
    - Expose HttpClient factory pointing at container
    - Expose admin credentials for test setup
    - Implement `IAsyncLifetime` for container lifecycle
    - _Requirements: 15.1, 15.9_

  - [ ] 13.2 Create `KeycloakCodeExchangeTests.cs` and `KeycloakTokenValidationTests.cs`
    - Verify code exchange end-to-end using direct-access grant
    - Verify token validation against real JWKS from container
    - _Requirements: 15.2, 6.1, 6.6_

  - [ ] 13.3 Create `KeycloakRealmCrudTests.cs` and `KeycloakClientCrudTests.cs`
    - Realm CRUD round-trip: create → get → update → verify → delete → verify gone
    - Client CRUD round-trip: create → get → verify settings → update → delete
    - _Requirements: 15.3, 15.4_

  - [ ] 13.4 Create `KeycloakUserProvisioningTests.cs` and `KeycloakRoleExtractionTests.cs`
    - User provisioning: create with password → verify exists → set credentials → delete
    - Role extraction: create user, assign roles, get token, extract, verify
    - _Requirements: 15.5, 15.6_

  - [ ]* 13.5 Create `KeycloakAdminLinkBuilderTests.cs` and `KeycloakStartupValidationTests.cs`
    - Verify link builder returns correctly formed URLs against running instance
    - Verify startup validation throws on missing/invalid settings
    - _Requirements: 15.7, 15.8_

- [ ] 14. Final checkpoint — All tests pass
  - Run `dotnet test` for the full solution. Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The design uses C# directly — no language selection needed
- Task groups are sized for ~5 files each for reviewable PRs
- Integration tests require Docker for Testcontainers

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "2.1"] },
    { "id": 2, "tasks": ["2.2", "2.3"] },
    { "id": 3, "tasks": ["4.1", "4.2", "4.3"] },
    { "id": 4, "tasks": ["6.1"] },
    { "id": 5, "tasks": ["7.1"] },
    { "id": 6, "tasks": ["7.2", "7.3"] },
    { "id": 7, "tasks": ["9.1", "9.2"] },
    { "id": 8, "tasks": ["9.3"] },
    { "id": 9, "tasks": ["11.1", "11.2", "11.3"] },
    { "id": 10, "tasks": ["11.4", "11.5", "11.6", "11.7", "11.8"] },
    { "id": 11, "tasks": ["13.1"] },
    { "id": 12, "tasks": ["13.2", "13.3", "13.4", "13.5"] }
  ]
}
```
