# Requirements Document

## Introduction

Phase 9A establishes the core authentication and authorization data model for the GroundUp framework. This includes entities, enums, DTOs, FluentValidation validators, and security attributes that form the foundation of the permission system. The authentication module is fully optional — the core GroundUp.Api module works without it.

The key architectural decisions are:
- **Auth types live in a dedicated `GroundUp.Auth.Core` project** — consuming apps that don't need authentication won't have auth entities in their dependency tree. This keeps GroundUp.Core lean and focused on universally-needed types.
- **Security attributes stay in `GroundUp.Core`** — `RequiresPermissionAttribute` and `RequiresRoleAttribute` are available to any layer without depending on the auth module.
- **Keycloak is a protocol engine only** — GroundUp owns all permission logic (Permission → Policy → Role hierarchy)
- **Multi-tenant users** — a User can belong to multiple Tenants via the UserTenant junction, each with a per-tenant ExternalUserId mapping the IdP identity to the GroundUp user
- **Tenant hierarchy** — Tenants support parent/child relationships via ParentTenantId for enterprise SSO federation

All entities follow existing GroundUp conventions: extend BaseEntity (UUID v7 Id), opt into IAuditable/ISoftDeletable via interfaces, sealed classes, file-scoped namespaces, XML doc comments. DTOs are records with Dto suffix. Validators use FluentValidation.

**What stays in GroundUp.Core:**
- `RequiresPermissionAttribute` (in `src/GroundUp.Core/Attributes/`)
- `RequiresRoleAttribute` (in `src/GroundUp.Core/Attributes/`)
- `ICurrentUser` (already exists)
- `ITenantContext` (already exists)

**What goes in the NEW GroundUp.Auth.Core project:**
- All auth entities (in `Entities/` folder)
- All auth enums (in `Enums/` folder)
- All auth DTOs (in `Dtos/` folder)
- All auth validators (in `Validators/` folder)

**Rationale:** Auth is fully optional. A consuming app that doesn't need authentication shouldn't have auth entities in their dependency tree. Settings stays in Core because virtually every app needs settings. Auth is different — many apps (internal tools, simple APIs) don't need roles/permissions.

Phase 9A does NOT include: repositories, services, EF Core configurations, API controllers, Keycloak integration, permission enforcement logic, or caching. Those are covered in Phases 9B–9E.

## Glossary

- **User**: An entity representing an authenticated user in the GroundUp system. Has a global identity (Id, Email, DisplayName) independent of any specific tenant.
- **Tenant**: An entity representing an organizational boundary. Supports standard and enterprise types, hierarchical relationships (ParentTenantId), custom domains, and realm names for IdP routing.
- **UserTenant**: A junction entity linking a User to a Tenant with a per-tenant ExternalUserId that maps the user's identity in each realm/IdP to their GroundUp user record.
- **Role**: An entity representing a named collection of policies. Roles are scoped to a tenant and have a type (System, Application, Workspace). System roles are framework-defined and immutable.
- **Policy**: An entity representing a named collection of permissions. Policies provide a grouping layer between roles and individual permissions.
- **Permission**: An entity representing a single, granular authorization check (e.g., "settings.read", "users.manage"). Permissions are module-scoped and globally unique by key.
- **RolePolicy**: A junction entity linking a Role to a Policy (many-to-many).
- **PolicyPermission**: A junction entity linking a Policy to a Permission (many-to-many).
- **UserRole**: A junction entity linking a User to a Role within a specific Tenant context.
- **TenantType**: An enum distinguishing standard tenants from enterprise tenants (which support SSO federation).
- **OnboardingMode**: An enum defining how users join a tenant: InviteOnly, JoinLink, or Open.
- **RoleType**: An enum categorizing roles as System (framework-defined), Application (app-defined), or Workspace (user-created within a tenant).
- **RequiresPermissionAttribute**: A custom attribute placed on service interface methods to declare the permission(s) required to invoke that method. Lives in GroundUp.Core so it's available without depending on the auth module.
- **RequiresRoleAttribute**: A custom attribute placed on service interface methods to declare the role(s) required to invoke that method. Lives in GroundUp.Core.
- **BaseEntity**: The abstract base class in `GroundUp.Core` providing a UUID v7 `Id` property for all entities.
- **IAuditable**: The opt-in interface for automatic audit field population (CreatedAt, CreatedBy, UpdatedAt, UpdatedBy).
- **ISoftDeletable**: The opt-in interface for soft delete behavior (IsDeleted, DeletedAt, DeletedBy).
- **ITenantEntity**: The interface declaring tenant ownership for automatic tenant filtering in BaseTenantRepository.

