# Implementation Plan: Phase 10AB — Initial Setup & Secrets Foundation

## Overview

This plan implements the encryption substrate, bootstrap state machine, and first-run setup wizard for the GroundUp framework. Work is broken into 34 small, reviewable task groups (~5 files each) following the dependency order: core abstractions → data layer → service implementations → API layer → tests.

## Tasks

- [x] 1. Core abstractions and entities
  - [x] 1.1 Create `IMasterKeyProvider` interface in `GroundUp.Core/Abstractions/`
    - Define `byte[] GetKey()` method with XML docs
    - _Requirements: 1.10_
  - [x] 1.2 Create `ISecretResolver` interface in `GroundUp.Core/Abstractions/`
    - Define `Task<string?> ResolveAsync(string secretRef, CancellationToken)` with XML docs
    - _Requirements: 4.1_
  - [x] 1.3 Create `IBootstrapStateService` interface in `GroundUp.Core/Abstractions/`
    - Define `IsCompleteAsync`, `CompleteSetupAsync`, `InvalidateCache` methods
    - _Requirements: 6.1_
  - [x] 1.4 Create `BootstrapState` entity in `GroundUp.Core/Entities/`
    - Singleton entity with sentinel ID, `IsComplete`, `CompletedAt`, `CompletedBy`, `IAuditable`
    - _Requirements: 5.1_
  - [x] 1.5 Create `SetupTransactionLog` entity in `GroundUp.Core/Entities/`
    - `Operation`, `Stage`, `CorrelationId`, `ExternalUserId`, `Email`, `ErrorMessage`, `IAuditable`
    - _Requirements: 13.1_
  - [x] 1.6 Create `EncryptionException` in `GroundUp.Core/Exceptions/`
    - Sealed exception with message and inner-exception constructors
    - _Requirements: 2.4, 2.5_
  - [x] 1.7 Create `BootstrapOptions` and `KeycloakBootstrapOptions` in `GroundUp.Core/Configuration/`
    - Bound from `GroundUp` section; properties for `DatabaseConnection`, `MasterKey`, `MasterKeyPath`, `BootstrapAdminToken`
    - _Requirements: 18.1, 18.2_
  - [x] 1.8 Create `SetupOptions`, `SetupRateLimitOptions`, `SetupTransactionLogOptions` in `GroundUp.Core/Configuration/`
    - Bound from `GroundUp:Setup`, `GroundUp:Setup:RateLimit`, `GroundUp:SetupTransactionLog` sections
    - _Requirements: 18.4, 18.5, 13.10_

- [x] 2. EF configurations for new entities
  - [x] 2.1 Create `BootstrapStateConfiguration` in `GroundUp.Data.Postgres/Configurations/`
    - Table name, CHECK constraint for singleton, unique index, xmin concurrency token, IAuditable columns
    - _Requirements: 5.1, 5.2, 5.7_
  - [x] 2.2 Create `SetupTransactionLogConfiguration` in `GroundUp.Data.Postgres/Configurations/`
    - Table name, column constraints, indexes on `(Operation, Stage)` and `CreatedAt`
    - _Requirements: 13.1, 13.2_
  - [x] 2.3 Register both entities in `GroundUpDbContext`
    - Add `DbSet<BootstrapState>` and `DbSet<SetupTransactionLog>` properties
    - _Requirements: 5.7_

- [x] 3. EF migration
  - [x] 3.1 Create migration `AddBootstrapStateAndSetupTransactionLog`
    - Create both tables, seed singleton BootstrapState row with `IsComplete=false`
    - _Requirements: 5.3, 5.8, 13.2_

- [x] 4. AesGcmSettingEncryptionProvider implementation
  - [x] 4.1 Create `AesGcmSettingEncryptionProvider` in `GroundUp.Services/Security/`
    - Implement `ISettingEncryptionProvider.Encrypt` with fresh 12-byte nonce, AES-256-GCM, self-describing format
    - Implement `ISettingEncryptionProvider.Decrypt` with prefix validation, base64 parsing, auth-tag verification
    - Throw `ArgumentException` on null/empty/whitespace input for both methods
    - Throw `EncryptionException` on unsupported prefix, malformed format, or tag failure
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7_

