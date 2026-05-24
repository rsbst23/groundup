# Requirements Document — Phase 10A: Keycloak Infra, IdP Admin Contract, AuthFlowState

## Introduction

Phase 10A is the foundational layer for Phase 10 (Keycloak Integration & Auth Flows) of the GroundUp framework. It contains **no flow logic** — its sole purpose is to land the infrastructure, contracts, persistence, and service skeletons that Phases 10B–10E build on top of:

1. A reproducible Keycloak container in `docker-compose.yml` (pinned version, dedicated database, automatic realm import on first boot).
2. The `IIdentityProviderAdminService` contract (interface + DTOs only) for realm, client, and user provisioning operations.
3. The `AuthFlowState` entity + supporting enums, EF configuration, migration, repository, and service skeleton — the stateful store that backs the OAuth `state` parameter and provides one-shot replay protection.
4. The `IAuthCookieWriter` contract (interface only). The cookie writer derives the cookie's `Domain` attribute from `auth.application.default-domain` (no separate config knob): when set, the writer scopes cookies to that domain (parent-domain cookie shared across subdomains); when empty, the writer issues host-only cookies (appropriate for single-host single-tenant deployments).
5. A system-only setting `auth.application.default-domain` seeded on startup, used by the host tenant resolver in 10C.
6. An `IHostedService`-based `AuthFlowStateCleanupSweeper` that expires `Pending` rows past their `ExpiresAt` and prunes terminal rows past the retention window. Migration to BackgroundJobs is tracked separately in GU-64.

Phase 10A ships interface stubs, persistence, and a sweeper. There are **no controllers**, **no flow handlers**, **no Keycloak client implementation**, and **no public CRUD endpoints** for `AuthFlowState`. Those land in 10B (Keycloak provider), 10C (dispatcher + flow handlers), 10D (invitations + join links), and 10E (enterprise flows).

Two audiences are addressed in the user stories below:

- **GroundUp framework developer** — the engineer building the framework itself, who needs internal contracts, persistence, and skeletons in place.
- **Consuming app developer** — the engineer integrating GroundUp into their application, who needs deterministic Keycloak boot, configurable cookie behavior for cross-subdomain deployments, and a default-domain setting to drive the host tenant resolver.

## Realm Strategy

The Keycloak realm topology assumed by Phase 10A (and locked in for all subsequent Phase 10 sub-phases) is:

- **Standard tenants share a single Keycloak realm** whose name is captured by the system-level setting `auth.keycloak.shared-realm-name` (default value `"groundup"`, configurable at deployment time). This is the realm seeded via `keycloak/realm.json` on first boot. Each user has exactly one Keycloak account in this realm; GroundUp's `UserTenant` table establishes which standard tenants the user belongs to. Standard tenants are routed to via subdomains of the configured app domain (e.g., `acme.sampleapp.com` → tenant with `Slug=acme`).
- **Enterprise tenants get their own dedicated Keycloak realm**, named per `Tenant.RealmName`. Within an enterprise realm, every Keycloak user belongs to exactly one tenant by construction. Enterprise tenants are also routed to via subdomains of the configured app domain (e.g., `bigco.sampleapp.com` → tenant with `Slug=bigco`, `RealmName=bigco-corp`). **Enterprise customers who want to run on their own registrable domain (e.g., `app.bigco.com`) self-deploy a single-tenant GroundUp instance — the framework does NOT support serving a dedicated registrable domain from a shared instance** because that would require cross-registrable-domain cookie handoff which we have explicitly chosen not to build.
- **Federated identity providers** (corporate Okta, Azure AD, Google Workspace, etc.) are configured by enterprise admins inside their realm via Keycloak's admin UI. GroundUp does NOT build a parallel UI for federated IdP configuration. The consuming-app UI MAY deep-link to the Keycloak admin console for the relevant realm as a convenience.

The Keycloak Postgres database is a separate logical database from the application database. In local development the docker-compose setup hosts both databases on the same Postgres container; in production, deployments are free to host the Keycloak database on the same Postgres server as the application database, on a separate Postgres server entirely, or to use a managed Keycloak service that handles its own persistence. The framework does not enforce a topology — the choice is a deployment concern owned by the consuming application. Phase 10A delivers the local-dev convention (one Postgres container, two databases, created via a `postgres-init.sql` script mounted at `/docker-entrypoint-initdb.d/`).

Phase 10A delivers only the contract that supports this topology — `IIdentityProviderAdminService` accepts a `realmName` parameter on every realm-scoped operation, so callers in 10B–10E can pass the resolved value of `auth.keycloak.shared-realm-name` for standard-tenant flows and `Tenant.RealmName` for enterprise-tenant flows without any branching at the interface level.

## Forward Reference: Operational Settings and Secret Management

Phase 10A introduces three auth-module settings (Requirement 22). All three are non-sensitive and stored as plain strings. The broader settings-as-operational-source-of-truth model — including encrypted-at-rest secrets (`IsSecret`), the master-key abstraction, the optional secret-resolver interface, the bootstrap-state entity, and the first-run setup wizard — is owned by **Phase 10AB (GU-70)**, which sits between 10A and 10B in the dependency order. Phase 10B is the first sub-phase to introduce a sensitive setting (the Keycloak admin client secret) and depends on 10AB for the encryption substrate. Phase 10A itself does NOT add any sensitive settings or secret-handling code.

## Glossary

- **AuthFlowState**: A row in the `AuthFlowStates` table representing one in-flight OAuth flow. Its primary key (UUID v7) is the value embedded in the OAuth `state` parameter sent to Keycloak.
- **AuthFlowStateService**: The `IAuthFlowStateService` implementation responsible for initiating, consuming, and failing `AuthFlowState` rows. Phase 10A ships only the skeleton — the dispatcher logic in 10C composes flow-specific behavior on top.
- **AuthFlowStateRepository**: The `IAuthFlowStateRepository` implementation providing standard CRUD plus `MarkConsumedAsync`, `MarkFailedAsync`, and sweeper-friendly bulk operations.
- **AuthFlowStateCleanupSweeper**: The `IHostedService` background worker that runs every `AuthOptions.CleanupIntervalMinutes` to mark expired `Pending` rows as `Expired` and delete terminal rows older than `AuthOptions.RetentionDays`.
- **FlowType**: An enum identifying which auth flow created the `AuthFlowState` row. Values for 10A: `NewOrganization`, `Invitation`, `JoinLink`, `EnterpriseFirstAdmin`, `EnterpriseSsoAutoJoin`, `MultiTenantSelection`, `TokenRefresh`.
- **FlowStatus**: An enum representing the lifecycle state of an `AuthFlowState` row. Values: `Pending`, `Consumed`, `Expired`, `Failed`.
- **IdentityProviderAdminService**: The `IIdentityProviderAdminService` interface — administrative contract for realm CRUD, client CRUD inside a realm, and user provisioning. Phase 10B implements against Keycloak; 10A defines the contract only.
- **AuthCookieWriter**: The `IAuthCookieWriter` interface — abstraction over writing and clearing the auth cookie that honours `AuthOptions.CookieName`, `CookieSecure`, and `CookieSameSite`, and derives the cookie's `Domain` attribute from the `auth.application.default-domain` system setting. Phase 10C implements; 10A defines the contract only.
- **AuthDbContext**: The existing EF Core `DbContext` (in `GroundUp.Auth.Data.Postgres`) that owns the auth module's tables. `AuthFlowStates` lives here alongside `Users`, `Tenants`, `Roles`, etc.
- **AuthOptions**: The configuration class in `GroundUp.Auth.Services` bound from the `GroundUp:Auth` configuration section. Extended in 10A with `CleanupIntervalMinutes` and `RetentionDays`.
- **DefaultAuthSettingsSeeder**: A new `IDataSeeder` implementation in `GroundUp.Auth.Data.Postgres/Seeders/` that seeds auth-module system-level settings, starting with `auth.application.default-domain`.
- **HostTenantResolver**: The `IHostTenantResolver` introduced in Phase 10C. 10A only seeds the `auth.application.default-domain` setting that 10C will consume.
- **Realm**: A Keycloak realm — the isolation boundary inside Keycloak that owns its own users, clients, and identity providers. The default realm imported on first boot is named `groundup`.
- **One-shot replay protection**: The guarantee that any single `AuthFlowState.Id` can be consumed at most once. Enforced atomically in `AuthFlowStateRepository.MarkConsumedAsync`.