## Requirements

### Requirement 1: GroundUp.Auth.Core Project

**User Story:** As a framework consumer, I want auth entities, DTOs, enums, and validators to live in a dedicated `GroundUp.Auth.Core` project, so that apps that don't need authentication don't pull auth types into their dependency tree.

#### Acceptance Criteria

1. THE project SHALL be created at `src/GroundUp.Auth.Core/GroundUp.Auth.Core.csproj`.
2. THE project SHALL target `net8.0` with nullable reference types enabled and implicit usings enabled.
3. THE project SHALL reference `GroundUp.Core` for access to `BaseEntity`, `IAuditable`, `ISoftDeletable`, and `ITenantEntity`.
4. THE project SHALL reference the `FluentValidation` NuGet package (version 11.*) for validators.
5. THE project SHALL have NO other project or package dependencies beyond those listed above.
6. THE project SHALL be added to the `groundup.sln` solution file.
7. THE project SHALL use the root namespace `GroundUp.Auth.Core`.
8. THE project SHALL enable `GenerateDocumentationFile` for XML doc generation.

### Requirement 2: User Entity

**User Story:** As a framework consumer, I want a User entity that represents an authenticated user's global identity, so that the system can track users independently of any specific tenant or identity provider.

#### Acceptance Criteria

1. THE User entity SHALL extend `BaseEntity` and implement `IAuditable`, and reside in `src/GroundUp.Auth.Core/Entities/User.cs`.
2. THE User entity SHALL have an `ExternalUserId` property of type `string` (required, max length 200) representing the user's primary external identity provider identifier.
3. THE User entity SHALL have an `Email` property of type `string` (required, max length 320) representing the user's email address.
4. THE User entity SHALL have a `DisplayName` property of type `string` (required, max length 200) representing the user's display name.
5. THE User entity SHALL have an `IsActive` property of type `bool` (default true) indicating whether the user account is active.
6. THE User entity SHALL have a `UserTenants` navigation collection of type `ICollection<UserTenant>` for accessing the user's tenant memberships.
7. THE User entity SHALL have a `UserRoles` navigation collection of type `ICollection<UserRole>` for accessing the user's role assignments.
8. THE User entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 3: Tenant Entity

**User Story:** As a framework consumer, I want a Tenant entity that supports standard and enterprise types, hierarchical relationships, custom domains, and realm names, so that the system can model complex organizational structures including enterprise SSO federation.

#### Acceptance Criteria

1. THE Tenant entity SHALL extend `BaseEntity` and implement `IAuditable` and `ISoftDeletable`, and reside in `src/GroundUp.Auth.Core/Entities/Tenant.cs`.
2. THE Tenant entity SHALL have a `Name` property of type `string` (required, max length 200) representing the tenant's display name.
3. THE Tenant entity SHALL have a `Slug` property of type `string` (required, max length 100, unique) representing the URL-friendly identifier for the tenant.
4. THE Tenant entity SHALL have a `TenantType` property of type `TenantType` enum (required) distinguishing standard from enterprise tenants.
5. THE Tenant entity SHALL have an `OnboardingMode` property of type `OnboardingMode` enum (required) defining how users join the tenant.
6. THE Tenant entity SHALL have a `ParentTenantId` property of type `Guid?` forming a self-referencing foreign key to another Tenant for hierarchical relationships.
7. THE Tenant entity SHALL have a `Parent` navigation property of type `Tenant?` and a `Children` navigation collection of type `ICollection<Tenant>` for traversing the hierarchy.
8. THE Tenant entity SHALL have a `RealmName` property of type `string?` (optional, max length 200) for IdP routing in enterprise SSO scenarios.
9. THE Tenant entity SHALL have a `CustomDomain` property of type `string?` (optional, max length 500) for tenant-specific domain routing.
10. THE Tenant entity SHALL have an `IsActive` property of type `bool` (default true) indicating whether the tenant is active.
11. THE Tenant entity SHALL have a `UserTenants` navigation collection of type `ICollection<UserTenant>` for accessing the tenant's user memberships.
12. THE Tenant entity SHALL have a `Roles` navigation collection of type `ICollection<Role>` for accessing the tenant's roles.
13. THE Tenant entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 4: UserTenant Junction Entity