- [x] 5. EnvironmentFileMasterKeyProvider implementation
  - [x] 5.1 Create `EnvironmentFileMasterKeyProvider` in `GroundUp.Services/Security/`
    - File-first priority resolution with double-checked locking cache
    - Validate: file exists, non-empty, valid base64, decoded >= 32 bytes
    - Log warning when both sources configured; log warning on Unix permissive file mode
    - Throw `InvalidOperationException` with descriptive messages for each failure case
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 1.10, 1.11, 1.12_

- [x] 6. SettingsService updates
  - [x] 6.1 Update `SettingsService` to short-circuit null/empty/whitespace before encryption
    - On `SetAsync`: persist null without calling `Encrypt` when value is null/empty/whitespace
    - On `GetAsync`: return default-value fallback without calling `Decrypt` when stored value is null/empty/whitespace
    - _Requirements: 3.1, 3.2, 3.3, 3.4_
  - [x] 6.2 Update `SettingsService` to integrate `ISettingEncryptionProvider` for `IsEncrypted=true` settings
    - Encrypt on write, decrypt on read; fail with clear error if provider not registered
    - _Requirements: 3.5, 3.6, 3.9, 3.10_
  - [x] 6.3 Update `SettingsService` to integrate `ISecretResolver` for `secretref://` values
    - Single-pass resolution on read paths only; literal pass-through when no resolver registered
    - _Requirements: 4.2, 4.3, 4.4, 4.6, 4.7, 4.8_
  - [x] 6.4 Update `SecretMask` constant from `"••••••••"` to `"***REDACTED***"`
    - Update any Phase 6 unit tests asserting on the old mask value
    - _Requirements: 3.7_

- [x] 7. BootstrapStateService implementation
  - [x] 7.1 Create `BootstrapStateService` in `GroundUp.Services/Bootstrap/`
    - `IsCompleteAsync`: IMemoryCache with 60s TTL, read from DB on miss, throw if row missing
    - `CompleteSetupAsync`: update row, xmin concurrency check, invalidate cache on success
    - `InvalidateCache`: evict cache key
    - _Requirements: 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8_

- [x] 8. SetupCurrentUser and AuditableInterceptor adjustment
  - [x] 8.1 Create `SetupCurrentUser` in `GroundUp.Services/Bootstrap/`
    - Implement `ICurrentUser` with sentinel Guid and `"setup-wizard"` display name
    - _Requirements: Cross-Cutting 8_
  - [x] 8.2 Update `AuditableInterceptor` to recognize `SetupCurrentUser.SetupSentinelUserId`
    - Write literal `"setup-wizard"` string to `CreatedBy`/`UpdatedBy` when sentinel Guid detected
    - _Requirements: Cross-Cutting 8_

- [x] 9. BootstrapModeMiddleware
  - [x] 9.1 Create `BootstrapModeMiddleware` in `GroundUp.Api/Middleware/`
    - Check `IBootstrapStateService.IsCompleteAsync`; pass through if complete
    - Allowed-path matching with `StartsWithSegments` (segment-aware, case-insensitive)
    - JSON clients get 503 (`setup_required`); others get 302 redirect to `/setup`
    - Handle DB exceptions → 503; handle missing row → 503 with `bootstrap_state_missing`
    - Log first setup-mode observation at Information; subsequent at Debug
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8, 5.4, 5.10_

- [x] 10. BootstrapAdminTokenAuthenticationHandler
  - [x] 10.1 Create `BootstrapAdminTokenAuthenticationHandler` in `GroundUp.Api/Authentication/`
    - Validate Bearer token with constant-time comparison (`CryptographicOperations.FixedTimeEquals`)
    - Reject all requests when `IsCompleteAsync=true`
    - Fail with descriptive reasons for missing header, invalid token, unconfigured token
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.8, 8.9_

- [x] 11. Startup hosted services
  - [x] 11.1 Create `MigrationStartupHostedService` in `GroundUp.Services/Bootstrap/`
    - Run `dbContext.Database.MigrateAsync` during `StartAsync`
    - _Requirements: 5.9_
  - [x] 11.2 Create `BootstrapTokenStartupValidator` in `GroundUp.Services/Bootstrap/`
    - Check `IsCompleteAsync`; if incomplete, throw if `BootstrapAdminToken` not configured
    - _Requirements: 8.6, 8.7, 8.9_