## Requirements

### Requirement 1: Keycloak Container Pinning and Hygiene (Consuming App Developer)

**User Story:** As a consuming app developer, I want `docker compose up` to start a deterministic Keycloak instance pinned to a known stable version, so that local development environments behave identically across machines and across time.

#### Acceptance Criteria

1. THE Docker_Compose SHALL pin the Keycloak service image to a 26.x stable release (e.g., `quay.io/keycloak/keycloak:26.0.7`). The `latest` tag SHALL NOT be used.
2. THE Docker_Compose SHALL configure Keycloak to use a dedicated Postgres database named `keycloak`, separate from the application database `groundup`.
3. THE Docker_Compose SHALL ensure the `keycloak` database is created before Keycloak starts (e.g., via a `postgres` init script or a healthcheck-gated dependency that creates the database on demand).
4. THE Docker_Compose SHALL expose Keycloak on port 8080 of the host machine for local browser access.
5. THE Docker_Compose SHALL declare a `depends_on` relationship from Keycloak to the Postgres service with a `service_healthy` condition, so Keycloak SHALL NOT start before Postgres is accepting connections.
6. THE Docker_Compose SHALL configure Keycloak with the `start-dev` command for local development (TLS not required, ephemeral admin credentials acceptable).
7. THE Docker_Compose SHALL persist Keycloak's data via a named volume so that realms, clients, and users survive container restarts.
8. THE Repository SHALL contain a file at `keycloak/postgres-init.sql` (or equivalent path) containing the SQL to create the `keycloak` database (e.g., `CREATE DATABASE keycloak;`). The docker-compose Postgres service SHALL mount this file at `/docker-entrypoint-initdb.d/` so the database is created on first container start.

### Requirement 2: Keycloak Realm Import on First Boot (Consuming App Developer)

**User Story:** As a consuming app developer, I want a default `groundup` realm with a `groundup-app` client and local-development redirect URIs to be imported automatically on first Keycloak boot, so that I can begin developing auth flows without clicking through the Keycloak admin UI.

#### Acceptance Criteria

1. THE Repository SHALL contain a `keycloak/realm.json` file at the repository root describing the default `groundup` realm.
2. THE Docker_Compose SHALL mount the `keycloak/realm.json` file into the Keycloak container at the path expected by Keycloak's import-on-start mechanism (e.g., `/opt/keycloak/data/import/realm.json`).
3. THE Docker_Compose SHALL invoke Keycloak with the `--import-realm` flag (or equivalent) so that the realm file is imported on first boot.
4. THE Realm_Import_File SHALL define a realm named `groundup`.
5. THE Realm_Import_File SHALL define a client named `groundup-app` configured for the OAuth2 authorization-code flow with PKCE.
6. THE Realm_Import_File SHALL register `https://localhost:*/auth/callback` as a permitted redirect URI for the `groundup-app` client to support local-development ports.
7. WHERE the Keycloak container has already imported the realm on a previous boot, THE Docker_Compose SHALL NOT cause the import to fail or overwrite local edits made via the admin UI (idempotent boot).

### Requirement 3: IIdentityProviderAdminService — Realm Operations (Framework Developer)

**User Story:** As a GroundUp framework developer, I want the `IIdentityProviderAdminService` interface to define realm CRUD operations, so that Phase 10B can implement realm provisioning against Keycloak and Phase 10E can call it from the enterprise signup flow.

#### Acceptance Criteria

1. THE IIdentityProviderAdminService SHALL define `CreateRealmAsync(CreateRealmRequest request, CancellationToken cancellationToken)` returning `Task<OperationResult<RealmDto>>`.
2. THE IIdentityProviderAdminService SHALL define `GetRealmAsync(string realmName, CancellationToken cancellationToken)` returning `Task<OperationResult<RealmDto>>`.
3. THE IIdentityProviderAdminService SHALL define `UpdateRealmAsync(string realmName, UpdateRealmRequest request, CancellationToken cancellationToken)` returning `Task<OperationResult<RealmDto>>`.
4. THE IIdentityProviderAdminService SHALL define `DeleteRealmAsync(string realmName, CancellationToken cancellationToken)` returning `Task<OperationResult>`.
5. THE IIdentityProviderAdminService SHALL live in the `GroundUp.Auth.Services` namespace, in a new file `IIdentityProviderAdminService.cs`.
6. THE IIdentityProviderAdminService SHALL be a sibling to the existing `IIdentityProviderService` and follow the same documentation, naming, and signature style.
7. Phase 10A SHALL NOT ship an implementation of `IIdentityProviderAdminService` — only the interface and DTOs.

### Requirement 4: IIdentityProviderAdminService — Client Operations (Framework Developer)

**User Story:** As a GroundUp framework developer, I want the `IIdentityProviderAdminService` interface to define client CRUD operations scoped to a realm, so that Phase 10B can manage OAuth clients inside Keycloak realms (e.g., per-tenant clients in 10E).

#### Acceptance Criteria

1. THE IIdentityProviderAdminService SHALL define `CreateClientAsync(string realmName, CreateIdentityProviderClientRequest request, CancellationToken cancellationToken)` returning `Task<OperationResult<IdentityProviderClientDto>>`.
2. THE IIdentityProviderAdminService SHALL define `GetClientAsync(string realmName, string clientId, CancellationToken cancellationToken)` returning `Task<OperationResult<IdentityProviderClientDto>>`.
3. THE IIdentityProviderAdminService SHALL define `UpdateClientAsync(string realmName, string clientId, UpdateIdentityProviderClientRequest request, CancellationToken cancellationToken)` returning `Task<OperationResult<IdentityProviderClientDto>>`.
4. THE IIdentityProviderAdminService SHALL define `DeleteClientAsync(string realmName, string clientId, CancellationToken cancellationToken)` returning `Task<OperationResult>`.
5. THE Client_Operations SHALL accept a redirect-URI list in the request DTOs to support per-tenant redirect configuration.
6. THE Client_Operations SHALL accept a flag indicating whether the client uses PKCE.

### Requirement 5: IIdentityProviderAdminService — User Provisioning (Framework Developer)

**User Story:** As a GroundUp framework developer, I want the `IIdentityProviderAdminService` interface to define user-provisioning operations, so that Phase 10B can create Keycloak users for invitations, first-admin signup, and other flows that need to seed an identity provider account before the user logs in.

#### Acceptance Criteria