**User Story:** As a framework consumer, I want a junction entity linking users to tenants with a per-tenant external user ID, so that a single user can belong to multiple tenants and each tenant can map the user's IdP identity independently.

#### Acceptance Criteria

1. THE UserTenant entity SHALL extend `BaseEntity` and implement `IAuditable`, and reside in `src/GroundUp.Auth.Core/Entities/UserTenant.cs`.
2. THE UserTenant entity SHALL have a `UserId` property of type `Guid` as a required foreign key to `User`, and a `User` navigation property.
3. THE UserTenant entity SHALL have a `TenantId` property of type `Guid` as a required foreign key to `Tenant`, and a `Tenant` navigation property.
4. THE UserTenant entity SHALL have an `ExternalUserId` property of type `string` (required, max length 200) representing the user's identity in this specific tenant's realm/IdP.
5. THE UserTenant entity SHALL have an `IsActive` property of type `bool` (default true) indicating whether the user's membership in this tenant is active.
6. THE UserTenant entity SHALL have a unique composite constraint on (`UserId`, `TenantId`) to prevent duplicate memberships.
7. THE UserTenant entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 5: Role Entity

**User Story:** As a framework consumer, I want a Role entity that groups policies, is scoped to a tenant, and distinguishes system roles from application and workspace roles, so that the permission system supports both framework-defined and user-defined role hierarchies.

#### Acceptance Criteria

1. THE Role entity SHALL extend `BaseEntity` and implement `IAuditable`, and reside in `src/GroundUp.Auth.Core/Entities/Role.cs`.
2. THE Role entity SHALL have a `Name` property of type `string` (required, max length 200) representing the role name.
3. THE Role entity SHALL have a `Description` property of type `string?` (optional, max length 1000) describing the role's purpose.
4. THE Role entity SHALL have a `RoleType` property of type `RoleType` enum (required) categorizing the role.
5. THE Role entity SHALL have a `TenantId` property of type `Guid` as a required foreign key to `Tenant`, implementing `ITenantEntity`, and a `Tenant` navigation property.
6. THE Role entity SHALL have an `IsSystem` property of type `bool` (default false) indicating whether the role is framework-defined and immutable.
7. THE Role entity SHALL have a `RolePolicies` navigation collection of type `ICollection<RolePolicy>` for accessing the role's policy assignments.
8. THE Role entity SHALL have a `UserRoles` navigation collection of type `ICollection<UserRole>` for accessing the role's user assignments.
9. THE Role entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 6: Policy Entity

**User Story:** As a framework consumer, I want a Policy entity that groups permissions and is scoped to a tenant, so that permissions can be organized into logical bundles that are assigned to roles.

#### Acceptance Criteria

1. THE Policy entity SHALL extend `BaseEntity` and implement `IAuditable`, and reside in `src/GroundUp.Auth.Core/Entities/Policy.cs`.
2. THE Policy entity SHALL have a `Name` property of type `string` (required, max length 200) representing the policy name.
3. THE Policy entity SHALL have a `Description` property of type `string?` (optional, max length 1000) describing the policy's purpose.
4. THE Policy entity SHALL have a `TenantId` property of type `Guid` as a required foreign key to `Tenant`, implementing `ITenantEntity`, and a `Tenant` navigation property.
5. THE Policy entity SHALL have a `RolePolicies` navigation collection of type `ICollection<RolePolicy>` for accessing the policy's role assignments.
6. THE Policy entity SHALL have a `PolicyPermissions` navigation collection of type `ICollection<PolicyPermission>` for accessing the policy's permission assignments.
7. THE Policy entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 7: Permission Entity

**User Story:** As a framework consumer, I want a Permission entity with a globally unique key and module scope, so that each module can define its own granular permissions that are discoverable and assignable to policies.

#### Acceptance Criteria