- [x] 12. Options validators
  - [x] 12.1 Create `BootstrapOptionsValidator` in `GroundUp.Services/Configuration/`
    - Validate `DatabaseConnection` required, at least one master key source, token min 32 chars if present
    - _Requirements: 18.1, 18.2, 8.7_
  - [x] 12.2 Create `SetupOptionsValidator` in `GroundUp.Services/Configuration/`
    - Clamp `MaxRequestBodyBytes` to 4096-byte floor
    - _Requirements: 18.5_

- [x] 13. Setup DTOs
  - [x] 13.1 Create request DTOs in `GroundUp.Core/Dtos/Setup/`
    - `SetAppIdentityRequest`, `SetIdentityProviderRequest`, `KeycloakBootstrapRequest`, `CreateFirstAdminRequest`
    - _Requirements: 9.1, 10.1, 11.1, 12.1_
  - [x] 13.2 Create response DTOs in `GroundUp.Core/Dtos/Setup/`
    - `StepResultDto`, `KeycloakBootstrapResultDto`, `FirstAdminResultDto`, `RecoverResultDto`, `SetupTransactionLogDto`, `SetupStatusDto`
    - _Requirements: 9.9, 10.11, 11.17, 12.22, 13.3, 15.3_

- [x] 14. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 15. IIdentityBootstrapService interface and DTOs (auth module)
  - [x] 15.1 Create `IIdentityBootstrapService` interface in `GroundUp.Auth.Services/Bootstrap/`
    - `ProvisionFirstSuperAdminAsync` and `HasSuperAdminAsync` methods
    - _Requirements: 12.23_
  - [x] 15.2 Create `ProvisionFirstSuperAdminRequest` record in `GroundUp.Auth.Services/Bootstrap/`
    - Properties: `Email`, `DisplayName`, `ExternalUserId`, `TenantId`
    - _Requirements: 12.23_
  - [x] 15.3 Create `BootstrapAdminResultDto` record in `GroundUp.Auth.Services/Bootstrap/`
    - Properties: `UserId`, `Email`, `AlreadyExisted`
    - _Requirements: 12.23_

- [x] 16. IdentityBootstrapService implementation
  - [x] 16.1 Create `IdentityBootstrapService` in `GroundUp.Auth.Services/Bootstrap/`
    - Implement `ProvisionFirstSuperAdminAsync` with single transaction, explicit existence checks
    - User + UserTenant + SuperAdmin role assignment; idempotent retry support
    - Translate unique-violation to Conflict result; conflict on attribute mismatch
    - Implement `HasSuperAdminAsync` query
    - _Requirements: 12.21, 12.23, 12.24, 12.26_
  - [x] 16.2 Register `IdentityBootstrapService` in auth module's `AddGroundUpAuth()` extension
    - Scoped registration of `IIdentityBootstrapService`
    - _Requirements: 12.23_

- [x] 17. KeycloakAdminHttpClient (typed HTTP client)
  - [x] 17.1 Create `KeycloakAdminHttpClient` in `GroundUp.Api/Setup/`
    - Methods: `AcquireAdminTokenAsync`, `GetExistingClientAsync`, `CreateAdminClientAsync`
    - Methods: `GetServiceAccountRoleNamesAsync`, `AddServiceAccountRolesAsync`, `GetClientSecretAsync`
    - Static `RequiredRealmManagementRoles` array
    - _Requirements: 11.7, 11.12, 11.13_
  - [x] 17.2 Create supporting DTOs for Keycloak responses in `GroundUp.Api/Setup/`
    - `KeycloakAdminTokenResponse`, `KeycloakClientLookupResult`, `KeycloakClientCreateResult`
    - _Requirements: 11.7_

- [x] 18. ISetupWizardService interface
  - [x] 18.1 Create `ISetupWizardService` interface in `GroundUp.Services/Setup/`
    - All wizard method signatures: `GetStatusAsync`, `SetAppIdentityAsync`, `SetIdentityProviderAsync`, `BootstrapKeycloakAsync`, `CreateFirstAdminAsync`, `CompleteSetupAsync`, `GetTransactionLogAsync`, `RecoverAsync`
    - _Requirements: 9, 10, 11, 12, 13, 14, 15_
  - [x] 18.2 Create `SetupPreconditions` static helper in `GroundUp.Services/Setup/`
    - Shared precondition checks for step ordering enforcement
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5_