1. THE IIdentityProviderAdminService SHALL define `ProvisionUserAsync(string realmName, ProvisionUserRequest request, CancellationToken cancellationToken)` returning `Task<OperationResult<ProvisionedUserDto>>`.
2. THE ProvisionUserRequest SHALL carry the email address, optional display name, optional initial password, and a `RequirePasswordReset` flag indicating whether the user must reset their password on first login.
3. THE IIdentityProviderAdminService SHALL define `SetUserCredentialsAsync(string realmName, string externalUserId, IdentityProviderUserCredentialsDto credentials, CancellationToken cancellationToken)` returning `Task<OperationResult>`.
4. THE IIdentityProviderAdminService SHALL define `DeleteUserAsync(string realmName, string externalUserId, CancellationToken cancellationToken)` returning `Task<OperationResult>`.
5. THE ProvisionedUserDto SHALL contain at minimum: `string ExternalUserId`, `string Email`, `string? DisplayName`, `bool RequiresPasswordReset`.
6. WHERE a provisioning request specifies an email that already exists in the realm, THE ProvisionUserAsync SHALL be expected (per its contract) to return a conflict-style `OperationResult` failure rather than throwing.

### Requirement 6: Identity Provider Admin DTOs (Framework Developer)

**User Story:** As a GroundUp framework developer, I want strongly-typed DTOs for the identity-provider admin contract, so that Phase 10B implements against stable shapes and the public contract is locked before any implementation is written.

#### Acceptance Criteria

1. THE RealmDto SHALL be a record with at minimum: `string RealmName`, `string DisplayName`, `bool Enabled`.
2. THE CreateRealmRequest SHALL be a record with at minimum: `string RealmName`, `string? DisplayName`.
3. THE UpdateRealmRequest SHALL be a record carrying mutable realm settings (`string? DisplayName`, `bool? Enabled`).
4. THE IdentityProviderClientDto SHALL be a record with at minimum: `string ClientId`, `string? ClientSecret`, `IReadOnlyList<string> RedirectUris`, `bool RequiresPkce`.
5. THE CreateIdentityProviderClientRequest SHALL be a record with at minimum: `string ClientId`, `IReadOnlyList<string> RedirectUris`, `bool RequiresPkce`.
6. THE UpdateIdentityProviderClientRequest SHALL be a record carrying mutable client settings (`IReadOnlyList<string>? RedirectUris`, `bool? RequiresPkce`).
7. THE ProvisionUserRequest SHALL be a record with: `string Email`, `string? DisplayName`, `string? InitialPassword`, `bool RequirePasswordReset`.
8. THE ProvisionedUserDto SHALL be a record with: `string ExternalUserId`, `string Email`, `string? DisplayName`, `bool RequiresPasswordReset`.
9. THE IdentityProviderUserCredentialsDto SHALL be a record with: `string Password`, `bool Temporary`.
10. ALL Identity_Provider_Admin_DTOs SHALL live in the `GroundUp.Auth.Core/Dtos` folder under the `GroundUp.Auth.Core.Dtos` namespace.
11. ALL Identity_Provider_Admin_DTOs SHALL use the `record` keyword.

### Requirement 7: AuthFlowState Entity (Framework Developer)

**User Story:** As a GroundUp framework developer, I want a single `AuthFlowState` entity that captures every property an in-flight OAuth flow may carry, so that Phase 10C can use the entity's primary key as the OAuth `state` value and reconstruct the full flow context from the database on callback.

#### Acceptance Criteria

1. THE AuthFlowState SHALL be a sealed class in `GroundUp.Auth.Core/Entities/AuthFlowState.cs` under the `GroundUp.Auth.Core.Entities` namespace.
2. THE AuthFlowState SHALL inherit from `BaseEntity` and implement `IAuditable`.
3. THE AuthFlowState SHALL NOT implement `ITenantEntity`. (Some flows — notably `NewOrganization` — exist before any tenant context is established, and the row must be writable without a tenant.)
4. THE AuthFlowState SHALL NOT implement `ISoftDeletable`. (Cleanup is performed via hard delete by the sweeper to keep the table bounded.)
5. THE AuthFlowState SHALL expose a `FlowType` property of type `FlowType` (enum, see Requirement 8).
6. THE AuthFlowState SHALL expose a `Status` property of type `FlowStatus` (enum, see Requirement 8). The default value SHALL be `FlowStatus.Pending`.
7. THE AuthFlowState SHALL expose a nullable `TenantId` property of type `Guid?` for flows that target a specific tenant. This is NOT a foreign key — some flows (e.g., NewOrganization) create the tenant as part of the flow, so the tenant may not exist when the row is created. No FK constraint is created.
8. THE AuthFlowState SHALL expose a nullable `InvitationId` property of type `Guid?` reserved for the Invitation flow in 10D. No FK constraint is created in 10A (the referenced table does not exist yet); a FK will be added in 10D when `TenantInvitations` is created.
9. THE AuthFlowState SHALL expose a nullable `JoinLinkId` property of type `Guid?` reserved for the Join Link flow in 10D. No FK constraint is created in 10A; a FK will be added in 10D when `TenantJoinLinks` is created.
10. THE AuthFlowState SHALL expose a nullable `Realm` property of type `string?` for the Keycloak realm targeted by the flow.
11. THE AuthFlowState SHALL expose a nullable `ReturnUrl` property of type `string?` for the URL the user should be redirected to after the flow completes.
12. THE AuthFlowState SHALL expose a `Nonce` property of type `string` for OIDC nonce binding. The Nonce is generated by the dispatcher in 10C using a CSPRNG (`RandomNumberGenerator`) and validated against the `nonce` claim in the ID token on callback. 10A accepts any string up to the configured max length (128 chars).
13. THE AuthFlowState SHALL expose nullable `CreatedByIp` and `CreatedByUserAgent` properties for audit context.
14. THE AuthFlowState SHALL expose an `ExpiresAt` property of type `DateTime` representing the UTC instant after which the row must not be consumed.
15. WHEN an AuthFlowState is created without an explicit `ExpiresAt`, THE AuthFlowStateService SHALL default `ExpiresAt` to `DateTime.UtcNow + TimeSpan.FromMinutes(15)`.
16. THE AuthFlowState SHALL expose a nullable `ConsumedAt` property of type `DateTime?` set transactionally when the row transitions to `Consumed`.
17. THE AuthFlowState SHALL expose a nullable `FailureReason` property of type `string?` set when the row transitions to `Failed`.
18. THE AuthFlowState SHALL expose a `TerminatedAt` property of type `DateTime?` set when the row transitions to any terminal state (Consumed, Expired, or Failed). This is the clock used by the retention pruning sweeper — it represents the time the row left the `Pending` state.
19. THE AuthFlowState SHALL be persisted in the existing `AuthDbContext` (alongside `Users`, `Tenants`, `Roles`).

### Requirement 8: FlowType and FlowStatus Enums (Framework Developer)

**User Story:** As a GroundUp framework developer, I want the `FlowType` and `FlowStatus` enums defined now, so that the `AuthFlowState` entity, repository, and service skeleton compile against stable types and Phase 10C–10E only need to reference (not redefine) them.

#### Acceptance Criteria

1. THE FlowType SHALL live in `GroundUp.Auth.Core/Enums/FlowType.cs` under the `GroundUp.Auth.Core.Enums` namespace.
2. THE FlowType SHALL define the values `NewOrganization`, `Invitation`, `JoinLink`, `EnterpriseFirstAdmin`, `EnterpriseSsoAutoJoin`, `MultiTenantSelection`, `TokenRefresh`. (The set may be expanded in later phases; this is the initial canonical set covering the seven Phase 10 flows.)
3. THE FlowStatus SHALL live in `GroundUp.Auth.Core/Enums/FlowStatus.cs` under the `GroundUp.Auth.Core.Enums` namespace.
4. THE FlowStatus SHALL define the values `Pending`, `Consumed`, `Expired`, `Failed`.
5. THE FlowStatus SHALL define `Pending` as the zero-valued (default) member.
6. ALL FlowType and FlowStatus values SHALL have explicit underlying integer values to keep the persisted representation stable across refactors (e.g., `NewOrganization = 0`, `Invitation = 1`, etc.).

