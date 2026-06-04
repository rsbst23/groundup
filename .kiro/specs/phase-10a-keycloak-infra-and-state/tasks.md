# Implementation Plan: Phase 10A — Keycloak Infra, IdP Admin Contract, AuthFlowState

## Overview

This plan implements the foundational infrastructure for Keycloak integration and OAuth flow state management. It delivers containers, contracts, persistence, and service skeletons that Phases 10B–10E build upon. The implementation is broken into 10 task groups that each compile independently and can be reviewed incrementally (~10-15 files per natural PR boundary).

## Tasks

- [ ] 1. Docker infrastructure
  - [ ] 1.1 Update docker-compose.yml for Keycloak pinning and volumes
    - Pin Keycloak image to `quay.io/keycloak/keycloak:26.0.7` (remove `latest` tag)
    - Add `--import-realm` to Keycloak command
    - Add postgres-init.sql volume mount at `/docker-entrypoint-initdb.d/01-create-keycloak-db.sql`
    - Add realm.json volume mount at `/opt/keycloak/data/import/realm.json`
    - Add `kcdata` named volume for Keycloak data persistence
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 2.2, 2.3, 2.7_
  - [ ] 1.2 Create keycloak/postgres-init.sql
    - SQL script to create the `keycloak` database idempotently
    - Use `SELECT ... WHERE NOT EXISTS ... \gexec` pattern
    - _Requirements: 1.8_
  - [ ] 1.3 Create keycloak/realm.json
    - Define `groundup` realm with `registrationAllowed: false`, `loginWithEmailAllowed: true`
    - Define `groundup-app` public client with authorization-code + PKCE (S256)
    - Register `https://localhost:*/auth/callback` redirect URI
    - Register `https://localhost:*` web origins
    - Disable direct access grants
    - _Requirements: 2.1, 2.4, 2.5, 2.6_

- [ ] 2. Core types — enums, entity, DTOs, IdP admin DTOs, validator
  - [ ] 2.1 Create FlowType enum in GroundUp.Auth.Core/Enums/FlowType.cs
    - Values: NewOrganization=0, Invitation=1, JoinLink=2, EnterpriseFirstAdmin=3, EnterpriseSsoAutoJoin=4, MultiTenantSelection=5, TokenRefresh=6
    - _Requirements: 8.1, 8.2, 8.6_
  - [ ] 2.2 Create FlowStatus enum in GroundUp.Auth.Core/Enums/FlowStatus.cs
    - Values: Pending=0, Consumed=1, Expired=2, Failed=3
    - _Requirements: 8.3, 8.4, 8.5, 8.6_
  - [ ] 2.3 Create AuthFlowState entity in GroundUp.Auth.Core/Entities/AuthFlowState.cs
    - Sealed class inheriting BaseEntity, implementing IAuditable
    - NOT ITenantEntity, NOT ISoftDeletable
    - Properties: FlowType, Status (default Pending), TenantId?, InvitationId?, JoinLinkId?, Realm?, ReturnUrl?, Nonce, CreatedByIp?, CreatedByUserAgent?, ExpiresAt, ConsumedAt?, TerminatedAt?, FailureReason?
    - IAuditable: CreatedAt, CreatedBy?, UpdatedAt?, UpdatedBy?
    - _Requirements: 7.1–7.19_
  - [ ] 2.4 Create AuthFlowStateDto record in GroundUp.Auth.Core/Dtos/AuthFlowStateDto.cs
    - Mirror entity read-relevant properties
    - _Requirements: 11.1, 11.4_
  - [ ] 2.5 Create InitiateAuthFlowRequest record in GroundUp.Auth.Core/Dtos/InitiateAuthFlowRequest.cs
    - Properties: FlowType, TenantId?, InvitationId?, JoinLinkId?, Realm?, ReturnUrl?, Nonce, CreatedByIp?, CreatedByUserAgent?, Lifetime?
    - _Requirements: 11.2, 11.4_
  - [ ] 2.6 Create IdP admin DTOs in GroundUp.Auth.Core/Dtos/IdentityProvider/
    - RealmDto, CreateRealmRequest, UpdateRealmRequest
    - IdentityProviderClientDto, CreateIdentityProviderClientRequest, UpdateIdentityProviderClientRequest
    - ProvisionUserRequest, ProvisionedUserDto, IdentityProviderUserCredentialsDto
    - All records in GroundUp.Auth.Core.Dtos namespace
    - _Requirements: 6.1–6.11_
  - [ ] 2.7 Create InitiateAuthFlowRequestValidator in GroundUp.Auth.Core/Validators/InitiateAuthFlowRequestValidator.cs
    - Nonce: NotEmpty, MaximumLength(128)
    - FlowType: IsInEnum
    - Realm: MaximumLength(128) when not null
    - ReturnUrl: MaximumLength(2048) when not null
    - CreatedByIp: MaximumLength(64) when not null
    - CreatedByUserAgent: MaximumLength(512) when not null
    - Lifetime: positive if specified
    - _Requirements: 17.1_