- [x] 19. SetupWizardService — app-identity + identity-provider steps
  - [x] 19.1 Create `SetupWizardService` class in `GroundUp.Services/Setup/` (partial — app-identity step)
    - `SetAppIdentityAsync`: trim, validate, `EnsureDefinitionAsync`, `SetAsync` for both keys
    - _Requirements: 9.1, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 9.9, 17.1_
  - [x] 19.2 Implement `SetIdentityProviderAsync` in `SetupWizardService`
    - Trim, validate URLs via `Uri.TryCreate`, default `internalBaseUrl` to `publicBaseUrl` when empty
    - `EnsureDefinitionAsync` + `SetAsync` for three keys; precondition check
    - _Requirements: 10.1, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8, 10.9, 10.10, 10.11, 16.1, 17.2_

- [x] 20. SetupWizardService — keycloak-bootstrap step
  - [x] 20.1 Implement `BootstrapKeycloakAsync` in `SetupWizardService`
    - Precondition checks (app-identity + identity-provider completed)
    - Resolve credentials from request body or config fallback
    - Call `KeycloakAdminHttpClient` methods: acquire token, get/create client, validate roles, get secret
    - Persist `admin-client-id` and `admin-client-secret` (encrypted) via `ISettingsService`
    - Scrub credentials in `finally` block
    - _Requirements: 11.1–11.17, 16.2, 17.3_

- [x] 21. SetupWizardService — first-admin step + transaction log rotation
  - [x] 21.1 Implement `CreateFirstAdminAsync` in `SetupWizardService`
    - Precondition checks (all prior steps completed, system tenant + SuperAdmin role exist)
    - Input validation: email RFC-5322, displayName, password complexity (Lu/Ll/Nd/non-alnum)
    - Idempotency check via `IIdentityBootstrapService.HasSuperAdminAsync`
    - Transaction log: insert `keycloak-pending` → call `IIdentityProviderAdminService.ProvisionUserAsync` → update `db-pending` → call `IIdentityBootstrapService.ProvisionFirstSuperAdminAsync` → update `completed`
    - _Requirements: 12.1–12.26, 16.3_
  - [x] 21.2 Implement `InsertWithRotationAsync` private helper
    - Bounded rotation respecting `MaxRowCount`; skip pending-stage rows; warn when all candidates are pending
    - _Requirements: 13.10_
  - [x] 21.3 Implement `RecoverAsync` in `SetupWizardService`
    - Handle `db-pending` recovery via `IIdentityBootstrapService`; reject `completed` and `keycloak-pending`
    - _Requirements: 13.4, 13.5, 13.6, 13.7, 13.8_

- [x] 22. SetupWizardService — complete step + status + GET /setup landing
  - [x] 22.1 Implement `CompleteSetupAsync` in `SetupWizardService`
    - Precondition checks (all steps completed); call `IBootstrapStateService.CompleteSetupAsync`
    - _Requirements: 14.1–14.8, 16.4_
  - [x] 22.2 Implement `GetStatusAsync` in `SetupWizardService`
    - Compute all flags, `currentStep`, `firstAdminPending` detection
    - _Requirements: 15.1–15.9_
  - [x] 22.3 Implement `GetTransactionLogAsync` in `SetupWizardService`
    - Return most recent 20 rows ordered by `CreatedAt DESC`
    - _Requirements: 13.3_

- [x] 23. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 24. SetupController
  - [x] 24.1 Create `SetupController` in `GroundUp.Api/Controllers/Setup/`
    - All endpoints: `GetSetupLanding`, `GetStatus`, `SetAppIdentity`, `SetIdentityProvider`, `BootstrapKeycloak`, `CreateFirstAdmin`, `CompleteSetup`, `GetTransactionLog`, `Recover`
    - Thin HTTP adapter mapping `OperationResult` to `IActionResult`
    - `[Authorize(AuthenticationSchemes = BootstrapAdminTokenAuthenticationHandler.SchemeName)]` on protected endpoints
    - `[AllowAnonymous]` on `GET /setup` landing
    - _Requirements: 9.1, 10.1, 11.1, 12.1, 13.3, 13.4, 14.1, 15.1_