1. THE Permission entity SHALL extend `BaseEntity` and implement `IAuditable`, and reside in `src/GroundUp.Auth.Core/Entities/Permission.cs`.
2. THE Permission entity SHALL have a `Key` property of type `string` (required, max length 200, unique) serving as the programmatic identifier (e.g., "settings.read", "users.manage").
3. THE Permission entity SHALL have a `Name` property of type `string` (required, max length 200) representing the human-readable permission name.
4. THE Permission entity SHALL have a `Description` property of type `string?` (optional, max length 1000) describing what the permission grants.
5. THE Permission entity SHALL have a `Module` property of type `string` (required, max length 100) identifying which module defines this permission (e.g., "Settings", "Users", "Auth").
6. THE Permission entity SHALL have a `PolicyPermissions` navigation collection of type `ICollection<PolicyPermission>` for accessing the permission's policy assignments.
7. THE Permission entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 8: RolePolicy Junction Entity

**User Story:** As a framework consumer, I want a junction entity linking roles to policies, so that the many-to-many relationship between roles and policies is explicitly modeled with its own identity.

#### Acceptance Criteria

1. THE RolePolicy entity SHALL extend `BaseEntity` and reside in `src/GroundUp.Auth.Core/Entities/RolePolicy.cs`.
2. THE RolePolicy entity SHALL have a `RoleId` property of type `Guid` as a required foreign key to `Role`, and a `Role` navigation property.
3. THE RolePolicy entity SHALL have a `PolicyId` property of type `Guid` as a required foreign key to `Policy`, and a `Policy` navigation property.
4. THE RolePolicy entity SHALL have a unique composite constraint on (`RoleId`, `PolicyId`) to prevent duplicate assignments.
5. THE RolePolicy entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 9: PolicyPermission Junction Entity

**User Story:** As a framework consumer, I want a junction entity linking policies to permissions, so that the many-to-many relationship between policies and permissions is explicitly modeled with its own identity.

#### Acceptance Criteria

1. THE PolicyPermission entity SHALL extend `BaseEntity` and reside in `src/GroundUp.Auth.Core/Entities/PolicyPermission.cs`.
2. THE PolicyPermission entity SHALL have a `PolicyId` property of type `Guid` as a required foreign key to `Policy`, and a `Policy` navigation property.
3. THE PolicyPermission entity SHALL have a `PermissionId` property of type `Guid` as a required foreign key to `Permission`, and a `Permission` navigation property.
4. THE PolicyPermission entity SHALL have a unique composite constraint on (`PolicyId`, `PermissionId`) to prevent duplicate assignments.
5. THE PolicyPermission entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 10: UserRole Junction Entity

**User Story:** As a framework consumer, I want a junction entity linking users to roles within a specific tenant context, so that role assignments are tenant-scoped and a user can have different roles in different tenants.

#### Acceptance Criteria

1. THE UserRole entity SHALL extend `BaseEntity` and implement `IAuditable`, and reside in `src/GroundUp.Auth.Core/Entities/UserRole.cs`.
2. THE UserRole entity SHALL have a `UserId` property of type `Guid` as a required foreign key to `User`, and a `User` navigation property.
3. THE UserRole entity SHALL have a `RoleId` property of type `Guid` as a required foreign key to `Role`, and a `Role` navigation property.
4. THE UserRole entity SHALL have a `TenantId` property of type `Guid` as a required foreign key to `Tenant`, implementing `ITenantEntity`, and a `Tenant` navigation property.
5. THE UserRole entity SHALL have a unique composite constraint on (`UserId`, `RoleId`, `TenantId`) to prevent duplicate role assignments within the same tenant.
6. THE UserRole entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 11: TenantType Enum

**User Story:** As a framework consumer, I want an enum distinguishing standard tenants from enterprise tenants, so that the system can apply different behavior (e.g., SSO federation) based on tenant type.

#### Acceptance Criteria

1. THE TenantType enum SHALL reside in `src/GroundUp.Auth.Core/Enums/TenantType.cs`.
2. THE TenantType enum SHALL define the following members: `Standard = 0`, `Enterprise = 1`.
3. THE TenantType enum SHALL have XML doc comments on the enum type and each member describing its purpose.

### Requirement 12: OnboardingMode Enum

**User Story:** As a framework consumer, I want an enum defining how users join a tenant, so that each tenant can control its onboarding flow independently.

#### Acceptance Criteria

1. THE OnboardingMode enum SHALL reside in `src/GroundUp.Auth.Core/Enums/OnboardingMode.cs`.
2. THE OnboardingMode enum SHALL define the following members: `InviteOnly = 0`, `JoinLink = 1`, `Open = 2`.
3. THE OnboardingMode enum SHALL have XML doc comments on the enum type and each member describing its purpose.

### Requirement 13: RoleType Enum