- [ ] 3. Tenant.CustomDomain removal + ISettingsService extension
  - [ ] 3.1 Remove CustomDomain property from Tenant entity
    - Remove `public string? CustomDomain { get; set; }` from Tenant.cs
    - _Requirements: 21.1_
  - [ ] 3.2 Remove CustomDomain from TenantDto, CreateTenantDto, UpdateTenantDto
    - Remove property from each DTO record
    - _Requirements: 21.2_
  - [ ] 3.3 Remove CustomDomain mapping from TenantConfiguration
    - Remove any `.Property(e => e.CustomDomain)` fluent configuration
    - _Requirements: 21.3_
  - [ ] 3.4 Remove CustomDomain references from mappers, validators, and tests
    - Update TenantMapper, any validators referencing CustomDomain
    - Fix compilation errors in test projects
    - _Requirements: 21.4_
  - [ ] 3.5 Create EnsureSettingDefinitionRequest DTO in GroundUp.Core/Dtos/Settings/EnsureSettingDefinitionRequest.cs
    - Record with: Key, DataType, DefaultValue, DisplayName, Description?, Category?, GroupKey, GroupDisplayName, AllowedLevelNames, RegexPattern?, ValidationMessage?, IsRequired, IsSecret, IsEncrypted
    - _Requirements: 22.1_
  - [ ] 3.6 Add EnsureDefinitionAsync method to ISettingsService interface
    - `Task<OperationResult<SettingDefinitionDto>> EnsureDefinitionAsync(EnsureSettingDefinitionRequest request, CancellationToken cancellationToken = default)`
    - Add to existing ISettingsService.cs
    - _Requirements: 22.2_

- [ ] 4. Checkpoint — Ensure all tests pass
  - Ensure the solution compiles cleanly with enums, entity, DTOs, validator, and CustomDomain removal. Existing tests must still pass. Ask the user if questions arise.

- [ ] 5. Repository layer
  - [ ] 5.1 Create IAuthFlowStateRepository in GroundUp.Auth.Data.Abstractions/IAuthFlowStateRepository.cs
    - Inherit IBaseRepository<AuthFlowStateDto>
    - Add: MarkConsumedAsync(Guid id, CancellationToken), MarkFailedAsync(Guid id, string reason, CancellationToken)
    - Add: MarkExpiredOlderThanAsync(DateTime cutoff, CancellationToken), DeleteTerminalOlderThanAsync(DateTime cutoff, CancellationToken)
    - _Requirements: 12.1, 12.4, 13.1, 14.1, 14.2, 15.1, 15.3_
  - [ ] 5.2 Create AuthFlowStateMapper in GroundUp.Auth.Repositories/Mappers/AuthFlowStateMapper.cs
    - Mapperly [Mapper] static partial class
    - ToDto(AuthFlowState) and ToEntity(AuthFlowStateDto) methods
    - _Requirements: 11.3_
  - [ ] 5.3 Create AuthFlowStateRepository in GroundUp.Auth.Repositories/AuthFlowStateRepository.cs
    - Inherit BaseRepository<AuthFlowState, AuthFlowStateDto>
    - Constructor takes AuthDbContext, passes mapper delegates
    - MarkConsumedAsync: conditional UPDATE (WHERE Id=@id AND Status=Pending AND ExpiresAt>@now), set Status=Consumed, ConsumedAt=UtcNow, TerminatedAt=UtcNow
    - MarkFailedAsync: load entity, verify Pending, set Status=Failed, FailureReason (truncate 1024), TerminatedAt=UtcNow
    - MarkExpiredOlderThanAsync: ExecuteUpdateAsync bulk transition
    - DeleteTerminalOlderThanAsync: ExecuteDeleteAsync bulk delete
    - _Requirements: 12.2, 12.3, 12.5, 13.1–13.7, 14.1–14.5, 15.1–15.5_
  - [ ] 5.4 Create AuthFlowStateConfiguration in GroundUp.Auth.Data.Postgres/Configurations/AuthFlowStateConfiguration.cs
    - Table "AuthFlowStates", PK on Id
    - Composite index (Status, ExpiresAt), index on TenantId
    - FlowType/Status as int conversion
    - String constraints: Nonce(128 required), Realm(128), ReturnUrl(2048), CreatedByIp(64), CreatedByUserAgent(512), FailureReason(1024)
    - Nullable timestamps: ConsumedAt, TerminatedAt
    - _Requirements: 9.1–9.14_
  - [ ] 5.5 Add AuthFlowStates DbSet to AuthDbContext
    - `public DbSet<AuthFlowState> AuthFlowStates => Set<AuthFlowState>();`
    - _Requirements: 7.19_