- [x] 25. Rate limiting + body size filter
  - [x] 25.1 Create `SetupBodySizeFilter` in `GroundUp.Api/Setup/`
    - Endpoint filter checking `ContentLength` against `SetupOptions.MaxRequestBodyBytes`
    - Return 413 with `payload_too_large` error shape
    - _Requirements: 18.5_
  - [x] 25.2 Configure rate limiter policy `SetupRateLimit` in `AddGroundUpSetup()`
    - Fixed-window per IP; bypass loopback in Development; disabled when `IsComplete=true`
    - 429 response with `Retry-After` header and `rate_limited` error shape
    - _Requirements: 18.4_

- [x] 26. Health checks
  - [x] 26.1 Create `MasterKeyHealthCheck` in `GroundUp.Api/HealthChecks/`
    - Verify `IMasterKeyProvider.GetKey()` succeeds and returns >= 32 bytes
    - _Requirements: 7.7_
  - [x] 26.2 Create `BootstrapStateAwareHealthCheck` in `GroundUp.Api/HealthChecks/`
    - Wrapper that returns Healthy in setup mode without executing inner check
    - _Requirements: 7.7_
  - [x] 26.3 Create `AddGroundUpHealthChecks()` extension in `GroundUp.Api/`
    - Register `MasterKeyHealthCheck` and `DbContextCheck`; custom `/ready` response writer with `setupMode` flag
    - _Requirements: 7.7_

- [x] 27. Module registration extensions
  - [x] 27.1 Create `AddGroundUpBootstrap()` extension in `GroundUp.Services/`
    - Register: `IMasterKeyProvider`, `IBootstrapStateService`, `ISettingEncryptionProvider`, `ICurrentUser` factory, hosted services, options + validators, `IMemoryCache`
    - _Requirements: 1.12, 2.8, 2.9, 6.7, 5.9, 8.6, 18.1_
  - [x] 27.2 Create `AddGroundUpSetup()` extension in `GroundUp.Api/`
    - Register: `SetupOptions` + validator, authentication scheme, `ISetupWizardService`, `KeycloakAdminHttpClient` with Polly retry, rate limiter policy
    - _Requirements: 18.4, 18.5, 8.1_
  - [x] 27.3 Create `UseGroundUpBootstrapMode()` extension in `GroundUp.Api/`
    - Register `BootstrapModeMiddleware` in the pipeline
    - _Requirements: 7.1, 7.5_

- [x] 28. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 29. Property-based tests — encryption (Properties 1–4)
  - [x]* 29.1 Create `AesGcmEncryptionPropertyTests` in `GroundUp.Tests.Unit/Security/`
    - **Property 1: Encryption Round-Trip Integrity** — For any non-whitespace UTF-8 string, encrypt then decrypt produces the original
    - **Validates: Requirements 2.2, 2.3, 3.1, 3.3**
  - [x]* 29.2 Add property test for tagged fresh ciphertext
    - **Property 2: Encryption Produces Tagged, Fresh Ciphertext** — Two encryptions of same value differ, both start with `aes-gcm-v1:`, parse into 4 colon-delimited base64 segments
    - **Validates: Requirements 2.2, 2.7**
  - [x]* 29.3 Add property test for decryption failures
    - **Property 3: Decryption Fails on Wrong Key, Tampering, or Unsupported Prefix** — Wrong key throws, mutated byte throws, missing prefix throws (all `EncryptionException`)
    - **Validates: Requirements 2.4, 2.5**
  - [x]* 29.4 Add property test for whitespace rejection
    - **Property 4: Provider Rejects Null/Empty/Whitespace with ArgumentException** — Null, empty, whitespace-only inputs throw `ArgumentException` naming the parameter
    - **Validates: Requirements 2.6**