### Requirement 9: AuthFlowState Persistence — EF Configuration (Framework Developer)

**User Story:** As a GroundUp framework developer, I want a Fluent API entity configuration for `AuthFlowState` with appropriate indexes and column constraints, so that the cleanup sweeper performs efficient queries and string fields are sized predictably.

#### Acceptance Criteria

1. THE AuthFlowStateConfiguration SHALL live in `src/GroundUp.Auth.Data.Postgres/Configurations/AuthFlowStateConfiguration.cs` and implement `IEntityTypeConfiguration<AuthFlowState>`.
2. THE AuthFlowStateConfiguration SHALL configure the table name as `AuthFlowStates`.
3. THE AuthFlowStateConfiguration SHALL configure the primary key on `Id`.
4. THE AuthFlowStateConfiguration SHALL configure a non-unique index on `(Status, ExpiresAt)` to support the cleanup sweeper's range scans.
5. THE AuthFlowStateConfiguration SHALL configure a non-unique index on `(TenantId)` for tenant-scoped lookups.
6. THE AuthFlowStateConfiguration SHALL configure the `FlowType` and `Status` columns to be persisted as the underlying integer (per project convention) with a provider-defined column type.
7. THE AuthFlowStateConfiguration SHALL configure `Nonce` as required with a maximum length of 128 characters.
8. THE AuthFlowStateConfiguration SHALL configure `Realm` as nullable with a maximum length of 128 characters.
9. THE AuthFlowStateConfiguration SHALL configure `ReturnUrl` as nullable with a maximum length of 2048 characters.
10. THE AuthFlowStateConfiguration SHALL configure `CreatedByIp` as nullable with a maximum length of 64 characters (sufficient for IPv6).
11. THE AuthFlowStateConfiguration SHALL configure `CreatedByUserAgent` as nullable with a maximum length of 512 characters.
12. THE AuthFlowStateConfiguration SHALL configure `FailureReason` as nullable with a maximum length of 1024 characters.
13. THE AuthFlowStateConfiguration SHALL configure `TerminatedAt` as nullable.
14. THE AuthFlowStateConfiguration SHALL NOT use data annotations for any schema configuration (project convention).

### Requirement 10: AuthFlowState Persistence — Migration (Framework Developer)

**User Story:** As a GroundUp framework developer, I want a single, named EF Core migration adding the `AuthFlowStates` table and dropping the unused `Tenant.CustomDomain` column from the auth database, so that consuming applications get both schema changes as a self-contained step alongside Phase 10A.

#### Acceptance Criteria

1. THE Migration SHALL be added to the `GroundUp.Auth.Data.Postgres/Migrations` folder.
2. THE Migration SHALL create the `AuthFlowStates` table with all columns defined by Requirement 9.
3. THE Migration SHALL create the indexes defined by Requirement 9 (`(Status, ExpiresAt)` and `(TenantId)`).
4. THE Migration SHALL drop the `CustomDomain` column from the `Tenants` table per Requirement 21.
5. THE Migration SHALL be reversible (its `Down` method SHALL drop the `AuthFlowStates` table and its indexes cleanly, and re-add the `CustomDomain` column with the same type, length, and nullability).
6. THE Migration SHALL be named with a descriptive identifier such as `AddAuthFlowStatesAndDropTenantCustomDomain`.

### Requirement 11: AuthFlowState DTOs (Framework Developer)

**User Story:** As a GroundUp framework developer, I want a single read DTO and a small set of input DTOs for `AuthFlowState`, so that the service layer in 10C can consume strongly-typed shapes without touching entity types directly.

#### Acceptance Criteria

1. THE AuthFlowStateDto SHALL be a record under `GroundUp.Auth.Core/Dtos/AuthFlowStateDto.cs` mirroring the entity's read-relevant properties: `Id`, `FlowType`, `Status`, `TenantId`, `InvitationId`, `JoinLinkId`, `Realm`, `ReturnUrl`, `Nonce`, `CreatedByIp`, `CreatedByUserAgent`, `ExpiresAt`, `ConsumedAt`, `TerminatedAt`, `FailureReason`, `CreatedAt`, `UpdatedAt`.
2. THE InitiateAuthFlowRequest SHALL be a record carrying the inputs for `IAuthFlowStateService.InitiateAsync`: `FlowType FlowType`, `Guid? TenantId`, `Guid? InvitationId`, `Guid? JoinLinkId`, `string? Realm`, `string? ReturnUrl`, `string Nonce`, `string? CreatedByIp`, `string? CreatedByUserAgent`, `TimeSpan? Lifetime`.
3. THE Mapperly mapper for `AuthFlowState` ↔ `AuthFlowStateDto` SHALL live in the appropriate mapper folder following existing conventions (parallel to other auth-module mappers).
4. ALL AuthFlowState_DTOs SHALL use the `record` keyword.
5. Phase 10A SHALL NOT expose `AuthFlowStateDto` via any HTTP controller or public API endpoint.

### Requirement 12: IAuthFlowStateRepository — Standard CRUD (Framework Developer)

**User Story:** As a GroundUp framework developer, I want `IAuthFlowStateRepository` to follow the standard `BaseRepository<TEntity, TDto>` pattern, so that get/add operations follow the same conventions as every other auth-module repository.

#### Acceptance Criteria

1. THE IAuthFlowStateRepository SHALL inherit from `IBaseRepository<AuthFlowStateDto>` to get standard CRUD signatures.
2. THE AuthFlowStateRepository SHALL inherit from `BaseRepository<AuthFlowState, AuthFlowStateDto>` (NOT `BaseTenantRepository`, because `AuthFlowState` does not implement `ITenantEntity`).
3. THE AuthFlowStateRepository SHALL live in `src/GroundUp.Auth.Repositories/AuthFlowStateRepository.cs`.
4. THE IAuthFlowStateRepository SHALL live in `src/GroundUp.Auth.Data.Abstractions/IAuthFlowStateRepository.cs`.
5. THE AuthFlowStateRepository SHALL register against the `AuthDbContext` (the same DbContext as `Users`, `Tenants`, etc.).

### Requirement 13: IAuthFlowStateRepository — One-Shot Consumption (Framework Developer)

**User Story:** As a GroundUp framework developer, I want a single repository operation that atomically marks an `AuthFlowState` row as `Consumed`, so that even concurrent OAuth callbacks from a replayed `state` value can never both succeed.

#### Acceptance Criteria

1. THE IAuthFlowStateRepository SHALL define `MarkConsumedAsync(Guid id, CancellationToken cancellationToken)` returning `Task<OperationResult<AuthFlowStateDto>>`.
2. WHEN `MarkConsumedAsync` is called and the row exists with `Status = Pending` and `ExpiresAt > DateTime.UtcNow`, THE AuthFlowStateRepository SHALL atomically set `Status = Consumed`, `ConsumedAt = DateTime.UtcNow`, and `TerminatedAt = DateTime.UtcNow`, and return the consumed row.
3. THE AuthFlowStateRepository SHALL implement the consumption transition using a single conditional UPDATE statement (e.g., `UPDATE ... WHERE Id = @id AND Status = 0 AND ExpiresAt > @now` plus `RETURNING`) so that exactly one of any two concurrent callers observes a row affected. If the UPDATE affects 0 rows, the implementation SHALL perform a follow-up read to determine the failure reason (row missing, already consumed/failed, or expired) and return the appropriate distinct failure result.
4. IF the row does not exist, THEN THE AuthFlowStateRepository SHALL return `OperationResult.NotFound`.
5. IF the row exists but `Status != Pending`, THEN THE AuthFlowStateRepository SHALL return a failure result distinguishable as "already consumed / expired / failed" (e.g., `OperationResult.Conflict` or a domain-specific failure code).
6. IF the row exists with `Status = Pending` but `ExpiresAt <= DateTime.UtcNow`, THEN THE AuthFlowStateRepository SHALL return a failure result distinguishable as "expired" and SHALL NOT set `ConsumedAt`.
7. THE AuthFlowStateRepository SHALL NOT throw exceptions for any of the above business outcomes.