- [ ] 6. Migration
  - [ ] 6.1 Create EF Core migration AddAuthFlowStatesAndDropTenantCustomDomain
    - Up: CREATE TABLE AuthFlowStates with all columns and indexes; ALTER TABLE Tenants DROP COLUMN CustomDomain
    - Down: ALTER TABLE Tenants ADD COLUMN CustomDomain varchar(256) NULL; DROP TABLE AuthFlowStates
    - Place in GroundUp.Auth.Data.Postgres/Migrations
    - _Requirements: 10.1–10.6_

- [ ] 7. Service layer
  - [ ] 7.1 Create IAuthFlowStateService interface in GroundUp.Auth.Services/IAuthFlowStateService.cs
    - InitiateAsync(InitiateAuthFlowRequest, CancellationToken) → Task<OperationResult<AuthFlowStateDto>>
    - ConsumeAsync(Guid id, FlowType expectedFlowType, CancellationToken) → Task<OperationResult<AuthFlowStateDto>>
    - MarkFailedAsync(Guid id, string reason, CancellationToken) → Task<OperationResult<AuthFlowStateDto>>
    - _Requirements: 16.1–16.7_
  - [ ] 7.2 Create AuthFlowStateService in GroundUp.Auth.Services/AuthFlowStateService.cs
    - InitiateAsync: validate request, build DTO with Status=Pending, ExpiresAt=UtcNow+(Lifetime ?? 15 min), call repo.AddAsync
    - ConsumeAsync: call repo.MarkConsumedAsync, verify FlowType match on success, surface failures unchanged
    - MarkFailedAsync: reject empty reason (ValidationFailure), delegate to repo.MarkFailedAsync
    - _Requirements: 16.1–16.7, 13.2, 13.6_
  - [ ] 7.3 Create IAuthCookieWriter interface in GroundUp.Auth.Services/IAuthCookieWriter.cs
    - WriteAuthCookie(HttpContext, string token)
    - ClearAuthCookie(HttpContext)
    - XML doc specifying Domain derivation from auth.application.default-domain setting
    - _Requirements: 18.1–18.5_
  - [ ] 7.4 Replace IIdentityProviderAdminService stub with full contract
    - Replace the Phase 9D minimal stub with the full interface from design section 7
    - Realm CRUD: CreateRealmAsync, GetRealmAsync, UpdateRealmAsync, DeleteRealmAsync
    - Client CRUD: CreateClientAsync, GetClientAsync, UpdateClientAsync, DeleteClientAsync
    - User provisioning: ProvisionUserAsync, SetUserCredentialsAsync, DeleteUserAsync
    - _Requirements: 3.1–3.7, 4.1–4.6, 5.1–5.6_
  - [ ] 7.5 Extend AuthOptions with CleanupIntervalMinutes and RetentionDays
    - CleanupIntervalMinutes: int, default 5, must be > 0
    - RetentionDays: int, default 7, must be >= 0
    - Add validation rules to AddOptionsValidation
    - _Requirements: 23.1–23.4_
  - [ ] 7.6 Create AuthFlowStateCleanupSweeper in GroundUp.Auth.Services/AuthFlowStateCleanupSweeper.cs
    - BackgroundService with PeriodicTimer (interval from AuthOptions.CleanupIntervalMinutes)
    - Each tick: MarkExpiredOlderThanAsync(UtcNow), then DeleteTerminalOlderThanAsync(UtcNow - RetentionDays)
    - Structured logging for expired/deleted counts
    - Catch exceptions per cycle, log and continue
    - _Requirements: 15.1–15.5, 23.1–23.4, 24.1_
  - [ ] 7.7 Update AuthServiceCollectionExtensions DI registrations
    - Register IAuthFlowStateRepository → AuthFlowStateRepository (scoped)
    - Register IAuthFlowStateService → AuthFlowStateService (scoped)
    - Register IValidator<InitiateAuthFlowRequest> → InitiateAuthFlowRequestValidator (scoped)
    - Register AuthFlowStateCleanupSweeper as hosted service
    - No registration for IIdentityProviderAdminService or IAuthCookieWriter
    - _Requirements: 23.5_