**User Story:** As a framework consumer, I want an enum categorizing roles by their origin and scope, so that the system can distinguish framework-defined roles from application-defined and user-created roles.

#### Acceptance Criteria

1. THE RoleType enum SHALL reside in `src/GroundUp.Auth.Core/Enums/RoleType.cs`.
2. THE RoleType enum SHALL define the following members: `System = 0`, `Application = 1`, `Workspace = 2`.
3. THE RoleType enum SHALL have XML doc comments on the enum type and each member describing its purpose.

### Requirement 14: RequiresPermissionAttribute

**User Story:** As a framework consumer, I want a custom attribute that I can place on service interface methods to declare required permissions, so that the permission enforcement decorator can check authorization without the service implementation knowing about security.

#### Acceptance Criteria

1. THE RequiresPermissionAttribute SHALL reside in `src/GroundUp.Core/Attributes/RequiresPermissionAttribute.cs` so it is available without depending on the auth module.
2. THE RequiresPermissionAttribute SHALL accept one or more permission keys as constructor parameters (params string array).
3. THE RequiresPermissionAttribute SHALL expose a `Permissions` property of type `IReadOnlyList<string>` containing the required permission keys.
4. THE RequiresPermissionAttribute SHALL be decorated with `[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]`.
5. THE RequiresPermissionAttribute SHALL be a sealed class with XML doc comments on the class, constructor, and properties.

### Requirement 15: RequiresRoleAttribute

**User Story:** As a framework consumer, I want a custom attribute that I can place on service interface methods to declare required roles, so that the permission enforcement decorator can check role membership as an alternative to granular permission checks.

#### Acceptance Criteria

1. THE RequiresRoleAttribute SHALL reside in `src/GroundUp.Core/Attributes/RequiresRoleAttribute.cs` so it is available without depending on the auth module.
2. THE RequiresRoleAttribute SHALL accept one or more role names as constructor parameters (params string array).
3. THE RequiresRoleAttribute SHALL expose a `Roles` property of type `IReadOnlyList<string>` containing the required role names.
4. THE RequiresRoleAttribute SHALL be decorated with `[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]`.
5. THE RequiresRoleAttribute SHALL be a sealed class with XML doc comments on the class, constructor, and properties.

### Requirement 16: Auth Read DTOs

**User Story:** As a framework consumer, I want DTO records for all auth entities, so that the service and API layers can transfer auth data without exposing EF Core entity internals.

#### Acceptance Criteria

1. THE UserDto SHALL be a record in `src/GroundUp.Auth.Core/Dtos/UserDto.cs` with properties: `Id` (Guid), `ExternalUserId` (string), `Email` (string), `DisplayName` (string), `IsActive` (bool).
2. THE TenantDto SHALL be a record in `src/GroundUp.Auth.Core/Dtos/TenantDto.cs` with properties: `Id` (Guid), `Name` (string), `Slug` (string), `TenantType` (TenantType), `OnboardingMode` (OnboardingMode), `ParentTenantId` (Guid?), `RealmName` (string?), `CustomDomain` (string?), `IsActive` (bool).
3. THE RoleDto SHALL be a record in `src/GroundUp.Auth.Core/Dtos/RoleDto.cs` with properties: `Id` (Guid), `Name` (string), `Description` (string?), `RoleType` (RoleType), `TenantId` (Guid), `IsSystem` (bool).
4. THE PolicyDto SHALL be a record in `src/GroundUp.Auth.Core/Dtos/PolicyDto.cs` with properties: `Id` (Guid), `Name` (string), `Description` (string?), `TenantId` (Guid).
5. THE PermissionDto SHALL be a record in `src/GroundUp.Auth.Core/Dtos/PermissionDto.cs` with properties: `Id` (Guid), `Key` (string), `Name` (string), `Description` (string?), `Module` (string).
6. ALL auth read DTO records SHALL have XML doc comments on the record type and each property parameter.

### Requirement 17: Auth Create/Update DTOs

**User Story:** As a framework consumer, I want create and update DTO records for tenants and roles, so that the API layer has well-defined input contracts for mutation operations.

#### Acceptance Criteria