- [x] 30. Property-based tests — bootstrap + service (Properties 5–10)
  - [x]* 30.1 Create `SettingsServiceEncryptionPropertyTests` in `GroundUp.Tests.Unit/Services/Settings/`
    - **Property 5: SettingsService Short-Circuits Whitespace Before Calling Provider** — Null/empty/whitespace persists null without calling Encrypt; non-whitespace calls Encrypt exactly once
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
  - [x]* 30.2 Create `SecretResolverPropertyTests` in `GroundUp.Tests.Unit/Services/Settings/`
    - **Property 6: Single-Pass Secret Reference Resolution** — Resolver called exactly once; result returned verbatim even if it starts with `secretref://`; write paths persist literal
    - **Validates: Requirements 4.6, 4.8**
  - [x]* 30.3 Create `BootstrapStatePropertyTests` in `GroundUp.Tests.Unit/Services/Bootstrap/`
    - **Property 7: Bootstrap One-Shot Completion Under Concurrency** — Exactly one of N concurrent calls succeeds; rest return Conflict
    - **Validates: Requirements 6.3, 6.4**
  - [x]* 30.4 Create `BootstrapMiddlewarePropertyTests` in `GroundUp.Tests.Unit/Api/Middleware/`
    - **Property 8: Bootstrap Middleware Path Partition** — Allowed paths pass through; non-allowed paths get 503/302; all paths pass when complete
    - **Validates: Requirements 7.2, 7.3, 7.4, 7.6**
  - [x]* 30.5 Create `BootstrapTokenAuthPropertyTests` in `GroundUp.Tests.Unit/Api/Authentication/`
    - **Property 9: Bootstrap Token Authentication Correctness** — Succeeds iff `isComplete=false` AND token bytes match (constant-time)
    - **Validates: Requirements 8.2, 8.3, 8.4, 8.5, 8.8**
  - [x]* 30.6 Create `WizardIdempotencyAndRotationPropertyTests` in `GroundUp.Tests.Unit/Services/Setup/`
    - **Property 10: Wizard Idempotency and Transaction Log Rotation** — Repeated calls produce same state; rotation respects MaxRowCount and pending-row protection
    - **Validates: Requirements 9.8, 10.10, 12.20, 13.10**

- [x] 31. Unit tests — master key provider + AES-GCM provider
  - [x]* 31.1 Create `EnvironmentFileMasterKeyProviderTests` in `GroundUp.Tests.Unit/Services/Security/`
    - Test: file not found throws, empty file throws, invalid base64 throws, short key throws, caching works, both-sources warning logged
    - _Requirements: 1.1–1.12_
  - [x]* 31.2 Create `AesGcmSettingEncryptionProviderTests` in `GroundUp.Tests.Unit/Services/Security/`
    - Test: ArgumentException on whitespace, format validation of output, tampered ciphertext throws EncryptionException, unsupported prefix throws
    - _Requirements: 2.1–2.9_

- [x] 32. Unit tests — SettingsService encryption + secret resolver
  - [x]* 32.1 Create `SettingsServiceEncryptionTests` in `GroundUp.Tests.Unit/Services/Settings/`
    - Test: encrypt on set, decrypt on get, missing-provider error, mask `***REDACTED***`, IsSecret+IsEncrypted combo
    - _Requirements: 3.1–3.10_
  - [x]* 32.2 Create `SettingsServiceSecretResolverTests` in `GroundUp.Tests.Unit/Services/Settings/`
    - Test: resolve on read, verbatim without resolver, null resolution failure, single-pass behavior
    - _Requirements: 4.1–4.8_
  - [x]* 32.3 Update existing Phase 6 `SettingsServiceMaskTests` for new mask constant
    - Change assertions from `"••••••••"` to `"***REDACTED***"`
    - _Requirements: 3.7_

- [x] 33. Unit tests — BootstrapStateService + middleware + auth handler
  - [x]* 33.1 Create `BootstrapStateServiceTests` in `GroundUp.Tests.Unit/Services/Bootstrap/`
    - Test: cache hit/miss, complete success, already-complete conflict, xmin concurrency
    - _Requirements: 6.1–6.8_
  - [x]* 33.2 Create `BootstrapModeMiddlewareTests` in `GroundUp.Tests.Unit/Api/Middleware/`
    - Test: allowed paths pass, redirect for non-allowed, JSON 503, health passthrough, Accept header parsing
    - _Requirements: 7.1–7.8_
  - [x]* 33.3 Create `BootstrapAdminTokenAuthHandlerTests` in `GroundUp.Tests.Unit/Api/Authentication/`
    - Test: valid token succeeds, invalid token fails, missing header fails, post-setup rejection
    - _Requirements: 8.1–8.9_