### Requirement 14: IAuthFlowStateRepository — Failure Marking (Framework Developer)

**User Story:** As a GroundUp framework developer, I want a repository operation that marks an `AuthFlowState` row as `Failed` with a reason, so that flow handlers in 10C–10E can record terminal failures (e.g., upstream IdP error, validation failure) for audit and debugging.

#### Acceptance Criteria

1. THE IAuthFlowStateRepository SHALL define `MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken)` returning `Task<OperationResult<AuthFlowStateDto>>`.
2. WHEN `MarkFailedAsync` is called and the row exists with `Status = Pending`, THE AuthFlowStateRepository SHALL set `Status = Failed`, `FailureReason = reason`, `TerminatedAt = DateTime.UtcNow`, and return the failed row.
3. IF the row does not exist, THEN THE AuthFlowStateRepository SHALL return `OperationResult.NotFound`.
4. IF the row exists but `Status != Pending`, THEN THE AuthFlowStateRepository SHALL return a failure result distinguishable as "already terminal".
5. THE AuthFlowStateRepository SHALL truncate or reject `reason` values exceeding the configured column length (Requirement 9.12).

### Requirement 15: IAuthFlowStateRepository — Sweeper Operations (Framework Developer)

**User Story:** As a GroundUp framework developer, I want bulk-operation methods on the repository that the cleanup sweeper can call once per cycle, so that the sweeper does no per-row round-trips and the table stays bounded under load.

#### Acceptance Criteria

1. THE IAuthFlowStateRepository SHALL define `MarkExpiredOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken)` returning `Task<OperationResult<int>>` whose value is the count of rows updated.
2. WHEN `MarkExpiredOlderThanAsync` is called, THE AuthFlowStateRepository SHALL update all rows where `Status = Pending` AND `ExpiresAt <= cutoff`, setting `Status = Expired` and `TerminatedAt = DateTime.UtcNow`, in a single SQL statement (e.g., `ExecuteUpdateAsync`).
3. THE IAuthFlowStateRepository SHALL define `DeleteTerminalOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken)` returning `Task<OperationResult<int>>` whose value is the count of rows deleted.
4. WHEN `DeleteTerminalOlderThanAsync` is called, THE AuthFlowStateRepository SHALL delete all rows where `Status IN (Consumed, Expired, Failed)` AND `TerminatedAt <= cutoff`, in a single SQL statement.
5. THE Sweeper_Operations SHALL honour the cancellation token and abandon any in-flight bulk statement promptly when cancellation is requested.

### Requirement 16: IAuthFlowStateService — Initiation (Framework Developer)

**User Story:** As a GroundUp framework developer, I want an `IAuthFlowStateService.InitiateAsync` method that creates a `Pending` row and returns its identifier, so that 10C's dispatcher can embed that identifier as the OAuth `state` parameter when redirecting to Keycloak.

#### Acceptance Criteria

1. THE IAuthFlowStateService SHALL live in `src/GroundUp.Auth.Services/IAuthFlowStateService.cs` under the `GroundUp.Auth.Services` namespace.
2. THE IAuthFlowStateService SHALL define `InitiateAsync(InitiateAuthFlowRequest request, CancellationToken cancellationToken)` returning `Task<OperationResult<AuthFlowStateDto>>`.
3. WHEN `InitiateAsync` is called, THE AuthFlowStateService SHALL create a new `AuthFlowState` row with `Status = Pending`.
4. WHEN `request.Lifetime` is null, THE AuthFlowStateService SHALL set `ExpiresAt = DateTime.UtcNow + TimeSpan.FromMinutes(15)`.
5. WHERE `request.Lifetime` is provided, THE AuthFlowStateService SHALL set `ExpiresAt = DateTime.UtcNow + request.Lifetime`.
6. THE AuthFlowStateService SHALL copy `FlowType`, `TenantId`, `InvitationId`, `JoinLinkId`, `Realm`, `ReturnUrl`, `Nonce`, `CreatedByIp`, and `CreatedByUserAgent` from the request to the new row without modification.
7. THE AuthFlowStateService SHALL return the persisted row (with its server-generated `Id` and `CreatedAt`) on success.
8. IF the request fails FluentValidation (e.g., empty `Nonce`, unknown `FlowType`), THEN THE AuthFlowStateService SHALL return `OperationResult.ValidationFailure` and SHALL NOT persist a row.
9. Phase 10A SHALL NOT implement any flow-handler logic in `AuthFlowStateService`. Initiation is a pure persistence step.

### Requirement 17: IAuthFlowStateService — Consumption (Framework Developer)

**User Story:** As a GroundUp framework developer, I want an `IAuthFlowStateService.ConsumeAsync` method that atomically transitions a `Pending` row to `Consumed` and verifies the expected flow type, so that 10C's dispatcher cannot accidentally process a row of the wrong flow type or process the same row twice.

#### Acceptance Criteria

1. THE IAuthFlowStateService SHALL define `ConsumeAsync(Guid id, FlowType expectedFlowType, CancellationToken cancellationToken)` returning `Task<OperationResult<AuthFlowStateDto>>`.
2. WHEN `ConsumeAsync` is called, THE AuthFlowStateService SHALL invoke `IAuthFlowStateRepository.MarkConsumedAsync(id, cancellationToken)` to perform the atomic transition.
3. IF `MarkConsumedAsync` returns success but the consumed row's `FlowType` does not equal `expectedFlowType`, THEN THE AuthFlowStateService SHALL return a failure result distinguishable as "wrong flow type" without rolling back the consumption (the row stays consumed; the caller is the one who got it wrong).
4. IF `MarkConsumedAsync` returns the "already consumed / failed" result, THEN THE AuthFlowStateService SHALL surface that failure to the caller unchanged.
5. IF `MarkConsumedAsync` returns the "expired" result, THEN THE AuthFlowStateService SHALL surface that failure to the caller unchanged.
6. IF `MarkConsumedAsync` returns `NotFound`, THEN THE AuthFlowStateService SHALL surface `NotFound` to the caller unchanged.

### Requirement 18: IAuthFlowStateService — Failure Marking (Framework Developer)

**User Story:** As a GroundUp framework developer, I want an `IAuthFlowStateService.MarkFailedAsync` method that wraps the repository operation, so that 10C's dispatcher records flow failures via a single service call.

#### Acceptance Criteria

1. THE IAuthFlowStateService SHALL define `MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken)` returning `Task<OperationResult<AuthFlowStateDto>>`.
2. THE AuthFlowStateService SHALL delegate to `IAuthFlowStateRepository.MarkFailedAsync` and surface the result unchanged.
3. IF `reason` is null, empty, or whitespace, THEN THE AuthFlowStateService SHALL return `OperationResult.ValidationFailure` and SHALL NOT call the repository.