1. THE CreateTenantDto SHALL be a record in `src/GroundUp.Auth.Core/Dtos/CreateTenantDto.cs` with properties: `Name` (string), `Slug` (string), `TenantType` (TenantType), `OnboardingMode` (OnboardingMode), `ParentTenantId` (Guid?), `RealmName` (string?), `CustomDomain` (string?).
2. THE UpdateTenantDto SHALL be a record in `src/GroundUp.Auth.Core/Dtos/UpdateTenantDto.cs` with properties: `Name` (string), `Slug` (string), `TenantType` (TenantType), `OnboardingMode` (OnboardingMode), `RealmName` (string?), `CustomDomain` (string?), `IsActive` (bool).
3. THE CreateRoleDto SHALL be a record in `src/GroundUp.Auth.Core/Dtos/CreateRoleDto.cs` with properties: `Name` (string), `Description` (string?), `RoleType` (RoleType).
4. THE UpdateRoleDto SHALL be a record in `src/GroundUp.Auth.Core/Dtos/UpdateRoleDto.cs` with properties: `Name` (string), `Description` (string?).
5. THE CreatePolicyDto SHALL be a record in `src/GroundUp.Auth.Core/Dtos/CreatePolicyDto.cs` with properties: `Name` (string), `Description` (string?).
6. THE CreatePermissionDto SHALL be a record in `src/GroundUp.Auth.Core/Dtos/CreatePermissionDto.cs` with properties: `Key` (string), `Name` (string), `Description` (string?), `Module` (string).
7. ALL auth create/update DTO records SHALL have XML doc comments on the record type and each property parameter.

### Requirement 18: FluentValidation Validators for Auth DTOs

**User Story:** As a framework consumer, I want FluentValidation validators for all auth create/update DTOs, so that input validation is consistent, testable, and runs in the service layer before any repository calls.

#### Acceptance Criteria

1. THE CreateTenantDtoValidator SHALL reside in `src/GroundUp.Auth.Core/Validators/CreateTenantDtoValidator.cs` and validate: Name (not empty, max 200), Slug (not empty, max 100, matches slug pattern), TenantType (valid enum), OnboardingMode (valid enum), RealmName (max 200 when provided), CustomDomain (max 500 when provided).
2. THE UpdateTenantDtoValidator SHALL reside in `src/GroundUp.Auth.Core/Validators/UpdateTenantDtoValidator.cs` and validate the same constraints as CreateTenantDtoValidator for the corresponding properties.
3. THE CreateRoleDtoValidator SHALL reside in `src/GroundUp.Auth.Core/Validators/CreateRoleDtoValidator.cs` and validate: Name (not empty, max 200), Description (max 1000 when provided), RoleType (valid enum).
4. THE UpdateRoleDtoValidator SHALL reside in `src/GroundUp.Auth.Core/Validators/UpdateRoleDtoValidator.cs` and validate: Name (not empty, max 200), Description (max 1000 when provided).
5. THE CreatePolicyDtoValidator SHALL reside in `src/GroundUp.Auth.Core/Validators/CreatePolicyDtoValidator.cs` and validate: Name (not empty, max 200), Description (max 1000 when provided).
6. THE CreatePermissionDtoValidator SHALL reside in `src/GroundUp.Auth.Core/Validators/CreatePermissionDtoValidator.cs` and validate: Key (not empty, max 200, matches permission key pattern), Name (not empty, max 200), Description (max 1000 when provided), Module (not empty, max 100).
7. ALL validators SHALL be sealed classes with XML doc comments.

### Requirement 19: Solution Compilation

**User Story:** As a framework developer, I want the entire solution to compile with zero errors after adding the new GroundUp.Auth.Core project and all auth entities, enums, DTOs, validators, and attributes, so that the data layer is ready for EF configurations in Phase 9B.

#### Acceptance Criteria

1. WHEN `dotnet build groundup.sln` is executed, THE build SHALL complete with zero errors for both `GroundUp.Core` and `GroundUp.Auth.Core` projects.
2. THE auth entities SHALL follow the existing project conventions: file-scoped namespaces, sealed classes, XML doc comments on all public types and members, one class per file.
3. THE auth entities SHALL reside in `src/GroundUp.Auth.Core/Entities/`.
4. THE auth DTOs SHALL reside in `src/GroundUp.Auth.Core/Dtos/`.
5. THE auth enums SHALL reside in `src/GroundUp.Auth.Core/Enums/`.
6. THE auth validators SHALL reside in `src/GroundUp.Auth.Core/Validators/`.
7. THE security attributes SHALL reside in `src/GroundUp.Core/Attributes/`.