- [x] 34. Unit tests — SetupWizardService + preconditions + status
  - [x]* 34.1 Create `SetupWizardServiceTests` in `GroundUp.Tests.Unit/Services/Setup/`
    - Test: each step validation, precondition enforcement, idempotency, password complexity
    - _Requirements: 9–14, 16_
  - [x]* 34.2 Create `SetupPreconditionsTests` in `GroundUp.Tests.Unit/Services/Setup/`
    - Test: each ordering rule returns correct error when predecessor incomplete
    - _Requirements: 16.1–16.5_
  - [x]* 34.3 Create `SetupStatusComputationTests` in `GroundUp.Tests.Unit/Services/Setup/`
    - Test: all flag combinations, currentStep transitions, firstAdminPending detection
    - _Requirements: 15.3–15.9_

- [x] 35. Unit tests — IdentityBootstrapService + transaction log rotation + options validators
  - [x]* 35.1 Create `IdentityBootstrapServiceTests` in `GroundUp.Tests.Unit/Auth/Services/Bootstrap/`
    - Test: existence checks, transactional User+UserTenant+SuperAdmin, idempotent retry, conflict on attribute mismatch, HasSuperAdminAsync
    - _Requirements: 12.21, 12.23, 12.26_
  - [x]* 35.2 Create `TransactionLogRotationTests` in `GroundUp.Tests.Unit/Services/Setup/`
    - Test: pending rows skipped, all-pending warning, rotation disabled when MaxRowCount ≤ 0
    - _Requirements: 13.10_
  - [x]* 35.3 Create `BootstrapOptionsValidatorTests` in `GroundUp.Tests.Unit/Services/Configuration/`
    - Test: each validation rule (missing DB connection, missing master key, short token)
    - _Requirements: 18.1, 18.2_
  - [x]* 35.4 Create `SetupOptionsValidatorTests` in `GroundUp.Tests.Unit/Services/Configuration/`
    - Test: body-size 4096 floor clamp
    - _Requirements: 18.5_
  - [x]* 35.5 Create `MasterKeyHealthCheckTests` and `BootstrapStateAwareHealthCheckTests` in `GroundUp.Tests.Unit/Api/HealthChecks/`
    - Test: setup-mode returns Healthy without inner check; complete-mode runs inner check
    - _Requirements: 7.7_

- [x] 36. Unit tests — hosted services + ICurrentUser switching + GET /setup landing
  - [x]* 36.1 Create `MigrationStartupHostedServiceTests` in `GroundUp.Tests.Unit/Services/Bootstrap/`
    - Test: migrations run during StartAsync
    - _Requirements: 5.9_
  - [x]* 36.2 Create `BootstrapTokenStartupValidatorTests` in `GroundUp.Tests.Unit/Services/Bootstrap/`
    - Test: throws when token missing and incomplete; no-op when complete
    - _Requirements: 8.6, 8.9_
  - [x]* 36.3 Create `SetupCurrentUserTests` in `GroundUp.Tests.Unit/Services/Bootstrap/`
    - Test: setup-mode produces `"setup-wizard"` sentinel via interceptor; complete-mode produces JwtCurrentUser
    - _Requirements: Cross-Cutting 8_
  - [x]* 36.4 Create `SetupLandingTests` in `GroundUp.Tests.Unit/Api/Controllers/Setup/`
    - Test: 200 in setup mode, 404 after complete, Cache-Control: no-store header
    - _Requirements: 15.1_

- [~] 37. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 38. Integration tests — encryption round-trip + bootstrap middleware + concurrent completion
  - [x]* 38.1 Create `SettingsEncryptionIntegrationTests` in `GroundUp.Tests.Integration/Setup/`
    - Test: write encrypted setting, read decrypted, verify DB row contains `aes-gcm-v1:` ciphertext
    - _Requirements: 3.1, 3.3, 3.9_
  - [x]* 38.2 Create `BootstrapModeIntegrationTests` in `GroundUp.Tests.Integration/Setup/`
    - Test: redirect in setup mode, passthrough after complete, JSON 503 on `Accept: application/json`
    - _Requirements: 7.2, 7.3, 7.4, 7.6_
  - [x]* 38.3 Create `BootstrapConcurrencyTests` in `GroundUp.Tests.Integration/Setup/`
    - Test: multiple workers calling CompleteSetupAsync — exactly one wins via xmin
    - _Requirements: 6.3, 6.4_

