# Requirements Document

## Phase 10AB: Initial Setup & Secrets Foundation

## Introduction

Phase 10AB bridges Phases 10A and 10B. It delivers three intertwined capabilities that together let the GroundUp framework be configured WordPress-style — bootstrap from a fresh database, capture all operational configuration through a UI-driven setup wizard, and protect sensitive values with encryption at rest:

1. **Master key abstraction and encrypted-at-rest secrets**: AES-GCM encryption of `IsSecret=true` settings using a master key resolved from environment variables or a file. Plaintext values never leave the service layer.
2. **Bootstrap state machine and middleware**: A singleton `BootstrapState` row gates the application — until setup is complete, all routes redirect to the setup wizard. The bootstrap admin token is the only authentication mechanism during setup.
3. **First-run setup wizard**: Sequential JSON endpoints that capture app identity, identity provider URLs, Keycloak admin client provisioning (Path B auto-provision), and create the first super admin. Master admin Keycloak credentials never persist to the database.

This phase introduces no new authentication flows. It is the substrate that Phase 10B (Keycloak provider implementation) and beyond depend on for storing the Keycloak admin client secret and other sensitive operational configuration.

## Glossary

- **Master Key**: A 256-bit (32-byte) symmetric key used for AES-GCM encryption/decryption of secret settings. Resolved at startup from `GroundUp:MasterKeyPath` (file, preferred) or `GroundUp:MasterKey` (env var, base64). Required for the application to start.
- **IsSecret Setting**: A setting definition with `IsSecret=true` and/or `IsEncrypted=true`. `IsSecret=true` means the API never returns plaintext (returns redacted markers). `IsEncrypted=true` means the value is encrypted at rest using the master key.
- **Bootstrap State**: A singleton database row tracking whether first-run setup has been completed. Has exactly two states: incomplete (the framework is in setup mode) or complete (normal operation).
- **Bootstrap Admin Token**: A bearer token configured via `GroundUp:BootstrapAdminToken` env var. The only credential accepted during setup mode. Rejected automatically once setup completes, even if still present in environment.
- **Setup Mode**: The application state when `BootstrapState.IsComplete=false`. All non-`/setup/*`, non-health, non-static-asset routes redirect to `/setup`.
- **Path B Auto-Provisioning**: A Keycloak admin bootstrap strategy where the master admin credentials are accepted once during the wizard, used to create a long-lived realm-management service-account client, and then immediately discarded. The service-account client credentials are stored encrypted in settings and used for all subsequent admin operations.
- **Secret Reference**: A setting value prefixed with `secretref://` that, if `ISecretResolver` is registered, is resolved to a real secret from an external store (Azure Key Vault, AWS Secrets Manager, HSM). No implementations ship in 10AB.
- **AES-GCM**: Authenticated symmetric encryption algorithm. Each encryption uses a fresh nonce. Provides confidentiality, integrity, and authenticity. NIST-approved for FIPS-validated environments.
- **Self-Describing Ciphertext Format**: The encrypted form `aes-gcm-v1:{nonce-base64}:{ciphertext-base64}:{tag-base64}`. The `aes-gcm-v1` prefix identifies the algorithm version, allowing future rotation to a different scheme without losing the ability to decrypt legacy values.
- **Setup Transaction Log**: A persistent record of in-progress wizard steps so that partial failures (e.g., Keycloak user created but DB write failed) can be diagnosed and recovered via a `/setup/recover` endpoint without manual cleanup.
- **xmin Concurrency Token**: The Postgres-native `xmin` system column on a row, used as an EF Core optimistic concurrency token. Increments automatically on every UPDATE without requiring an explicit application-managed column. Configured via EF Core fluent API as `IsConcurrencyToken()` and `ValueGeneratedOnAddOrUpdate()`.

## Cross-Cutting Conventions

The following conventions apply to all requirements unless explicitly overridden by a specific acceptance criterion:

1. **Whitespace handling**: All string inputs to setup endpoints SHALL be trimmed of leading and trailing whitespace before validation. Length checks (max length, min length) SHALL apply to the trimmed value. After trimming, an empty string is treated as null/empty for required-field validation.
2. **Sensitive request body redaction**: Setup endpoints that accept passwords or secrets (`/setup/keycloak-bootstrap`, `/setup/first-admin`) SHALL NOT have their request bodies logged. Configured request body logging SHALL skip these paths or redact known secret fields (`masterAdminPassword`, `password`).
3. **URL validation**: "Valid absolute URL with http or https scheme" SHALL be validated using `Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https")`.
4. **Localization**: User-facing error messages MAY be hard-coded English strings in this phase. Resource-string localization is deferred until the framework introduces a localization story.
5. **Error response shape**: All non-2xx responses from setup endpoints SHALL use a consistent JSON shape derived from the framework's existing `OperationResult` failure conversion: `{ "code": "string", "message": "string", "details": [{ "field": "string?", "message": "string" }]? }`. The `code` SHALL be a stable machine-readable identifier (e.g., `setup_required`, `precondition_step_missing`, `keycloak_credentials_rejected`, `validation_error`, `bootstrap_state_missing`, `rate_limited`, `payload_too_large`). The `message` SHALL be human-readable English. The `details` array is optional and used for field-level validation errors. Endpoint-specific success/error semantics in individual requirements set the HTTP status code; the body shape is invariant across all setup endpoints.
6. **Path matching**: All path-prefix and path-equality matches in setup-related middleware AND wizard plumbing SHALL use case-insensitive comparisons. Prefix matches SHALL use `HttpRequest.Path.StartsWithSegments(allowed, StringComparison.OrdinalIgnoreCase)` (segment-aware, preventing `/setup-evil` from matching `/setup`). Exact matches SHALL use `string.Equals(path, allowed, StringComparison.OrdinalIgnoreCase)`. Bare `string.StartsWith` SHALL NOT be used for path comparisons.
7. **Optimistic concurrency token**: All entities introduced in this phase that participate in optimistic concurrency control SHALL use the Postgres `xmin` system column as the EF Core concurrency token, configured via `IsConcurrencyToken()` AND `ValueGeneratedOnAddOrUpdate()`. The `xmin` value SHALL NOT be exposed in DTOs or API responses.
8. **Audit field population during setup mode**: All entities implementing `IAuditable` that are written while `BootstrapState.IsComplete=false` SHALL have `CreatedBy` AND `UpdatedBy` set to the fixed sentinel value `"setup-wizard"`. THE existing `IAuditable` interceptor in `GroundUp.Data.Postgres` reads `ICurrentUser` to populate these fields; during setup mode there is no authenticated user, so an `ICurrentUser` implementation SHALL be registered that returns `"setup-wizard"` as the identity. THE specific implementation choice (e.g., extending the existing `SystemCurrentUser` from Phase 9, or introducing a new `SetupCurrentUser` registered conditionally on bootstrap state) is a design-time concern; the requirement is only that the sentinel value reaches the database via the existing interceptor mechanism.

## Requirements

### Requirement 1: Master Key Provider Abstraction

**User Story:** As a framework developer, I want a master key abstraction that resolves the encryption key at startup from environment variables or a file, so that the framework can encrypt secret settings without hard-coding the key location and can fail fast at boot if no valid key is configured.

#### Acceptance Criteria