- [ ] 8. Settings seeder
  - [ ] 8.1 Create DefaultAuthSettingsSeeder in GroundUp.Auth.Data.Postgres/Seeders/DefaultAuthSettingsSeeder.cs
    - Implement IDataSeeder with Order = 30
    - Seed `auth.application.default-domain` (string, default "", system level, group "auth.application")
    - Seed `auth.keycloak.shared-realm-name` (string, default "groundup", system level, group "auth.keycloak")
    - Seed `auth.keycloak.public-base-url` (string, default "http://localhost:8080", system level, group "auth.keycloak")
    - All calls via ISettingsService.EnsureDefinitionAsync (idempotent)
    - _Requirements: 22.1–22.5_

- [ ] 9. Checkpoint — Ensure all tests pass
  - Ensure the solution compiles cleanly, all existing tests pass, and the new service/repository/seeder code is wired up. Ask the user if questions arise.

- [ ] 10. Unit tests
  - [ ] 10.1 Write unit tests for AuthFlowStateService.InitiateAsync
    - Default lifetime (15 min) when Lifetime is null
    - Custom lifetime applied correctly
    - Validation failure when Nonce is empty
    - Validation failure when Nonce exceeds 128 chars
    - _Requirements: 7.15, 16.1, 16.6_
  - [ ] 10.2 Write unit tests for AuthFlowStateService.ConsumeAsync
    - Happy path: returns consumed DTO with correct status
    - Wrong FlowType mismatch returns failure
    - Surfaces NotFound, Conflict, expired from repo unchanged
    - _Requirements: 16.2, 16.3, 16.7_
  - [ ] 10.3 Write unit tests for AuthFlowStateService.MarkFailedAsync
    - Empty/whitespace reason returns ValidationFailure
    - Delegates to repo and surfaces result
    - _Requirements: 16.4, 16.5_
  - [ ] 10.4 Write unit tests for InitiateAuthFlowRequestValidator
    - All rules: Nonce required, Nonce max length, FlowType in enum, Realm max length, ReturnUrl max length, CreatedByIp max length, CreatedByUserAgent max length, Lifetime positive
    - _Requirements: 17.1_
  - [ ] 10.5 Write unit tests for AuthOptions validation
    - CleanupIntervalMinutes <= 0 fails validation
    - RetentionDays < 0 fails validation
    - Valid values pass
    - _Requirements: 23.2, 23.4_

- [ ] 11. Integration tests
  - [ ]* 11.1 Write integration tests for AuthFlowStateRepository CRUD
    - Add, get by ID, update, delete via Testcontainers Postgres
    - _Requirements: 12.1–12.5_
  - [ ]* 11.2 Write integration tests for AuthFlowStateRepository.MarkConsumedAsync
    - Happy path (Pending + not expired → Consumed)
    - Already-consumed rejection (Conflict)
    - Expired rejection (ExpiresAt <= UtcNow)
    - Not-found (invalid id)
    - _Requirements: 13.1–13.7_
  - [ ]* 11.3 Write integration tests for AuthFlowStateRepository.MarkFailedAsync
    - Happy path (Pending → Failed with reason)
    - Already-terminal rejection (Conflict)
    - _Requirements: 14.1–14.5_
  - [ ]* 11.4 Write integration tests for AuthFlowStateRepository bulk operations
    - MarkExpiredOlderThanAsync: only Pending rows past cutoff transition, others untouched
    - DeleteTerminalOlderThanAsync: only terminal rows past cutoff deleted, Pending untouched
    - _Requirements: 15.1–15.5_
  - [ ]* 11.5 Write integration tests for AuthFlowStateCleanupSweeper
    - Configure sub-second interval, verify expired rows transition
    - RetentionDays=0, verify terminal rows deleted immediately
    - _Requirements: 24.1_
  - [ ]* 11.6 Write integration tests for DefaultAuthSettingsSeeder
    - First run creates all 3 setting definitions
    - Second run is idempotent (no errors, no duplicates)
    - _Requirements: 22.3, 22.4_