- [x] 39. Integration tests — full setup wizard flow + step ordering + rate limiting + health checks
  - [x]* 39.1 Create `SetupWizardIntegrationTests` in `GroundUp.Tests.Integration/Setup/`
    - Test: happy path all steps in order, verify state transitions and CreatedBy="setup-wizard"
    - _Requirements: 9–14, Cross-Cutting 8_
  - [-]* 39.2 Create `SetupStepOrderingTests` in `GroundUp.Tests.Integration/Setup/`
    - Test: skip steps → 412, repeat steps → idempotent
    - _Requirements: 16.1–16.5_
  - [x]* 39.3 Create `SetupRateLimitTests` in `GroundUp.Tests.Integration/Setup/`
    - Test: exceed limit → 429 with Retry-After, loopback bypass in dev, disabled when complete
    - _Requirements: 18.4_
  - [x]* 39.4 Create `SetupBodySizeTests` in `GroundUp.Tests.Integration/Setup/`
    - Test: oversized payload → 413, 4096-byte floor clamp respected
    - _Requirements: 18.5_
  - [ ]* 39.5 Create `HealthCheckIntegrationTests` in `GroundUp.Tests.Integration/Setup/`
    - Test: `/ready` returns `{ status: "Healthy", setupMode: true }` in setup mode
    - _Requirements: 7.7_
  - [x]* 39.6 Create `FirstAdminIdempotencyIntegrationTests` in `GroundUp.Tests.Integration/Setup/`
    - Test: re-run with same email/displayName → 200 with same userId; conflicting attributes → 409
    - _Requirements: 12.14, 12.18, 12.19_
  - [-]* 39.7 Create `SetupRecoveryIntegrationTests` in `GroundUp.Tests.Integration/Setup/`
    - Test: db-pending recovery, already-completed rejection, keycloak-pending refusal
    - _Requirements: 13.4–13.8_

- [~] 40. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The design uses C# (.NET 8) with xUnit + NSubstitute for unit tests, FsCheck.Xunit for property tests, and Testcontainers for integration tests
- `SetupWizardService` crosses the auth-module boundary ONLY via `IIdentityBootstrapService` and `IIdentityProviderAdminService` interfaces — never via direct repository injection
- The `KeycloakAdminHttpClient` is the one justified exception to the service-interface rule (the admin client doesn't exist yet at that step)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.4", "1.5", "1.6", "1.7", "1.8"] },
    { "id": 1, "tasks": ["2.1", "2.2", "2.3", "13.1", "13.2"] },
    { "id": 2, "tasks": ["3.1"] },
    { "id": 3, "tasks": ["4.1", "5.1", "7.1", "11.1", "12.1", "12.2"] },
    { "id": 4, "tasks": ["6.1", "6.2", "6.3", "6.4", "8.1", "8.2"] },
    { "id": 5, "tasks": ["9.1", "10.1", "11.2"] },
    { "id": 6, "tasks": ["15.1", "15.2", "15.3", "17.1", "17.2", "18.1", "18.2"] },
    { "id": 7, "tasks": ["16.1", "16.2"] },
    { "id": 8, "tasks": ["19.1", "19.2"] },
    { "id": 9, "tasks": ["20.1"] },
    { "id": 10, "tasks": ["21.1", "21.2", "21.3"] },
    { "id": 11, "tasks": ["22.1", "22.2", "22.3"] },
    { "id": 12, "tasks": ["24.1", "25.1", "25.2", "26.1", "26.2", "26.3"] },
    { "id": 13, "tasks": ["27.1", "27.2", "27.3"] },
    { "id": 14, "tasks": ["29.1", "29.2", "29.3", "29.4"] },
    { "id": 15, "tasks": ["30.1", "30.2", "30.3", "30.4", "30.5", "30.6"] },
    { "id": 16, "tasks": ["31.1", "31.2", "32.1", "32.2", "32.3"] },
    { "id": 17, "tasks": ["33.1", "33.2", "33.3", "34.1", "34.2", "34.3"] },
    { "id": 18, "tasks": ["35.1", "35.2", "35.3", "35.4", "35.5"] },
    { "id": 19, "tasks": ["36.1", "36.2", "36.3", "36.4"] },
    { "id": 20, "tasks": ["38.1", "38.2", "38.3"] },
    { "id": 21, "tasks": ["39.1", "39.2", "39.3", "39.4", "39.5", "39.6", "39.7"] }
  ]
}
```