1. WHEN the application starts THEN the framework SHALL resolve the master key in the following priority order: file path from `GroundUp:MasterKeyPath` configuration value, then base64 string from `GroundUp:MasterKey` environment variable.
2. WHERE both `GroundUp:MasterKeyPath` and `GroundUp:MasterKey` are set, the framework SHALL prefer the file path source AND log a warning that both are configured.
3. WHEN the master key source is a file path THEN the framework SHALL read the file content as base64-encoded text, trim leading and trailing whitespace (including trailing newlines), AND decode the trimmed value.
4. WHEN the master key file exists but is empty after trimming whitespace THEN the framework SHALL throw an exception during host startup with a message distinguishing "file is empty" from "file content is too short."
5. WHEN `GroundUp:MasterKeyPath` is configured AND the file at that path does not exist THEN the framework SHALL throw an exception during host startup with a message naming the configured path AND SHALL NOT silently fall back to the `GroundUp:MasterKey` env var. (Silent fallback would mask a deployment misconfiguration.)
6. WHEN the master key cannot be resolved from any source THEN the framework SHALL throw an exception during host startup with a clear error message naming the expected configuration keys.
7. WHEN the resolved master key is fewer than 32 bytes (256 bits) after base64 decoding THEN the framework SHALL throw an exception during host startup naming the actual decoded length.
8. WHEN the master key cannot be base64-decoded THEN the framework SHALL throw an exception during host startup naming the expected base64 format.
9. WHEN the master key source is a file path AND the host operating system is a Unix-like system AND the file is readable by users other than the owner THEN the framework SHALL log a warning at startup recommending file mode 0600. THE framework SHALL NOT block startup on permissive file modes.
10. THE master key provider SHALL expose its key as a `byte[]` via an `IMasterKeyProvider` interface in `GroundUp.Core.Abstractions`.
11. THE master key SHALL be cached in memory after first resolution; subsequent calls SHALL return the same byte array without re-reading the source.
12. THE default `IMasterKeyProvider` implementation SHALL be registered as a singleton in DI via the existing core service registration extension.

### Requirement 2: AES-GCM Setting Encryption Provider

**User Story:** As a framework developer, I want a default `ISettingEncryptionProvider` implementation backed by AES-GCM and the master key, so that settings flagged `IsEncrypted=true` are transparently encrypted at rest.

#### Acceptance Criteria

1. THE framework SHALL provide an `AesGcmSettingEncryptionProvider` class implementing the existing `ISettingEncryptionProvider` interface in `GroundUp.Core`.
2. WHEN encrypting a plaintext value THEN the provider SHALL generate a fresh 12-byte (96-bit) random nonce, encrypt using AES-256-GCM with the master key, and produce the output format `aes-gcm-v1:{nonce-base64}:{ciphertext-base64}:{tag-base64}`.
3. WHEN decrypting a value with prefix `aes-gcm-v1:` THE provider SHALL parse the three colon-delimited base64 segments AND decrypt using AES-256-GCM with the master key.
4. WHEN decrypting a value that does NOT match any supported version prefix (including values missing colons, values with no prefix, and values that look like plaintext) THEN the provider SHALL throw an exception naming the unsupported version prefix WITHOUT crashing on parse errors. The exception type SHALL be the same regardless of whether the input is malformed or has an unknown prefix.
5. WHEN the authentication tag verification fails during decryption THEN the provider SHALL throw an exception indicating ciphertext tampering or wrong key.
6. WHEN encrypting OR decrypting a value that is null, empty, OR whitespace-only THEN the provider SHALL throw `ArgumentException` naming the offending parameter (`plaintext` or `ciphertext`). THE provider SHALL NOT silently return null, empty, or any sentinel for these inputs. Callers (notably `SettingsService` per Requirement 3) are responsible for short-circuiting null/empty/whitespace values before invoking the provider so that the provider's contract remains "valid input → valid output, invalid input → fail loudly."
7. THE encryption provider SHALL use a fresh nonce per encryption operation; the same plaintext encrypted twice SHALL produce different ciphertexts.
8. THE encryption provider SHALL be registered as a singleton in DI by default, replacing any previously registered `ISettingEncryptionProvider`.
9. THE `AddGroundUpSettings()` extension SHALL register `AesGcmSettingEncryptionProvider` as the default `ISettingEncryptionProvider` only if no other implementation is already registered.

### Requirement 3: Setting Service Encryption Round-Trip

**User Story:** As a system administrator, I want the settings service to transparently encrypt secret values on save and decrypt them on load, so that I never need to handle ciphertext directly and plaintext is never stored in the database for IsEncrypted settings.

#### Acceptance Criteria