### Requirement 19: IAuthCookieWriter Interface (Framework Developer)

**User Story:** As a GroundUp framework developer, I want the `IAuthCookieWriter` contract defined now, so that 10C's flow handlers and `AuthController` write the auth cookie through a single, testable abstraction that already honours all `AuthOptions` cookie settings and derives the cookie's `Domain` attribute from the application's configured default domain.

#### Acceptance Criteria

1. THE IAuthCookieWriter SHALL live in `src/GroundUp.Auth.Services/IAuthCookieWriter.cs` under the `GroundUp.Auth.Services` namespace.
2. THE IAuthCookieWriter SHALL define `WriteAuthCookie(HttpContext httpContext, string token)`.
3. THE IAuthCookieWriter SHALL define `ClearAuthCookie(HttpContext httpContext)`.
4. THE IAuthCookieWriter contract SHALL state (in its XML documentation) that implementations MUST honour `AuthOptions.CookieName`, `AuthOptions.CookieSecure`, and `AuthOptions.CookieSameSite`.
5. THE IAuthCookieWriter contract SHALL state (in its XML documentation) that implementations MUST derive the cookie's `Domain` attribute from the `auth.application.default-domain` system setting using these rules:
   a. WHERE the setting value is null or empty, the implementation MUST NOT set the cookie's `Domain` attribute (host-only cookie — appropriate for single-host / single-tenant deployments).
   b. WHERE the setting value is non-empty (e.g., `sampleapp.com`), the implementation MUST set the cookie's `Domain` attribute to the value with a leading dot prepended (e.g., `.sampleapp.com`) so the cookie is shared across all subdomains of the configured app domain.
6. THE IAuthCookieWriter contract SHALL state (in its XML documentation) that the cookie expiration SHALL align with `AuthOptions.TokenExpirationMinutes`.
7. Phase 10A SHALL NOT ship an implementation of `IAuthCookieWriter` — only the interface.

### Requirement 20: AuthOptions Cleanup Configuration (Consuming App Developer)

**User Story:** As a consuming app developer, I want to tune the cleanup sweeper's interval and retention window via `AuthOptions`, so that I can shorten retention in compliance-sensitive deployments or lengthen it for forensic investigations without recompiling.

#### Acceptance Criteria

1. THE AuthOptions SHALL expose a `CleanupIntervalMinutes` property of type `int` with a default value of `5`.
2. THE AuthOptions SHALL expose a `RetentionDays` property of type `int` with a default value of `7`.
3. BOTH properties SHALL be bindable from the `GroundUp:Auth` configuration section.
4. THE AddGroundUpAuth options validation SHALL fail startup IF `CleanupIntervalMinutes <= 0`.
5. THE AddGroundUpAuth options validation SHALL fail startup IF `RetentionDays < 0`.

### Requirement 21: Drop Tenant.CustomDomain Column (Framework Developer)

**User Story:** As a GroundUp framework developer, I want the `Tenant.CustomDomain` column dropped as part of the 10A migration, so that we don't carry an unused column forward indefinitely now that cross-registrable-domain handoff is out of scope (enterprise customers wanting their own domain self-deploy a single-tenant instance instead).

#### Acceptance Criteria

1. THE 10A Migration SHALL drop the `CustomDomain` column from the `Tenants` table in its `Up` step, in addition to creating the `AuthFlowStates` table.
2. THE 10A Migration's `Down` step SHALL re-add the `CustomDomain` column with the same type, length, and nullability it had before, in addition to dropping the `AuthFlowStates` table.
3. THE Tenant entity in `GroundUp.Auth.Core/Entities/Tenant.cs` SHALL have its `CustomDomain` property removed.
4. THE TenantDto in `GroundUp.Auth.Core/Dtos/TenantDto.cs` SHALL have its `CustomDomain` property removed.
5. THE CreateTenantDto in `GroundUp.Auth.Core/Dtos/CreateTenantDto.cs` SHALL have its `CustomDomain` property removed.
6. THE UpdateTenantDto SHALL have its `CustomDomain` property removed if present.
7. THE TenantConfiguration EF configuration SHALL have its `CustomDomain` mapping removed.
8. ALL references to `CustomDomain` in tests, validators, and mappers SHALL be removed.
9. The framework SHALL still compile and all existing tests SHALL pass after these removals.

### Requirement 22: Authentication System Settings (Consuming App Developer)

**User Story:** As a consuming app developer, I want a small set of system-only settings seeded on startup that capture deployment-level authentication facts — the application's default domain and the shared Keycloak realm name — so that 10C's host tenant resolver and the identity-provider service layer can read these values from a single source of truth instead of from hardcoded strings or environment variables.

#### Acceptance Criteria

1. THE DefaultAuthSettingsSeeder SHALL be a new `IDataSeeder` implementation in `src/GroundUp.Auth.Data.Postgres/Seeders/DefaultAuthSettingsSeeder.cs`.
2. THE DefaultAuthSettingsSeeder SHALL run after `DefaultPermissionSeeder` and `DefaultSystemRoleSeeder` (i.e., its `Order` SHALL be greater than 20).
3. THE DefaultAuthSettingsSeeder SHALL call `ISettingsService.EnsureDefinitionAsync(...)` (a new idempotent method added to `ISettingsService` as part of Phase 10A — see Requirement 22a) to create each setting definition. It SHALL NOT write to the Settings DbContext directly, per the framework's cross-module communication rule.
4. THE DefaultAuthSettingsSeeder SHALL ensure a `SettingGroup` exists with Key `"auth"` and DisplayName `"Authentication"`. If the group already exists, it SHALL NOT modify it.
5. THE DefaultAuthSettingsSeeder SHALL seed the following three `SettingDefinition` records (all assigned to the `"auth"` group):

   **Setting 1: `auth.application.default-domain`**
   - DisplayName: `"Default Application Domain"`
   - DataType: `SettingDataType.String`
   - DefaultValue: `""`
   - Description: `"The default application domain used by the host tenant resolver to identify subdomain-based tenant routing. Example: 'sampleapp.com'. Leave empty for single-host / single-tenant deployments."`
   - Category: `"Domain"`
   - RegexPattern: `"^$|^(localhost|([a-z0-9]([a-z0-9-]*[a-z0-9])?\\.)+[a-z]{2,})$"` (allows empty string, `localhost`, or a valid FQDN without scheme or trailing slash)
   - ValidationMessage: `"Must be empty, 'localhost', or a valid domain name (e.g., 'sampleapp.com'). Do not include https:// or trailing slashes."`

   **Setting 2: `auth.keycloak.shared-realm-name`**
   - DisplayName: `"Shared Keycloak Realm Name"`
   - DataType: `SettingDataType.String`
   - DefaultValue: `"groundup"`
   - Description: `"The Keycloak realm name used for standard (non-enterprise) tenants. All standard-tenant flows authenticate against this realm. Enterprise tenants override this via Tenant.RealmName. Changing this setting at runtime requires the realm to already exist in Keycloak with that name."`
   - Category: `"Identity Provider"`

   **Setting 3: `auth.keycloak.public-base-url`**
   - DisplayName: `"Keycloak Public Base URL"`
   - DataType: `SettingDataType.String`
   - DefaultValue: `""`
   - Description: `"The publicly-reachable base URL of the Keycloak server, used by the GroundUp API to build deep-link URLs for the consuming application's UI to navigate to realm-specific Keycloak admin pages. Example: 'https://auth.sampleapp.com'. If empty, Keycloak admin links are unavailable."`
   - Category: `"Identity Provider"`