- [ ] 12. Property-based tests
  - [ ]* 12.1 Write FsCheck property test for one-shot consumption idempotency
    - **Property 1: One-shot consumption idempotency**
    - Generate random valid AuthFlowState, consume twice sequentially, assert exactly one success
    - Minimum 100 iterations
    - **Validates: Requirements 13.2, 13.3, 13.5**
  - [ ]* 12.2 Write FsCheck property test for initiate-consume round-trip
    - **Property 2: Initiate-consume round-trip preserves metadata**
    - Generate random InitiateAuthFlowRequest, initiate → consume, verify all metadata fields preserved
    - Minimum 100 iterations
    - **Validates: Requirements 16.6, 16.7, 13.2**
  - [ ]* 12.3 Write FsCheck property test for concurrent consumption
    - **Property 3: Concurrent consumption selects exactly one winner**
    - Generate random valid row, launch N (2–10) concurrent ConsumeAsync calls, verify exactly one winner
    - Minimum 100 iterations (requires Testcontainers Postgres)
    - **Validates: Requirements 13.3, 13.5**
  - [ ]* 12.4 Write FsCheck property test for sweeper expiration
    - **Property 4: Sweeper expiration transition correctness**
    - Generate random mix of rows (expired/non-expired), run sweep, verify only expired Pending rows transition
    - Minimum 100 iterations (requires Testcontainers Postgres)
    - **Validates: Requirements 15.1, 15.2, 23.3**
  - [ ]* 12.5 Write FsCheck property test for sweeper retention pruning
    - **Property 5: Sweeper retention pruning correctness**
    - Generate random mix of terminal rows (old/recent), run sweep, verify only old ones deleted
    - Minimum 100 iterations (requires Testcontainers Postgres)
    - **Validates: Requirements 15.3, 15.4, 24.1**
  - [ ]* 12.6 Write FsCheck property test for expired row consumption rejection
    - **Property 6: Expired rows cannot be consumed**
    - Generate random rows with ExpiresAt in the past, attempt consume, verify all fail
    - Minimum 100 iterations
    - **Validates: Requirements 13.6, 7.14**

- [ ] 13. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task group compiles independently and can be reviewed as a separate PR
- Natural PR boundaries: Tasks 1–4 (infra + types), Tasks 5–6 (data layer), Tasks 7–8 (services), Tasks 10–12 (tests)
- No controllers, flow handlers, or Keycloak client implementations are included (deferred to 10B–10E)
- Property tests 1, 2, 6 can use NSubstitute mocks (testing service logic); properties 3, 4, 5 require Testcontainers Postgres
- The IIdentityProviderAdminService stub from Phase 9D is replaced with the full contract — no breaking changes since no implementation exists yet
- Existing tests must continue to pass after CustomDomain removal (task 3.4 handles fixing compilation)
- The EnsureDefinitionAsync method on ISettingsService requires an implementation update in SettingsService — this is expected to be handled alongside task 3.6 or deferred to Phase 10AB if the implementation is non-trivial

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "2.1", "2.2"] },
    { "id": 1, "tasks": ["2.3", "2.4", "2.5", "2.6", "2.7"] },
    { "id": 2, "tasks": ["3.1", "3.2", "3.3", "3.5", "3.6"] },
    { "id": 3, "tasks": ["3.4"] },
    { "id": 4, "tasks": ["5.1", "5.2", "5.4", "5.5"] },
    { "id": 5, "tasks": ["5.3"] },
    { "id": 6, "tasks": ["6.1"] },
    { "id": 7, "tasks": ["7.1", "7.3", "7.4", "7.5"] },
    { "id": 8, "tasks": ["7.2", "7.6", "7.7"] },
    { "id": 9, "tasks": ["8.1"] },
    { "id": 10, "tasks": ["10.1", "10.2", "10.3", "10.4", "10.5"] },
    { "id": 11, "tasks": ["11.1", "11.2", "11.3", "11.4", "11.5", "11.6"] },
    { "id": 12, "tasks": ["12.1", "12.2", "12.3", "12.4", "12.5", "12.6"] }
  ]
}
```