1. WHEN `ISettingsService.SetAsync` persists a value AND the corresponding `SettingDefinition` has `IsEncrypted=true` AND the supplied value is non-null, non-empty, AND non-whitespace THEN the service SHALL encrypt the value via `ISettingEncryptionProvider` before writing to the database.
2. WHEN `ISettingsService.SetAsync` is called AND the supplied value is null, empty, OR whitespace-only THEN the service SHALL persist the value as `null` regardless of the `IsEncrypted` flag, AND SHALL NOT invoke `ISettingEncryptionProvider.Encrypt`. (The encryption provider's contract per Requirement 2.6 is that null/empty/whitespace inputs throw; `SettingsService` short-circuits before that contract is violated.)
3. WHEN `ISettingsService.GetAsync` loads a value AND the corresponding `SettingDefinition` has `IsEncrypted=true` AND the stored value is non-null, non-empty, AND non-whitespace THEN the service SHALL decrypt the value via `ISettingEncryptionProvider` after reading from the database.
4. WHEN `ISettingsService.GetAsync` reads a stored value that is null, empty, OR whitespace-only THEN the service SHALL return null (or the definition's default-value fallback per existing Phase 6 behavior) WITHOUT invoking `ISettingEncryptionProvider.Decrypt`.
5. WHILE `ISettingEncryptionProvider` is not registered AND a setting has `IsEncrypted=true` AND the value being written is non-null/non-empty/non-whitespace, the service SHALL fail the `SetAsync` call with a clear error message naming the missing provider.
6. WHILE `ISettingEncryptionProvider` is not registered AND a setting has `IsEncrypted=true` AND the stored value being read is non-null/non-empty/non-whitespace, the service SHALL fail the `GetAsync` call with a clear error message naming the missing provider.
7. WHEN a setting has `IsSecret=true` AND `IsEncrypted=false` THEN the service SHALL still mask the value in API responses (existing Phase 6 behavior, preserved). IF the existing Phase 6 implementation does not yet mask `IsSecret=true` values in `SettingsController` responses THEN this phase SHALL add the masking behavior to the settings service AND controller as a prerequisite for shipping `IsEncrypted` settings safely. The masking implementation SHALL replace the value with the literal string `"***REDACTED***"` while preserving the surrounding DTO shape. THE existing `SettingsService.SecretMask` constant (currently `"••••••••"`) SHALL be updated to `"***REDACTED***"` AND any Phase 6 unit tests asserting on the old mask SHALL be updated.
8. WHEN a setting has `IsSecret=true` AND `IsEncrypted=true` THEN the service SHALL both encrypt the value at rest AND mask the value in API responses.
9. THE encryption SHALL be transparent to API consumers — DTOs and request/response shapes do not change.
10. THE existing `ISettingEncryptionProvider` interface signature (`string Encrypt(string)`, `string Decrypt(string)`) SHALL remain unchanged. Null/empty/whitespace handling lives in `SettingsService`, not in the interface contract — this preserves backward compatibility with any existing implementations of the interface and keeps the provider's contract focused on "encrypt/decrypt valid strings."

### Requirement 4: Secret Reference Resolver Interface

**User Story:** As a framework developer, I want an `ISecretResolver` interface for routing setting values prefixed `secretref://` through external secret stores, so that consuming applications can integrate Azure Key Vault, AWS Secrets Manager, or HSM without modifying the framework.

#### Acceptance Criteria

1. THE framework SHALL define an `ISecretResolver` interface in `GroundUp.Core.Abstractions` with a single method `Task<string?> ResolveAsync(string secretRef, CancellationToken cancellationToken = default)`.
2. WHEN a setting value is being read via `ISettingsService.GetAsync` AND the value starts with `secretref://` AND `ISecretResolver` is registered AND the resolver does not return null THEN the service SHALL return the resolved secret value.
3. WHEN a setting value is being read AND the value starts with `secretref://` AND `ISecretResolver` is NOT registered THEN `ISettingsService` SHALL return the value verbatim (treat as a literal string) WITHOUT logging at info level. This literal-string fallback SHALL apply only to read operations; write operations always persist the literal as-is regardless.
4. WHEN the registered `ISecretResolver` returns null for a `secretref://` value THEN `ISettingsService.GetAsync` SHALL return a failure result indicating the secret could not be resolved.
5. THE 10AB phase SHALL NOT ship any `ISecretResolver` implementation. Azure Key Vault, AWS Secrets Manager, and HSM implementations are explicitly out of scope.
6. THE secret reference resolution SHALL apply only to read paths (`GetAsync`, `GetAllForScopeAsync`, `GetGroupAsync`, `GetTypedValueAsync`). Write paths (`SetAsync`) SHALL persist the `secretref://` literal as-is — they do not resolve before storing.
7. WHEN a `secretref://` value is read AND the setting also has `IsEncrypted=true` THEN the service SHALL decrypt first and then resolve the secret reference.
8. THE secret reference resolution SHALL be single-pass. WHEN `ISecretResolver.ResolveAsync` returns a value that itself starts with `secretref://` THEN that returned value SHALL be treated as the final, literal resolved value AND SHALL NOT be re-resolved. (Chained/recursive secret references are explicitly out of scope to prevent infinite loops and to keep the resolver contract simple.)

### Requirement 5: Bootstrap State Entity and Persistence

**User Story:** As a framework operator, I want a `BootstrapState` singleton entity that tracks whether first-run setup is complete, so that the framework can route requests differently before and after initial configuration.

#### Acceptance Criteria

1. THE framework SHALL define a `BootstrapState` entity in `GroundUp.Core.Entities` with: `Id` (Guid, fixed sentinel value `00000000-0000-0000-0000-000000000001`), `IsComplete` (bool, default false), `CompletedAt` (DateTime?, default null), `CompletedBy` (Guid?, default null), audit fields from `IAuditable`. THE entity SHALL participate in optimistic concurrency control via the Postgres `xmin` system column (per cross-cutting convention 7).
2. THE `BootstrapState` table SHALL enforce the singleton invariant at the database level via a CHECK constraint requiring `Id` equal the sentinel value AND a unique constraint on `Id`.
3. THE EF migration creating the `BootstrapState` table SHALL pre-insert the singleton row with `Id` equal the sentinel value, `IsComplete=false`, AND `CreatedAt=NOW()` so that no application-level lazy initialization is required.
4. WHEN the framework reads `BootstrapState` at request time AND the singleton row is missing (e.g., manual deletion after startup) THEN the bootstrap-mode middleware AND every wizard endpoint SHALL respond with HTTP 503 with body `{ "code": "bootstrap_state_missing", "message": "Bootstrap state row is missing — restore from a database backup or re-run migration {migration-name}." }` AND log the failure at Critical level. THE framework SHALL NOT attempt to lazy-recreate the row at request time, since lazy creation would conflict with the singleton invariant if multiple workers raced.
5. WHEN setup completes successfully THEN the service SHALL update the existing row's `IsComplete=true`, set `CompletedAt=UtcNow`, set `CompletedBy=` the provisioned admin user's Id, AND save changes.
6. THERE SHALL BE NO API endpoint exposed for resetting `BootstrapState.IsComplete` from true back to false. Re-entering setup mode requires direct database manipulation.
7. THE `BootstrapState` entity SHALL be configured in the main `GroundUpDbContext` (the framework-level context, not the auth context).
8. THE EF migration creating the `BootstrapState` table SHALL be added to `GroundUp.Data.Postgres/Migrations`.
9. THE framework SHALL run EF Core database migrations during host startup before the bootstrap-mode middleware is invoked.
10. WHEN the database is unreachable during a bootstrap-mode middleware invocation THEN the middleware SHALL respond with HTTP 503 (Service Unavailable) AND log the failure at Error level. The middleware SHALL NOT propagate the database exception or short-circuit to a 500.

### Requirement 6: Bootstrap State Service

**User Story:** As a framework component, I want an `IBootstrapStateService` that exposes the current bootstrap state with caching, so that the bootstrap-mode middleware can check setup completion on every request without hitting the database every time.

#### Acceptance Criteria

1. THE framework SHALL define an `IBootstrapStateService` interface in `GroundUp.Core.Abstractions` with methods: `Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default)`, `Task<OperationResult> CompleteSetupAsync(Guid completedByUserId, CancellationToken cancellationToken = default)`, and `void InvalidateCache()`.
2. WHEN `IsCompleteAsync` is called AND a cached value exists AND its cache entry has not exceeded the TTL of 60 seconds THEN the service SHALL return the cached value WITHOUT a database read. WHEN no cached value exists OR the cache entry has expired THEN the service SHALL read from the `BootstrapState` row, refresh the cache with a 60-second absolute expiration, AND return the value. THE service SHALL NOT attempt to "verify" a cached value against the database — the cache is authoritative within its TTL.
3. WHEN `CompleteSetupAsync` is called AND any condition prevents completion (current state is already `IsComplete=true`, prerequisite settings missing, no super admin user exists, or concurrent completion attempt detected via the `xmin` concurrency token) THEN the service SHALL return a `Conflict` failure indicating the specific condition that blocked completion.
4. WHEN `CompleteSetupAsync` is called AND the current bootstrap state is `IsComplete=false` THEN the service SHALL update the row to `IsComplete=true`, set `CompletedAt=UtcNow`, set `CompletedBy=completedByUserId`, save changes (which causes the `xmin` token to advance, providing optimistic concurrency safety), AND invalidate the cache.
5. WHEN `InvalidateCache` is called THE service SHALL evict the cached value immediately within the local instance.
6. THE multi-instance cache invalidation lag SHALL be accepted as a known limitation: when one instance calls `CompleteSetupAsync`, other instances may continue to see `IsComplete=false` for up to the cache TTL (60 seconds). Setup is a one-time event so this brief inconsistency window is acceptable. THE limitation SHALL be documented in the bootstrap state service's XML comments AND in the design doc.
7. THE service SHALL be registered as scoped in DI.
8. THE service SHALL use `IMemoryCache` for caching with key `"groundup:bootstrap-state"`.

### Requirement 7: Bootstrap Mode Middleware

**User Story:** As a framework operator, I want a middleware that redirects all non-setup routes to `/setup` while bootstrap is incomplete, so that the application cannot be used productively until first-run configuration finishes.

#### Acceptance Criteria

1. THE framework SHALL provide a `BootstrapModeMiddleware` in `GroundUp.Api` registered via `UseGroundUpBootstrapMode()` extension method.
2. WHEN the middleware processes a request AND `IBootstrapStateService.IsCompleteAsync` returns `true` THEN the middleware SHALL invoke the next middleware unchanged.
3. WHEN the middleware processes a request AND `IBootstrapStateService.IsCompleteAsync` returns `false` AND the request path matches one of the allowed paths THEN the middleware SHALL invoke the next middleware unchanged. Allowed paths are: paths starting with `/setup/` (segment-aware), exact path `/setup`, exact path `/health`, exact path `/ready`, paths starting with `/_framework/` (Blazor static assets), paths starting with `/css/`, paths starting with `/js/`, paths starting with `/images/`, AND paths starting with `/lib/`. ALL path comparisons SHALL be case-insensitive AND segment-aware per cross-cutting convention 6.
4. WHEN the middleware processes a request AND `IBootstrapStateService.IsCompleteAsync` returns `false` AND the request path does NOT match any allowed path THEN the middleware SHALL respond with HTTP 302 redirect to `/setup` AND short-circuit the pipeline.
5. THE middleware SHALL be added to the request pipeline before authentication middleware AND after exception handling middleware AND after correlation ID middleware.
6. WHEN the middleware processes a request that has an `Accept` header containing the media type `application/json` (matching either as the highest-priority media type or as one of the listed types in a comma-delimited list) AND would otherwise redirect THEN the middleware SHALL return HTTP 503 with body `{ "code": "setup_required", "message": "Application setup is not yet complete. Please complete setup at /setup." }` instead of 302 redirect.
7. WHEN the middleware would normally redirect AND the request path is `/health` or `/ready` THEN the middleware SHALL pass the request through unchanged. THE healthcheck implementations registered via `AddGroundUpHealthChecks()` SHALL only validate infrastructure dependencies (database connectivity, master key resolved successfully) during setup mode AND SHALL NOT validate configured-resource availability (Keycloak reachability, configured external services) until `BootstrapState.IsComplete=true`. WHEN the application is in setup mode AND infrastructure deps are healthy THEN `/ready` SHALL return HTTP 200 with body `{ "status": "Healthy", "setupMode": true }`. THE 200 status reflects that the pod IS ready to serve setup traffic; routing decisions about whether to send user traffic to a setup-mode pod are upstream concerns (load balancer / ingress).
8. THE middleware SHALL log the FIRST observation of setup mode after process startup at Information level with message "Application is in setup mode; non-setup routes will redirect to /setup". Subsequent per-request redirect/blocked decisions SHALL be logged at Debug level with the request path AND the bootstrap state.

### Requirement 8: Bootstrap Admin Token Authentication

**User Story:** As a framework operator, I want a one-time bootstrap admin token configured via environment variable that authenticates setup wizard requests, so that there is exactly one credential needed during initial setup and that credential becomes invalid the moment setup completes.

#### Acceptance Criteria

1. THE framework SHALL provide a `BootstrapAdminTokenAuthenticationHandler` registered as the default scheme for `/setup/*` routes.
2. WHEN the handler validates a request AND the `Authorization` header has format `Bearer {token}` AND the token equals `GroundUp:BootstrapAdminToken` configuration value AND `IBootstrapStateService.IsCompleteAsync` returns `false` THEN the handler SHALL succeed authentication with claim `bootstrap-admin`.
3. WHEN the handler validates a request AND `IBootstrapStateService.IsCompleteAsync` returns `true` THEN the handler SHALL fail authentication with reason "Setup is already complete; bootstrap token is no longer accepted."
4. WHEN the handler validates a request AND the `Authorization` header is missing THEN the handler SHALL fail authentication with reason "Missing Authorization header."
5. WHEN the handler validates a request AND the token does not match `GroundUp:BootstrapAdminToken` THEN the handler SHALL fail authentication with reason "Invalid bootstrap admin token."
6. WHEN `GroundUp:BootstrapAdminToken` is not configured AND the application is in bootstrap mode THEN the framework SHALL throw an exception during host startup naming the missing configuration key.
7. WHEN `GroundUp:BootstrapAdminToken` is configured AND its value is shorter than 32 characters THEN the framework SHALL throw an exception during host startup with a message recommending a longer token.
8. THE token comparison SHALL use a constant-time equality check to prevent timing side-channel attacks.
9. WHEN `BootstrapState.IsComplete=true` THEN the `GroundUp:BootstrapAdminToken` configuration value SHALL NOT be required at host startup. Operators MAY remove the env var from the deployment without restarting the application AND SHALL face no startup failure on subsequent restarts. THE handler SHALL reject all requests in this state regardless of whether the token is present (per criterion 3).

### Requirement 9: Setup Wizard — App Identity Step

**User Story:** As a system administrator running first-run setup, I want a wizard step that captures the application name and default domain, so that the framework knows how to display itself and which registrable domain owns auth cookies.

#### Acceptance Criteria

1. THE framework SHALL expose `POST /setup/app-identity` accepting JSON body `{ "applicationName": "string", "defaultDomain": "string" }`.
2. THE endpoint SHALL require successful `BootstrapAdminTokenAuthenticationHandler` authentication.
3. WHEN the endpoint receives a valid request THEN the service SHALL trim leading and trailing whitespace from `applicationName` AND `defaultDomain` before persisting AND validation, persist `applicationName` to setting key `app.identity.name`, AND persist `defaultDomain` to setting key `auth.application.default-domain`.
4. WHEN trimmed `applicationName` is null or empty THEN the endpoint SHALL return HTTP 400 with validation error.
5. WHEN trimmed `applicationName` exceeds 200 characters THEN the endpoint SHALL return HTTP 400 with validation error.
6. WHEN trimmed `defaultDomain` is provided AND fails domain format validation (regex `^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*$`) THEN the endpoint SHALL return HTTP 400 with validation error.
7. WHEN trimmed `defaultDomain` is null or empty THEN the endpoint SHALL persist an empty string (host-only cookie configuration).
8. WHEN the step is invoked multiple times THEN each invocation SHALL overwrite the previous values (idempotent and re-runnable until setup completes).
9. THE endpoint SHALL return HTTP 200 with body `{ "step": "app-identity", "completed": true }` on success.

### Requirement 10: Setup Wizard — Identity Provider Configuration Step

**User Story:** As a system administrator running first-run setup, I want a wizard step that captures Keycloak URLs and the shared realm name, so that the auth module knows where to redirect users and how to talk to Keycloak's admin API.

#### Acceptance Criteria

1. THE framework SHALL expose `POST /setup/identity-provider` accepting JSON body `{ "publicBaseUrl": "string", "internalBaseUrl": "string", "sharedRealmName": "string" }`.
2. THE endpoint SHALL require successful `BootstrapAdminTokenAuthenticationHandler` authentication.
3. WHEN the endpoint receives a valid request THEN the service SHALL trim leading and trailing whitespace from all string inputs before validation AND persistence, then persist values to settings: `publicBaseUrl` → `auth.keycloak.public-base-url`, `internalBaseUrl` → `auth.keycloak.internal-base-url`, `sharedRealmName` → `auth.keycloak.shared-realm-name`.
4. WHEN trimmed `publicBaseUrl` is null or empty THEN the endpoint SHALL return HTTP 400 with validation error.
5. WHEN `publicBaseUrl` is not a valid absolute URL with `http` or `https` scheme (validated using `Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https")`) THEN the endpoint SHALL return HTTP 400 with validation error.
6. WHEN trimmed `internalBaseUrl` is null or empty THEN the service SHALL default it to the value of `publicBaseUrl` (single-host development scenario). This defaulting SHALL be applied per-call: every invocation of this endpoint SHALL fully overwrite both stored settings using the request's values (with the per-call default substitution), so an earlier non-empty `internalBaseUrl` is discarded if the next call submits an empty `internalBaseUrl`.
7. WHEN `internalBaseUrl` is provided AND is not a valid absolute URL with `http` or `https` scheme (same validation as `publicBaseUrl`) THEN the endpoint SHALL return HTTP 400 with validation error.
8. WHEN trimmed `sharedRealmName` is null or empty THEN the endpoint SHALL return HTTP 400 with validation error.
9. WHEN trimmed `sharedRealmName` exceeds 128 characters THEN the endpoint SHALL return HTTP 400 with validation error.
10. WHEN the step is invoked multiple times THEN each invocation SHALL overwrite the previous values.
11. THE endpoint SHALL return HTTP 200 with body `{ "step": "identity-provider", "completed": true }` on success.

### Requirement 11: Setup Wizard — Keycloak Realm Bootstrap Step (Path B)

**User Story:** As a system administrator running first-run setup, I want a wizard step that uses my Keycloak master admin credentials once to provision a long-lived service-account client, so that the framework has admin API access without my master credentials being persisted.

#### Acceptance Criteria

1. THE framework SHALL expose `POST /setup/keycloak-bootstrap` accepting JSON body `{ "masterAdminUsername": "string?", "masterAdminPassword": "string?" }`.
2. THE endpoint SHALL require successful `BootstrapAdminTokenAuthenticationHandler` authentication.
3. WHEN `masterAdminUsername` is not provided in the request body THEN the service SHALL fall back to `GroundUp:Keycloak:BootstrapAdminUsername` configuration value.
4. WHEN `masterAdminPassword` is not provided in the request body THEN the service SHALL fall back to `GroundUp:Keycloak:BootstrapAdminPassword` configuration value.
5. WHEN neither the request body nor configuration provides credentials THEN the endpoint SHALL return HTTP 400 with error "Keycloak master admin credentials are required for bootstrap."
6. WHEN the step is invoked AND `auth.keycloak.public-base-url` setting is not yet configured THEN the endpoint SHALL return HTTP 412 (Precondition Failed) with error "Identity provider configuration must be completed first."
7. WHEN the step authenticates to Keycloak AND obtains an admin token THEN the service SHALL: (a) check if a client with `clientId=groundup-admin-client` exists in the configured shared realm; (b) if it exists, retrieve and reuse it; (c) if not, create it as a confidential service-account client with the following minimum role mappings from the realm's `realm-management` client: `manage-users`, `manage-clients`, `manage-realm`, `view-realm`, `view-users`, `view-clients`, `query-users`, `query-clients`. The service SHALL NOT grant master-realm or other-realm admin roles.
8. WHEN the realm-management client is provisioned successfully THEN the service SHALL retrieve its `client-secret` AND persist it to setting `auth.keycloak.admin-client-secret` with `IsEncrypted=true`.
9. WHEN the realm-management client is provisioned successfully THEN the service SHALL persist its `clientId` to setting `auth.keycloak.admin-client-id`.
10. WHEN any Keycloak API call fails with HTTP 401 or 403 THEN the endpoint SHALL return HTTP 400 with error "Keycloak master admin credentials were rejected by Keycloak."
11. WHEN any Keycloak API call fails with a different error THEN the endpoint SHALL return HTTP 502 with error "Keycloak responded with an error during bootstrap" AND log the underlying failure at Error level.
12. THE Keycloak HTTP client SHALL use a 30-second timeout per request AND SHALL retry once on HTTP 5xx or transient network failures. The endpoint SHALL NOT retry on 4xx responses.
13. WHEN the step succeeds THEN the service SHALL discard the master admin credentials from memory immediately. The credentials SHALL NEVER be written to the database, log files, or response body.
14. WHEN the step is invoked multiple times THEN each invocation SHALL be idempotent — re-running with the same credentials reuses the existing client without creating a duplicate. ON every invocation (including idempotent retries), the service SHALL re-validate the existing client's service-account role mappings against the required minimum set named in criterion 7. WHEN required roles are missing on the existing client THEN the service SHALL re-add them in this same call. WHEN re-adding required roles fails THEN the endpoint SHALL fail per criteria 10–11 instead of silently leaving the client misconfigured.
15. THE minimum required `realm-management` role list (criterion 7) is the smallest known-sufficient set as of phase 10AB. THE design phase MAY adjust this list if subsequent integration testing reveals additional roles are required for the wizard's first-admin step OR for runtime auth operations introduced in 10B. Such adjustments SHALL be reflected here before the implementation work begins.
16. WHEN the step is invoked AND `app.identity.name` setting is not yet configured THEN the endpoint SHALL return HTTP 412 (Precondition Failed) with error "App identity step must be completed before Keycloak bootstrap."
17. THE endpoint SHALL return HTTP 200 with body `{ "step": "keycloak-bootstrap", "completed": true, "clientId": "groundup-admin-client" }` on success. The body SHALL NOT include the client secret.

### Requirement 12: Setup Wizard — First Super Admin Step

**User Story:** As a system administrator running first-run setup, I want a wizard step that creates the first super admin user in both Keycloak and the GroundUp database, so that I have a permanent administrative account once setup completes.

#### Acceptance Criteria

1. THE framework SHALL expose `POST /setup/first-admin` accepting JSON body `{ "email": "string", "displayName": "string", "password": "string" }`.
2. THE endpoint SHALL require successful `BootstrapAdminTokenAuthenticationHandler` authentication.
3. WHEN the step is invoked AND `auth.keycloak.admin-client-secret` setting is not yet configured THEN the endpoint SHALL return HTTP 412 (Precondition Failed) with error "Keycloak admin client must be provisioned first."
4. WHEN the step is invoked AND the system tenant does not exist in the database THEN the endpoint SHALL return HTTP 500 with error "System tenant is missing — verify the framework startup seeders have run successfully." THE service SHALL NOT lazy-initialize the system tenant at request time; that is a startup responsibility. WHEN the SuperAdmin role does not exist in the database THEN the endpoint SHALL return HTTP 500 with error "SuperAdmin role is missing — verify the auth seeders have run."
5. WHEN `email` is null, empty after trimming, or fails RFC-5322 format validation THEN the endpoint SHALL return HTTP 400 with validation error.
6. WHEN `displayName` is null, empty after trimming, or whitespace THEN the endpoint SHALL return HTTP 400 with validation error.
7. WHEN trimmed `displayName` exceeds 200 characters THEN the endpoint SHALL return HTTP 400 with validation error.
8. WHEN `password` is fewer than 12 characters THEN the endpoint SHALL return HTTP 400 with validation error.
9. WHEN `password` does not contain at least one uppercase letter (Unicode category Lu), at least one lowercase letter (Unicode category Ll), at least one digit (Unicode category Nd), AND at least one non-alphanumeric character (any Unicode code point that is not in categories Lu, Ll, Lt, Lm, Lo, Nd, Nl, or No) THEN the endpoint SHALL return HTTP 400 with validation error. THE non-alphanumeric requirement SHALL accept any of the typical symbol characters including but not limited to `!@#$%^&*()_+-=[]{}|;:,.<>?/\\~\`'"` AND the space character.
10. WHEN the step authenticates to Keycloak using the provisioned admin client AND creates a user with the provided email, display name, and password in the configured shared realm THEN the service SHALL retrieve the Keycloak-assigned external user ID. THE Keycloak HTTP client SHALL use a 30-second timeout per request AND SHALL retry once on HTTP 5xx or transient network failures.
11. BEFORE making the Keycloak user creation call, the service SHALL write a `SetupTransactionLog` row with `Operation="first-admin-create"`, `Stage="keycloak-pending"`, `Email`, `Timestamp=UtcNow`, AND `CorrelationId`.
12. AFTER receiving a successful Keycloak response with the external user ID, the service SHALL update the transaction log row to `Stage="db-pending"` with the external user ID stored.
13. AFTER all database writes succeed (User row, UserTenant membership, SuperAdmin role assignment), the service SHALL update the transaction log row to `Stage="completed"`.
14. WHEN any Keycloak API call fails with HTTP 409 (user already exists with that email) AND the existing Keycloak user has the same email AND the same display name THEN the endpoint SHALL be idempotent: retrieve the existing user, complete the database writes if missing (using explicit existence checks, see criterion 21), AND return HTTP 200 with the existing user's ID.
15. WHEN any Keycloak API call fails with HTTP 409 (user already exists with that email) AND the existing user does NOT match the requested display name THEN the endpoint SHALL return HTTP 409 with error "A different user already exists with that email in Keycloak; resolve manually before retrying."
16. WHEN any Keycloak API call fails with a different error THEN the endpoint SHALL return HTTP 502 with error "Keycloak responded with an error" AND log the underlying failure.
17. WHEN any database operation fails after the Keycloak user has been created THEN the service SHALL leave the `SetupTransactionLog` row at `Stage="db-pending"` with the external user ID stored AND return HTTP 500 with error "Failed to complete super admin creation; partial state recorded for recovery." The endpoint SHALL NOT attempt automatic compensation.
18. WHEN the step is invoked multiple times AND a super admin already exists in the database with a matching email AND a matching display name THEN the endpoint SHALL be idempotent: return HTTP 200 with the existing user's ID.
19. WHEN the step is invoked multiple times AND a super admin already exists with a different email OR a different display name (for the same email) THEN the endpoint SHALL return HTTP 409 with error "A super admin user already exists with conflicting attributes."
20. THE first super admin SHALL be created exactly once per setup session in steady state, but retries that match the same email/displayName/password SHALL succeed idempotently.
21. THE database operations (User row + UserTenant membership + SuperAdmin role assignment) SHALL be wrapped in a single database transaction. WITHIN the transaction, the service SHALL use explicit existence checks before each insert (e.g., `SELECT 1 FROM "Users" WHERE Id = @id`, then conditional insert) so that the idempotent retry path does NOT trigger primary-key or unique-constraint violations on rows that already exist from a prior partial run. THE `UserTenant(UserId, TenantId)` unique constraint SHALL still exist as a defensive backstop against concurrent inserts that bypass the existence check, AND the unique-violation error SHALL be translated into the same `Conflict` failure shape as criterion 19.
22. THE endpoint SHALL return HTTP 200 with body `{ "step": "first-admin", "completed": true, "userId": "{guid}", "email": "string" }` on success. The body SHALL NOT include the password.
23. THE database operations (User row + UserTenant membership + SuperAdmin role assignment) SHALL be performed via a service-layer contract — `SetupWizardService` SHALL NOT inject auth-module repositories directly. Per the framework's strict layered architecture, cross-module work SHALL go through service interfaces. THE auth module SHALL expose an `IIdentityBootstrapService` interface in `GroundUp.Auth.Services` (under namespace `GroundUp.Auth.Services.Bootstrap`) with at least one method:
    - `Task<OperationResult<BootstrapAdminResultDto>> ProvisionFirstSuperAdminAsync(ProvisionFirstSuperAdminRequest request, CancellationToken cancellationToken)` — accepts the email, display name, and Keycloak external user ID; performs the User + UserTenant + SuperAdmin role assignment inside a single database transaction with explicit existence checks (per criterion 21); returns the resulting user ID.
24. THE `IIdentityBootstrapService` interface SHALL be the only auth-module dependency taken by `SetupWizardService` for the first-admin step. THE Keycloak user creation call SHALL also go through an existing service contract (the `IIdentityProviderAdminService.ProvisionUserAsync` introduced in Phase 10A), NOT a hand-rolled Keycloak HTTP client inside the setup module — this preserves the layering and avoids duplicating the Keycloak provisioning logic that 10B will implement against the same contract.
25. WHEN the auth module's `IIdentityBootstrapService` returns a failure result THEN the setup wizard SHALL propagate that failure to the caller using the same HTTP status mapping as the rest of this requirement (validation errors → 400, conflicts → 409, internal errors → 500).
26. THE `IIdentityBootstrapService.ProvisionFirstSuperAdminAsync` method SHALL be idempotent in the same way criteria 14, 18, and 21 describe — it MAY be called multiple times with the same inputs and SHALL return success with the existing user's ID rather than creating duplicates.

### Requirement 13: Setup Transaction Log and Recovery

**User Story:** As a system administrator who hit a partial-failure during the first-admin step, I want to see what got created where and how to recover, so that I can finish setup without manually digging through Keycloak admin UI and the database.

#### Acceptance Criteria

1. THE framework SHALL define a `SetupTransactionLog` entity in `GroundUp.Core.Entities` with: `Id` (UUID v7, inherited from `BaseEntity`), `Operation` (string, e.g., "first-admin-create"), `Stage` (string, e.g., "keycloak-pending", "db-pending", "completed", "failed"), `CorrelationId` (string), `ExternalUserId` (string?, set after Keycloak success), `Email` (string?, the operation's user email if applicable), `ErrorMessage` (string?, set if `Stage="failed"`), audit fields from `IAuditable`.
2. THE EF migration creating the `SetupTransactionLog` table SHALL be added in the same migration as `BootstrapState`.
3. THE framework SHALL expose `GET /setup/transaction-log` returning the most recent 20 transaction log rows ordered by `CreatedAt DESC`. THE endpoint SHALL require successful `BootstrapAdminTokenAuthenticationHandler` authentication.
4. THE framework SHALL expose `POST /setup/recover/{transactionLogId}` accepting an empty body. THE endpoint SHALL require successful `BootstrapAdminTokenAuthenticationHandler` authentication.
5. WHEN `/setup/recover/{transactionLogId}` is invoked AND the row's `Stage="db-pending"` AND the row's `Operation="first-admin-create"` THEN the service SHALL: (a) re-attempt the database writes (User row, UserTenant membership, SuperAdmin role assignment) using the stored `ExternalUserId` and `Email`; (b) update the row to `Stage="completed"` on success; (c) return HTTP 200 with `{ "recovered": true, "userId": "{guid}" }`.
6. WHEN `/setup/recover/{transactionLogId}` is invoked AND the row's `Stage="completed"` THEN the endpoint SHALL return HTTP 409 with error "Transaction log entry is already completed."
7. WHEN `/setup/recover/{transactionLogId}` is invoked AND the row's `Stage="keycloak-pending"` THEN the endpoint SHALL return HTTP 409 with error "Cannot auto-recover from keycloak-pending state; the partial Keycloak resource (if any) must be cleaned up manually before retrying the original step."
8. WHEN `/setup/recover/{transactionLogId}` is invoked AND the database recovery fails THEN the service SHALL update the row's `Stage="failed"` with the error message AND return HTTP 500.
9. THE `SetupTransactionLog` table SHALL be retained after setup completes (do not auto-delete on `BootstrapState.IsComplete=true`) so that operators can audit the setup process post-hoc.
10. THE framework SHALL define a configuration setting `GroundUp:SetupTransactionLog:MaxRowCount` (integer) with a default value of 1000. WHEN a new transaction log row is being inserted AND the current row count is at or above `MaxRowCount` THEN the service SHALL delete the oldest row(s) (ordered by `CreatedAt` ASC) such that after the new insert the table contains exactly `MaxRowCount` rows. THE rotation SHALL run inside the same transaction as the insert. WHEN `MaxRowCount` is set to 0 or a negative value THEN rotation SHALL be disabled (unbounded growth, intended only for diagnostic deployments). THE rotation SHALL never delete rows whose `Stage` is `"keycloak-pending"` or `"db-pending"` regardless of age, since those rows represent unresolved partial state. WHEN rotation cannot proceed because all candidate rows for deletion are in pending stages THEN the insert SHALL still proceed AND a Warning SHALL be logged naming the table size and pending-row count.

### Requirement 14: Setup Wizard — Complete Step

**User Story:** As a system administrator running first-run setup, I want a final wizard step that marks setup as complete, so that the bootstrap admin token is invalidated and the framework transitions out of setup mode.

#### Acceptance Criteria

1. THE framework SHALL expose `POST /setup/complete` accepting empty JSON body `{}`.
2. THE endpoint SHALL require successful `BootstrapAdminTokenAuthenticationHandler` authentication.
3. WHEN the step is invoked AND any prior step's settings are missing (`app.identity.name`, `auth.application.default-domain`, `auth.keycloak.public-base-url`, `auth.keycloak.shared-realm-name`, `auth.keycloak.admin-client-secret`) THEN the endpoint SHALL return HTTP 412 (Precondition Failed) with error naming the missing settings.
4. WHEN the step is invoked AND no super admin user exists in the GroundUp database THEN the endpoint SHALL return HTTP 412 with error "First super admin must be created before completing setup."
5. WHEN preconditions pass THEN the service SHALL call `IBootstrapStateService.CompleteSetupAsync` with the super admin's user ID.
6. WHEN `CompleteSetupAsync` succeeds THEN the bootstrap admin token SHALL be rejected on subsequent requests (verified by `BootstrapAdminTokenAuthenticationHandler` checking `IsCompleteAsync`).
7. THE endpoint SHALL return HTTP 200 with body `{ "step": "complete", "completed": true, "redirectTo": "/" }` on success.
8. WHEN the step is invoked AND `IBootstrapStateService.IsCompleteAsync` already returns true THEN the endpoint SHALL return HTTP 409 with error "Setup is already complete."

### Requirement 15: Setup Wizard — Status Endpoint

**User Story:** As a system administrator running first-run setup, I want to query the current wizard state, so that I know which steps remain and can resume where I left off.

#### Acceptance Criteria

1. THE framework SHALL expose `GET /setup/status` accepting no body.
2. THE endpoint SHALL require successful `BootstrapAdminTokenAuthenticationHandler` authentication.
3. WHEN the endpoint is invoked THEN the service SHALL return JSON `{ "isComplete": bool, "currentStep": string, "appIdentityCompleted": bool, "identityProviderCompleted": bool, "keycloakBootstrapCompleted": bool, "firstAdminCompleted": bool, "firstAdminPending": bool, "firstAdminPendingTransactionLogId": "string?" }`. The two `firstAdminPending*` fields SHALL be present in every response: `firstAdminPending` defaults to `false` AND `firstAdminPendingTransactionLogId` defaults to `null` unless criterion 7 detects a non-completed transaction log row.
4. THE `appIdentityCompleted` flag SHALL be true when both `app.identity.name` AND `auth.application.default-domain` settings exist with non-null values (`auth.application.default-domain` may be empty string).
5. THE `identityProviderCompleted` flag SHALL be true when `auth.keycloak.public-base-url` AND `auth.keycloak.shared-realm-name` settings exist with non-null values.
6. THE `keycloakBootstrapCompleted` flag SHALL be true when `auth.keycloak.admin-client-id` AND `auth.keycloak.admin-client-secret` settings exist with non-null values.
7. THE `firstAdminCompleted` flag SHALL be true when at least one user with the `SuperAdmin` role exists in the GroundUp database AND no `SetupTransactionLog` row exists with `Operation="first-admin-create"` AND `Stage` in (`"keycloak-pending"`, `"db-pending"`, `"failed"`). WHEN such a non-completed transaction log row exists THEN `firstAdminCompleted` SHALL be false EVEN IF a SuperAdmin user is already present, AND the response SHALL include `firstAdminPending: true` AND `firstAdminPendingTransactionLogId: "{guid}"` so the operator knows to call `/setup/recover/{id}` before proceeding.
8. THE `isComplete` flag SHALL be true when `BootstrapState.IsComplete=true`.
9. THE `currentStep` SHALL be the first incomplete step in this order: `"app-identity"`, `"identity-provider"`, `"keycloak-bootstrap"`, `"first-admin"`, `"complete"`. If all step flags are true AND `isComplete=false`, `currentStep` SHALL be `"complete"`. If `isComplete=true`, `currentStep` SHALL be `"done"`.

### Requirement 16: Wizard Step Ordering Enforcement

**User Story:** As a framework developer, I want each wizard step to refuse to run if its predecessor steps haven't been completed, so that operators can't skip ahead and produce inconsistent state.

#### Acceptance Criteria

1. THE `POST /setup/identity-provider` endpoint SHALL return HTTP 412 with error "App identity step must be completed before identity provider configuration" WHEN `appIdentityCompleted` (per Requirement 15.4) is false.
2. THE `POST /setup/keycloak-bootstrap` endpoint SHALL return HTTP 412 with error naming the missing prerequisite step WHEN either `appIdentityCompleted` OR `identityProviderCompleted` is false.
3. THE `POST /setup/first-admin` endpoint SHALL return HTTP 412 with error naming the missing prerequisite step WHEN any of `appIdentityCompleted`, `identityProviderCompleted`, OR `keycloakBootstrapCompleted` is false.
4. THE `POST /setup/complete` endpoint SHALL return HTTP 412 with error naming the missing prerequisite step WHEN any of `appIdentityCompleted`, `identityProviderCompleted`, `keycloakBootstrapCompleted`, OR `firstAdminCompleted` is false.
5. THE step ordering check SHALL be implemented in a single shared precondition helper used by all wizard endpoints to ensure consistent behavior.

### Requirement 17: Setting Definitions for Wizard Keys

**User Story:** As a framework developer, I want the wizard's seven setting keys to have explicit, fully-specified `SettingDefinition` shapes, so that `EnsureDefinitionAsync` calls are deterministic and downstream phases (10B+) can rely on stable validation, level-allowance, and encryption-flag semantics.

#### Acceptance Criteria

1. WHEN `POST /setup/app-identity` runs THEN the service SHALL ensure the following two setting definitions exist (creating them if absent via `EnsureDefinitionAsync`, leaving them unchanged if present):

| Key | DataType | AllowedLevels | IsRequired | IsSecret | IsEncrypted | MaxLength | RegexPattern | DefaultValue |
|---|---|---|---|---|---|---|---|---|
| `app.identity.name` | String | System | true | false | false | 200 | (none) | null |
| `auth.application.default-domain` | String | System | false | false | false | 253 | `^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*$` | null |

2. WHEN `POST /setup/identity-provider` runs THEN the service SHALL ensure the following three setting definitions exist:

| Key | DataType | AllowedLevels | IsRequired | IsSecret | IsEncrypted | MaxLength | RegexPattern | DefaultValue |
|---|---|---|---|---|---|---|---|---|
| `auth.keycloak.public-base-url` | String | System | true | false | false | 2048 | (none — URL validated in service per cross-cutting convention 3) | null |
| `auth.keycloak.internal-base-url` | String | System | false | false | false | 2048 | (none — URL validated in service per cross-cutting convention 3) | null |
| `auth.keycloak.shared-realm-name` | String | System | true | false | false | 128 | (none) | `groundup` |

3. WHEN `POST /setup/keycloak-bootstrap` runs THEN the service SHALL ensure the following two setting definitions exist:

| Key | DataType | AllowedLevels | IsRequired | IsSecret | IsEncrypted | MaxLength | RegexPattern | DefaultValue |
|---|---|---|---|---|---|---|---|---|
| `auth.keycloak.admin-client-id` | String | System | true | false | false | 256 | (none) | `groundup-admin-client` |
| `auth.keycloak.admin-client-secret` | String | System | true | true | true | 4096 | (none) | null |

4. ALL wizard setting definitions SHALL belong to a setting group with key `groundup.setup` AND display name `"Setup"`. THE group SHALL be created on first `EnsureDefinitionAsync` call if absent.
5. ALL wizard setting definitions SHALL be marked `IsVisible=true` AND `IsReadOnly=false` so that operators can revise them via the standard settings admin UI after setup completes (e.g., to rotate a Keycloak admin client secret without re-running the bootstrap step).
6. THE phase 10AB SHALL NOT require these definitions to be seeded at application startup — they are created on demand by the wizard. THE phase MAY OPTIONALLY register a `SetupSettingDefinitionSeeder` (`IDataSeeder` implementation) that pre-creates them at startup; the requirement does not mandate seeding, only that `EnsureDefinitionAsync` calls inside the wizard work correctly whether the definitions pre-exist or not.
7. THE `auth.application.default-domain` setting MaxLength of 253 SHALL match the maximum length of a fully-qualified DNS name per RFC 1035.

### Requirement 18: Configuration Schema and Validation

**User Story:** As a framework operator, I want all required Layer-0 configuration values validated at startup, so that misconfiguration fails fast with clear error messages instead of mysteriously failing at first use.

#### Acceptance Criteria

1. THE framework SHALL define a `BootstrapOptions` class in `GroundUp.Core` bound from configuration section `GroundUp` containing: `DatabaseConnection` (string, required), `MasterKey` (string?, optional), `MasterKeyPath` (string?, optional), `BootstrapAdminToken` (string?, optional).
2. THE framework SHALL define a nested `KeycloakBootstrapOptions` class containing: `BootstrapAdminUsername` (string?, optional), `BootstrapAdminPassword` (string?, optional).
3. WHEN the application starts THEN the framework SHALL validate the bootstrap options via `IValidateOptions<BootstrapOptions>` registered with `ValidateOnStart()`.
4. THE validator SHALL fail startup if `DatabaseConnection` is null, empty, or whitespace.
5. THE validator SHALL fail startup if both `MasterKey` and `MasterKeyPath` are null/empty (one is required).
6. WHEN `BootstrapState.IsComplete=false` THE validator SHALL fail startup if `BootstrapAdminToken` is null/empty AND emit a clear error message.
7. WHEN `BootstrapAdminToken` is configured AND has fewer than 32 characters THEN the validator SHALL fail startup with a message recommending at least 32 characters.

### Requirement 19: Property-Based Test Coverage

**User Story:** As a framework developer, I want property-based tests covering encryption invariants and one-shot bootstrap token semantics, so that we have evidence the security-critical primitives behave correctly across a wide input space.

#### Acceptance Criteria

1. THE test suite SHALL include a property test verifying for any non-null UTF-8 string `v`, `Decrypt(Encrypt(v)) == v` (encryption round-trip integrity), with at least 100 iterations.
2. THE test suite SHALL include a property test verifying for any non-null UTF-8 string `v`, `Encrypt(v) != v` AND `Encrypt(v).StartsWith("aes-gcm-v1:")` (encryption actually happens and produces tagged output), with at least 100 iterations.
3. THE test suite SHALL include a property test verifying for any pair of distinct master keys `k1 != k2`, ciphertext encrypted under `k1` cannot be decrypted under `k2` (no silent fallback), with at least 100 iterations.
4. THE test suite SHALL include a property test verifying for any non-null UTF-8 string `v`, `Encrypt(v)` produces a different ciphertext on each call (fresh nonce per encryption), with at least 100 iterations.
5. THE test suite SHALL include a property test verifying for N concurrent attempts to call `CompleteSetupAsync` after setup completion, all attempts SHALL fail (one-shot guarantee), with at least 100 iterations and N between 2 and 10.

### Requirement 20: Rate Limiting on Setup Endpoints

**User Story:** As a security-conscious operator, I want all `/setup/*` endpoints to enforce a per-IP rate limit, so that a leaked or guessed bootstrap admin token cannot be used at high frequency to brute-force or abuse setup operations.

#### Acceptance Criteria

1. THE framework SHALL apply ASP.NET Core rate limiting to all paths matching `/setup/*` (case-insensitive, segment-aware per cross-cutting convention 6).
2. THE rate limit SHALL be a fixed-window limiter keyed on the client's remote IP address (resolved via the configured `ForwardedHeadersOptions` if a reverse proxy is in use, otherwise the direct connection remote IP).
3. THE default rate limit SHALL be 30 requests per 60-second window per IP. The values SHALL be configurable via `GroundUp:Setup:RateLimit:RequestsPerWindow` (default 30) AND `GroundUp:Setup:RateLimit:WindowSeconds` (default 60).
4. WHEN a request exceeds the rate limit THEN the framework SHALL respond with HTTP 429 (Too Many Requests) with body `{ "code": "rate_limited", "message": "Too many setup requests from this IP; try again later." }` AND set the `Retry-After` response header to the remaining window seconds.
5. THE rate limiter SHALL apply BEFORE bootstrap-token authentication so unauthenticated abusers cannot drain server resources by repeatedly invoking endpoints with random tokens.
6. THE rate limiter SHALL be disabled automatically once `BootstrapState.IsComplete=true` (since `/setup/*` requests are uniformly rejected post-setup, rate limiting them is wasted work). THE configuration values from criterion 3 MAY still be honored to limit token-rejection log noise; this is a design-time decision.
7. WHEN a request comes from a loopback address (`127.0.0.1`, `::1`) AND the application is running in `Development` environment THEN the rate limit SHALL be bypassed to support automated local testing.
8. WHEN a reverse proxy is in use (the consuming application has registered `UseForwardedHeaders` in its pipeline) THEN the consuming application SHALL place `UseForwardedHeaders` BEFORE `UseRateLimiter` so the rate limiter sees the original client IP rather than the proxy's IP. THE framework's `UseGroundUpMiddleware` extension SHALL document this ordering requirement in XML comments. WHEN `UseForwardedHeaders` is NOT registered THEN the rate limiter SHALL fall back to `HttpContext.Connection.RemoteIpAddress`.

### Requirement 21: Request Body Size Limits on Setup Endpoints

**User Story:** As a security-conscious operator, I want all `/setup/*` endpoints to reject oversized request bodies, so that an attacker holding the bootstrap admin token cannot exhaust memory or disk by submitting large JSON payloads.

#### Acceptance Criteria

1. THE framework SHALL configure a request body size limit of 64 KB (65,536 bytes) on all `/setup/*` endpoints. THE limit SHALL be configurable via `GroundUp:Setup:MaxRequestBodyBytes` (default 65536, minimum enforced 4096).
2. WHEN a request body exceeds the configured limit THEN the framework SHALL respond with HTTP 413 (Payload Too Large) with body `{ "code": "payload_too_large", "message": "Request body exceeds the {N}-byte limit." }` where `{N}` is the configured limit.
3. THE size limit SHALL be enforced before any model binding or business-logic validation runs.
4. THE limit SHALL apply to JSON setup endpoints only, not to static asset paths or healthcheck paths in the allowed-path list.

### Requirement 22: GET /setup Default Response

**User Story:** As an operator who navigates to `/setup` in a browser without a setup-mode UI shipped, I want a small JSON response indicating the setup state, so that I know the bootstrap is active and can call `GET /setup/status` for details.

#### Acceptance Criteria

1. THE framework SHALL expose `GET /setup` returning HTTP 200 with body `{ "code": "setup_active", "message": "Setup mode active. Authenticate with the bootstrap admin token and call GET /setup/status to inspect wizard state." }`.
2. THE endpoint SHALL NOT require bootstrap-token authentication. It is a public landing-page response intended to confirm the setup endpoint is reachable.
3. WHEN `BootstrapState.IsComplete=true` THEN this endpoint SHALL return HTTP 404. (After setup, `/setup` is no longer a valid route.)
4. THE response SHALL set `Cache-Control: no-store` to prevent intermediaries from caching setup-mode responses across a setup-completion event.

### Requirement 23: Boundary Constraints (Negative Requirements)

**User Story:** As a framework architect, I want explicit out-of-scope items documented, so that 10AB ships a focused MVP and known follow-on work is tracked elsewhere.

#### Acceptance Criteria

1. THE 10AB phase SHALL NOT ship any `ISecretResolver` implementation. Azure Key Vault, AWS Secrets Manager, and HSM implementations are deferred.
2. THE 10AB phase SHALL NOT ship master-key rotation logic. Key rotation is deferred to a follow-on story.
3. THE 10AB phase SHALL NOT ship the full HTML/SPA setup wizard UI. The wizard SHALL ship as JSON endpoints only; minimal HTML pages for direct browser interaction are acceptable but not required for sign-off.
4. THE 10AB phase SHALL NOT modify any existing setting definitions to be `IsSecret=true` or `IsEncrypted=true`. The first phase to introduce a secret-flagged setting in production seeders is 10B (Keycloak admin client secret).
5. THE 10AB phase SHALL NOT introduce any new authentication flow. Setup-mode authentication uses only the bootstrap admin token.
6. THE 10AB phase SHALL NOT support concurrent setup wizard sessions. The wizard endpoints SHALL serialize on `IBootstrapStateService` updates via the `BootstrapState` row's `xmin` optimistic concurrency token (per cross-cutting convention 7).