6. FOR EACH setting definition, THE DefaultAuthSettingsSeeder SHALL create a `SettingDefinitionLevel` junction row linking the definition to the `SettingLevel` with Name `"System"`. This restricts each setting to system-level only — no tenant, application, or feature level overrides.
7. THE DefaultAuthSettingsSeeder SHALL be idempotent — re-running on a database that already contains any of the three settings SHALL be a no-op for the existing rows.
8. THE Realm_Import_File (`keycloak/realm.json`) SHALL define a realm with `"realm": "groundup"` so that the seeded default value for `auth.keycloak.shared-realm-name` matches the imported realm out of the box.

### Requirement 22a: ISettingsService.EnsureDefinitionAsync (Framework Developer)

**User Story:** As a GroundUp framework developer, I want an idempotent method on `ISettingsService` that creates a setting definition (with its group and allowed levels) if it doesn't already exist, so that cross-module seeders can register their settings through the service interface without directly accessing the Settings module's DbContext.

#### Acceptance Criteria

1. THE `ISettingsService` interface SHALL be extended with a new method: `EnsureDefinitionAsync(EnsureSettingDefinitionRequest request, CancellationToken cancellationToken)` returning `Task<OperationResult<SettingDefinitionDto>>`.
2. THE `EnsureSettingDefinitionRequest` SHALL be a new record in `GroundUp.Core/Dtos/Settings/` carrying at minimum: `Key`, `DataType`, `DefaultValue`, `DisplayName`, `Description`, `Category`, `GroupKey`, `GroupDisplayName`, `AllowedLevelNames` (IReadOnlyList<string>), and optional validation fields (`RegexPattern`, `ValidationMessage`, `IsRequired`, `IsSecret`, `IsEncrypted`).
3. WHEN `EnsureDefinitionAsync` is called and a `SettingDefinition` with the specified `Key` already exists, THE method SHALL return the existing definition without modification (true idempotent — no upsert).
4. WHEN `EnsureDefinitionAsync` is called and no definition with the specified `Key` exists, THE method SHALL: (a) ensure the `SettingGroup` with `GroupKey` exists (create if not), (b) create the `SettingDefinition` row, (c) create `SettingDefinitionLevel` junction rows for each level name in `AllowedLevelNames`, (d) return the created definition.
5. THE method SHALL resolve `SettingLevel` rows by `Name` (e.g., "System"). If a level name doesn't exist, the method SHALL return `OperationResult.BadRequest` with a clear error.
6. This is a SMALL additive change to the Settings module's service interface. It does NOT modify any existing method signatures or behavior.

### Requirement 23: AuthFlowState Cleanup Sweeper — Expiration (Framework Developer)

**User Story:** As a GroundUp framework developer, I want an `IHostedService`-based sweeper that periodically marks expired `Pending` rows as `Expired`, so that no stale flow can be redeemed by a late-arriving callback even if the row physically still exists.

#### Acceptance Criteria

1. THE AuthFlowStateCleanupSweeper SHALL be a sealed class implementing `IHostedService` (or `BackgroundService`) in `src/GroundUp.Auth.Services/AuthFlowStateCleanupSweeper.cs`.
2. THE AuthFlowStateCleanupSweeper SHALL invoke its sweep cycle every `AuthOptions.CleanupIntervalMinutes` (default 5 minutes), using a `PeriodicTimer` or equivalent non-allocating mechanism.
3. EACH sweep cycle SHALL call `IAuthFlowStateRepository.MarkExpiredOlderThanAsync(DateTime.UtcNow, cancellationToken)` exactly once.
4. EACH sweep cycle SHALL resolve `IAuthFlowStateRepository` from a freshly created scope (since the repository is scoped and the sweeper itself is a singleton).
5. WHEN the sweeper marks one or more rows as `Expired`, THE AuthFlowStateCleanupSweeper SHALL log an information-level message containing the count of rows expired.
6. IF the repository call throws an unexpected exception, THEN THE AuthFlowStateCleanupSweeper SHALL log the exception at `Error` level and SHALL continue running on the next cycle (a single failed sweep MUST NOT take down the host).
7. IN a multi-instance deployment where multiple application instances run the same sweeper, THE bulk SQL operations (ExecuteUpdateAsync / ExecuteDeleteAsync) are inherently safe because they are atomic per-row; the worst case is that two instances both run a sweep and each affects a disjoint subset of rows (or one affects 0 because the other already processed them). No additional distributed locking is required.
8. WHEN a sweep cycle completes with 0 rows expired AND 0 rows deleted, THE AuthFlowStateCleanupSweeper SHALL log at Debug level indicating the cycle ran but found no work. This allows operations teams to verify the sweeper is alive without cluttering production logs.

### Requirement 24: AuthFlowState Cleanup Sweeper — Retention Pruning (Framework Developer)

**User Story:** As a GroundUp framework developer, I want the sweeper to delete terminal-state rows older than the retention window, so that the `AuthFlowStates` table does not grow unbounded while still preserving recent rows for audit and debugging.

#### Acceptance Criteria

1. EACH sweep cycle SHALL call `IAuthFlowStateRepository.DeleteTerminalOlderThanAsync(DateTime.UtcNow - TimeSpan.FromDays(AuthOptions.RetentionDays), cancellationToken)` exactly once after the expiration step.
2. WHEN the sweeper deletes one or more rows, THE AuthFlowStateCleanupSweeper SHALL log an information-level message containing the count of rows deleted.
3. THE AuthFlowStateCleanupSweeper SHALL run the expiration step before the deletion step in every cycle so that newly-expired rows are not deleted in the same cycle they were created.

### Requirement 25: AuthFlowState Cleanup Sweeper — Lifecycle and Cancellation (Framework Developer)

**User Story:** As a GroundUp framework developer, I want the sweeper to honour graceful shutdown, so that the host can stop cleanly during deploys and tests.

#### Acceptance Criteria

1. THE AuthFlowStateCleanupSweeper SHALL stop its periodic timer and exit its loop when the cancellation token passed to `ExecuteAsync` (or equivalent lifecycle method) is signalled.
2. THE AuthFlowStateCleanupSweeper SHALL pass the lifecycle cancellation token through to repository calls, so that an in-flight sweep aborts promptly on shutdown.
3. THE AuthFlowStateCleanupSweeper SHALL be registered with `services.AddHostedService<AuthFlowStateCleanupSweeper>()` and SHALL therefore be started by the host's `IHostedService` orchestration.
4. THE AuthFlowStateCleanupSweeper's main loop SHALL catch ALL exceptions (including from the timer mechanism itself) and continue to the next cycle. The ONLY condition that exits the loop SHALL be cancellation via the lifecycle token.

### Requirement 26: DI Registration Extension (Consuming App Developer)

**User Story:** As a consuming app developer, I want all Phase 10A components — the repository, the service skeleton, and the sweeper — registered through the existing `AddGroundUpAuth(...)` call, so that I do not need to remember to call any additional setup method.

#### Acceptance Criteria

1. WHEN `services.AddGroundUpAuth(...)` is called, THE Auth_Service_Collection_Extension SHALL register `IAuthFlowStateRepository` as a scoped service backed by `AuthFlowStateRepository`.
2. WHEN `services.AddGroundUpAuth(...)` is called, THE Auth_Service_Collection_Extension SHALL register `IAuthFlowStateService` as a scoped service backed by `AuthFlowStateService`.
3. WHEN `services.AddGroundUpAuth(...)` is called, THE Auth_Service_Collection_Extension SHALL register `AuthFlowStateCleanupSweeper` via `AddHostedService<AuthFlowStateCleanupSweeper>()`.
4. WHEN `services.AddGroundUpAuth(...)` is called, THE Auth_Service_Collection_Extension SHALL NOT register any implementation of `IIdentityProviderAdminService` (10B owns that registration).
5. WHEN `services.AddGroundUpAuth(...)` is called, THE Auth_Service_Collection_Extension SHALL NOT register any implementation of `IAuthCookieWriter` (10C owns that registration).
6. THE Phase 10A extension wiring MAY be expressed as a private helper inside `AuthServiceCollectionExtensions` or as a new internal sub-extension method `AddGroundUpAuthFlowState(...)` invoked by `AddGroundUpAuth`. The public surface SHALL remain `AddGroundUpAuth`.

### Requirement 27: Boundary Constraints — No Flow Logic In 10A (Framework Developer)

**User Story:** As a GroundUp framework developer, I want a clear set of negative requirements that explicitly call out what does NOT ship in 10A, so that scope creep is prevented and 10B–10E reviewers can spot violations during code review.

#### Acceptance Criteria

1. Phase 10A SHALL NOT ship any implementation of `IIdentityProviderAdminService` (10B).
2. Phase 10A SHALL NOT ship any implementation of `IAuthCookieWriter` (10C).
3. Phase 10A SHALL NOT ship any controllers, action methods, or HTTP endpoints (10C+).
4. Phase 10A SHALL NOT ship any flow-specific handler logic inside `AuthFlowStateService` (10C+).
5. Phase 10A SHALL NOT ship `IHostTenantResolver` or any host-resolution middleware (10C).
6. Phase 10A SHALL NOT ship `AuthUrlBuilderService` or any URL-building logic (10C).
7. Phase 10A SHALL NOT introduce a new project — all framework changes land in existing projects (`GroundUp.Auth.Core`, `GroundUp.Auth.Data.Abstractions`, `GroundUp.Auth.Repositories`, `GroundUp.Auth.Data.Postgres`, `GroundUp.Auth.Services`).
8. Phase 10A SHALL NOT modify any Phase 9 contract that is depended on by application code (additive changes to `AuthOptions` are explicitly allowed; changes to `ITokenService`, `IAuthSessionService`, `IPermissionService`, etc., are not).
9. Phase 10A SHALL NOT ship the Keycloak admin deep-link API endpoint, the deep-link URL builder service, or any HTTP surface that consumes `auth.keycloak.public-base-url`. The setting is seeded in 10A so consuming apps and operators have a place to configure it; the URL builder and the API endpoint that exposes the resulting deep-links live in 10C / 10E alongside the tenant-management controller.
10. Phase 10A SHALL NOT introduce any cross-registrable-domain cookie handoff mechanism (no exchange-token endpoint, no DomainExchange flow type, no signed-token redirect). Enterprise customers wanting their own registrable domain self-deploy a single-tenant instance.

### Requirement 28: Test Coverage (Framework Developer)

**User Story:** As a GroundUp framework developer, I want the Phase 10A components covered by unit, integration, and property-based tests, so that the foundation is verified before any flow logic is layered on top.

#### Acceptance Criteria

1. THE AuthFlowStateRepository SHALL be covered by integration tests against Testcontainers Postgres exercising: standard CRUD, `MarkConsumedAsync` happy path, `MarkConsumedAsync` already-consumed rejection, `MarkConsumedAsync` expired rejection, `MarkFailedAsync` happy path, `MarkExpiredOlderThanAsync` (only `Pending` rows past cutoff transition), `DeleteTerminalOlderThanAsync` (only terminal rows past cutoff are deleted).
2. THE AuthFlowStateService SHALL be covered by unit tests using NSubstitute for the repository, exercising: `InitiateAsync` default lifetime, `InitiateAsync` custom lifetime, `ConsumeAsync` happy path, `ConsumeAsync` wrong flow type, `MarkFailedAsync` validation failure on empty reason.
3. THE AuthFlowStateCleanupSweeper SHALL be covered by an integration test that sets `CleanupIntervalMinutes` to a sub-second override (e.g., 100ms) and verifies that an expired `Pending` row transitions to `Expired` within two cycle intervals.
4. THE AuthFlowStateCleanupSweeper SHALL be covered by an integration test that sets `RetentionDays` to 0 and verifies that a terminal-state row created in the past is deleted within two cycle intervals.
5. THE Property_Based_Tests SHALL be implemented using FsCheck per the framework's standard for the properties listed in the Correctness Properties section below.
6. THE existing test suite SHALL continue to pass after Phase 10A changes are merged (no regression).
7. THE DefaultAuthSettingsSeeder SHALL be covered by an integration test verifying: (a) first run creates all three definitions with correct groups, categories, and level restrictions; (b) second run is idempotent (no duplicates, no modifications to existing rows).
8. THE AuthFlowStateService SHALL include a unit test verifying that `InitiateAsync` rejects a Nonce longer than 128 characters with a validation failure.

## Correctness Properties

The following properties MUST hold for any valid Phase 10A implementation. They are tracked for property-based testing via FsCheck.

### Property 1: One-Shot Consumption is Idempotent in the Outcome Sense

For any `Pending`, non-expired `AuthFlowState` row with id `x`, calling `ConsumeAsync(x, ...)` twice — concurrently or sequentially — SHALL result in exactly one success outcome and one "already consumed" failure outcome. Two successes SHALL never be observed.

### Property 2: Initiate → Consume Round-Trip Preserves Metadata

For any valid `InitiateAuthFlowRequest req`, the row returned by `ConsumeAsync(initiated.Id, req.FlowType, ...)` (where `initiated` is the result of `InitiateAsync(req)`) SHALL have:

- `Id == initiated.Id`
- `FlowType == req.FlowType`
- `TenantId == req.TenantId`
- `InvitationId == req.InvitationId`
- `JoinLinkId == req.JoinLinkId`
- `Realm == req.Realm`
- `ReturnUrl == req.ReturnUrl`
- `Nonce == req.Nonce`
- `CreatedByIp == req.CreatedByIp`
- `CreatedByUserAgent == req.CreatedByUserAgent`
- `Status == FlowStatus.Consumed`
- `ConsumedAt != null`

### Property 3: Concurrent Consumption Selects Exactly One Winner

For any `Pending`, non-expired row with id `x` and any `n >= 2`, launching `n` concurrent `ConsumeAsync(x, ...)` calls SHALL yield exactly one success result; the remaining `n - 1` results SHALL be the "already consumed" failure.

### Property 4: Sweeper Expiration Transition Correctness

For any `AuthFlowState` row with `Status == Pending` and `ExpiresAt <= DateTime.UtcNow`, after one full sweep cycle of `AuthFlowStateCleanupSweeper`, the row SHALL satisfy `Status == Expired`. Rows with `ExpiresAt > DateTime.UtcNow` SHALL remain `Pending`.

### Property 5: Sweeper Retention Pruning Correctness

For any `AuthFlowState` row with `Status IN (Consumed, Expired, Failed)` and `TerminatedAt <= DateTime.UtcNow - TimeSpan.FromDays(RetentionDays)`, after one full sweep cycle the row SHALL no longer exist in the database. Rows whose `TerminatedAt` is newer than the cutoff, and rows still in `Pending` (which have `TerminatedAt = null`), SHALL remain.

### Property 6: Expired Rows Cannot Be Consumed

For any `AuthFlowState` row whose `ExpiresAt <= DateTime.UtcNow`, every call to `ConsumeAsync` SHALL return a non-success result, regardless of whether the sweeper has yet transitioned the row's `Status` from `Pending` to `Expired`.

---

End of Requirements.
