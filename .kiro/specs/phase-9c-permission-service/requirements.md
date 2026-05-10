# Requirements Document — Phase 9C: Permission Service & Enforcement

## Introduction

Phase 9C builds the permission service layer and authorization enforcement mechanism for the GroundUp framework. This phase introduces the `GroundUp.Auth.Services` project containing the permission resolution engine, DispatchProxy-based authorization enforcement, JWT-based ICurrentUser/ITenantContext implementations, and the `AddGroundUpAuth()` DI registration extension. Together these components form the security boundary at the service layer — controllers remain free of security logic, and authorization is enforced transparently via proxy interception on service interface methods decorated with `[RequiresPermission]` or `[RequiresRole]` attributes.

## Glossary

- **Permission_Service**: The service responsible for resolving a user's effective permissions within a tenant by walking the UserRole → Role → RolePolicy → Policy → PolicyPermission → Permission hierarchy.
- **Authorization_Proxy**: A `System.Reflection.DispatchProxy`-based interceptor that wraps service interface implementations to enforce `[RequiresPermission]` and `[RequiresRole]` attributes before method execution.
- **Permission_Cache**: An `IMemoryCache`-based cache storing the resolved set of permission keys per user+tenant combination, keyed as `permissions:{userId}:{tenantId}`.
- **Cache_Invalidation_Handler**: An `IEventHandler<T>` implementation that evicts relevant Permission_Cache entries when roles, policies, or permissions are modified.
- **JwtCurrentUser**: The `ICurrentUser` implementation that extracts UserId, Email, and DisplayName from JWT claims via `IHttpContextAccessor`.
- **JwtTenantContext**: The `ITenantContext` implementation that extracts TenantId from JWT claims via `IHttpContextAccessor`.
- **SystemCurrentUser**: A manually-constructed `ICurrentUser` implementation for non-HTTP scenarios (background jobs, SDK usage) where no HttpContext is available.
- **SystemTenantContext**: A manually-constructed `ITenantContext` implementation for non-HTTP scenarios.
- **AuthOptions**: A configuration class holding auth module settings (cache TTL, claim type mappings).
- **AddGroundUpAuth**: The `IServiceCollection` extension method that registers all auth service layer components.
- **AddAuthorized**: The `IServiceCollection` extension method `AddAuthorized<TInterface, TImplementation>()` that registers a service with the Authorization_Proxy wrapper.

## Requirements

### Requirement 1: Permission Resolution

**User Story:** As a framework consumer, I want to resolve a user's effective permissions within a tenant, so that authorization decisions can be made against the full permission set.

#### Acceptance Criteria

1. WHEN `HasPermissionAsync(userId, permissionKey)` is called, THE Permission_Service SHALL return true if the user holds the specified permission within the current tenant, and false otherwise.
2. WHEN `HasAnyPermissionAsync(userId, permissionKeys)` is called, THE Permission_Service SHALL return true if the user holds at least one of the specified permissions within the current tenant, and false otherwise.
3. WHEN `GetUserPermissionsAsync(userId)` is called, THE Permission_Service SHALL return the complete set of permission keys the user holds within the current tenant.
4. THE Permission_Service SHALL resolve permissions by traversing: UserRoles (for userId in current tenant) → Roles → RolePolicies → Policies → PolicyPermissions → Permissions.
5. THE Permission_Service SHALL ALSO include permissions from the user's system-level roles (RoleType.System) regardless of which tenant is current. System roles transcend tenant boundaries.
6. WHEN a user has multiple roles (tenant-scoped and system) that grant overlapping permissions, THE Permission_Service SHALL return a deduplicated set of permission keys.
7. WHEN a user has no role assignments in the current tenant AND no system role assignments, THE Permission_Service SHALL return an empty permission set.
8. IF the userId does not correspond to a valid user-tenant membership, THEN THE Permission_Service SHALL still resolve system-level role permissions if the user has system role assignments.

### Requirement 2: Permission Caching

**User Story:** As a framework consumer, I want permission lookups to be cached, so that repeated authorization checks do not incur database round-trips on every service call.

#### Acceptance Criteria

1. THE Permission_Service SHALL cache the resolved permission set in IMemoryCache using the key format `permissions:{userId}:{tenantId}`.
2. THE Permission_Service SHALL use the cached permission set for subsequent calls to HasPermissionAsync, HasAnyPermissionAsync, and GetUserPermissionsAsync for the same userId and tenantId.
3. THE Permission_Service SHALL use a configurable cache TTL with a default value of 15 minutes.
4. THE Permission_Service SHALL read the cache TTL from `AuthOptions.PermissionCacheTtlMinutes` provided via `IOptions<AuthOptions>`.
5. WHEN the cache entry for a user+tenant combination does not exist, THE Permission_Service SHALL resolve permissions from the database and populate the cache.

### Requirement 3: Cache Invalidation

**User Story:** As a framework consumer, I want the permission cache to be invalidated when authorization data changes, so that permission checks reflect the current state.

#### Acceptance Criteria

1. WHEN a role assignment is created or removed (EntityCreatedEvent or EntityDeletedEvent for UserRole), THE Cache_Invalidation_Handler SHALL evict the cache entry for the affected userId and tenantId.
2. WHEN a role-policy assignment is created or removed (EntityCreatedEvent or EntityDeletedEvent for RolePolicy), THE Cache_Invalidation_Handler SHALL evict cache entries for all users who hold that role in the current tenant.
3. WHEN a policy-permission assignment is created or removed (EntityCreatedEvent or EntityDeletedEvent for PolicyPermission), THE Cache_Invalidation_Handler SHALL evict cache entries for all users whose roles include that policy in the current tenant.
4. WHEN a role is updated or deleted, THE Cache_Invalidation_Handler SHALL evict cache entries for all users who hold that role in the current tenant.
5. THE Cache_Invalidation_Handler SHALL subscribe to events via `IEventHandler<T>` registrations in the DI container.

### Requirement 4: Authorization Proxy — Permission Enforcement

**User Story:** As a framework consumer, I want service methods decorated with `[RequiresPermission]` to be automatically checked before execution, so that unauthorized calls are blocked without manual checks in service code.

#### Acceptance Criteria

1. WHEN a method decorated with `[RequiresPermission("perm1", "perm2")]` is invoked through the Authorization_Proxy, THE Authorization_Proxy SHALL verify that the current user holds ALL listed permissions (AND semantics).
2. IF the current user does not hold all required permissions, THEN THE Authorization_Proxy SHALL return `OperationResult<T>.Forbidden()` without invoking the underlying service method.
3. WHEN the current user holds all required permissions, THE Authorization_Proxy SHALL invoke the underlying service method and return its result.
4. THE Authorization_Proxy SHALL read the `[RequiresPermission]` attribute from the interface method definition, not the implementation class.
5. THE Authorization_Proxy SHALL use `System.Reflection.DispatchProxy` as its interception mechanism.
6. WHEN a method has no `[RequiresPermission]` or `[RequiresRole]` attribute, THE Authorization_Proxy SHALL invoke the underlying service method without any authorization check.

### Requirement 5: Authorization Proxy — Role Enforcement

**User Story:** As a framework consumer, I want service methods decorated with `[RequiresRole]` to be automatically checked before execution, so that system-level role-based access control is enforced transparently.

#### Acceptance Criteria

1. WHEN a method decorated with `[RequiresRole("Admin", "Manager")]` is invoked through the Authorization_Proxy, THE Authorization_Proxy SHALL verify that the current user holds at least one of the listed roles (OR semantics).
2. IF the current user does not hold any of the required roles, THEN THE Authorization_Proxy SHALL return `OperationResult<T>.Forbidden()` without invoking the underlying service method.
3. WHEN the current user holds at least one required role, THE Authorization_Proxy SHALL invoke the underlying service method and return its result.
4. THE Authorization_Proxy SHALL read the `[RequiresRole]` attribute from the interface method definition, not the implementation class.
5. THE Authorization_Proxy SHALL resolve the user's roles by querying for system-level roles (RoleType.System) that bypass tenant filtering. `[RequiresRole]` is only meaningful for system roles because dynamic tenant-scoped roles are not known at development time.
6. THE role name comparison SHALL be case-insensitive.

### Requirement 6: Authorization Proxy — Return Type Handling

**User Story:** As a framework consumer, I want the authorization proxy to correctly handle all OperationResult return types, so that Forbidden results are properly typed.

#### Acceptance Criteria

1. WHEN a method returns `Task<OperationResult<T>>` and authorization fails, THE Authorization_Proxy SHALL return `Task.FromResult(OperationResult<T>.Forbidden())` with the correct generic type parameter.
2. WHEN a method returns `Task<OperationResult>` (non-generic) and authorization fails, THE Authorization_Proxy SHALL return `Task.FromResult(OperationResult.Forbidden())`.
3. IF a method has a return type that is not `Task<OperationResult<T>>` or `Task<OperationResult>`, THEN THE Authorization_Proxy SHALL invoke the method without authorization checks.

### Requirement 7: AddAuthorized DI Extension

**User Story:** As a framework consumer, I want a simple DI registration method to wrap my services with authorization enforcement, so that I can opt-in to permission checking per service.

#### Acceptance Criteria

1. WHEN `services.AddAuthorized<TInterface, TImplementation>()` is called, THE extension method SHALL register TImplementation as the concrete service and wrap it with the Authorization_Proxy that implements TInterface.
2. THE AddAuthorized extension SHALL register the service with Scoped lifetime.
3. THE AddAuthorized extension SHALL support any interface where methods return `Task<OperationResult<T>>` or `Task<OperationResult>`.
4. WHEN a service is registered without AddAuthorized (using standard AddScoped), THE service SHALL function without any authorization proxy interception.

### Requirement 8: JwtCurrentUser Implementation

**User Story:** As a framework consumer, I want ICurrentUser to be populated from JWT claims in HTTP scenarios, so that the authenticated user's identity is available throughout the service layer.

#### Acceptance Criteria

1. THE JwtCurrentUser SHALL extract UserId from the JWT claim identified by `AuthOptions.UserIdClaimType` (default: `"sub"`).
2. THE JwtCurrentUser SHALL extract Email from the JWT claim identified by `AuthOptions.EmailClaimType` (default: `"email"`).
3. THE JwtCurrentUser SHALL extract DisplayName from the JWT claim identified by `AuthOptions.DisplayNameClaimType` (default: `"name"`).
4. THE JwtCurrentUser SHALL read claims from `IHttpContextAccessor.HttpContext.User`.
5. IF no HttpContext is available or no authenticated user is present, THEN THE JwtCurrentUser SHALL return `Guid.Empty` for UserId and null for Email and DisplayName.
6. THE JwtCurrentUser SHALL parse the UserId claim value as a Guid.

### Requirement 9: JwtTenantContext Implementation

**User Story:** As a framework consumer, I want ITenantContext to be populated from JWT claims in HTTP scenarios, so that tenant scoping is automatic for authenticated requests.

#### Acceptance Criteria

1. THE JwtTenantContext SHALL extract TenantId from the JWT claim identified by `AuthOptions.TenantIdClaimType` (default: `"tenant_id"`).
2. THE JwtTenantContext SHALL read claims from `IHttpContextAccessor.HttpContext.User`.
3. IF no HttpContext is available or no tenant claim is present, THEN THE JwtTenantContext SHALL return `Guid.Empty` for TenantId.
4. THE JwtTenantContext SHALL parse the TenantId claim value as a Guid.

### Requirement 10: Non-HTTP Identity Support

**User Story:** As a framework consumer, I want ICurrentUser and ITenantContext to work in non-HTTP scenarios (SDK, background jobs), so that the auth module supports all hosting models.

#### Acceptance Criteria

1. THE SystemCurrentUser SHALL accept UserId, Email, and DisplayName as constructor parameters and expose them via the ICurrentUser interface.
2. THE SystemTenantContext SHALL accept TenantId as a constructor parameter and expose it via the ITenantContext interface.
3. WHEN running in a non-HTTP scenario, THE consuming application SHALL register SystemCurrentUser and SystemTenantContext as scoped services with the appropriate values.
4. THE Authorization_Proxy SHALL function identically regardless of whether ICurrentUser is backed by JwtCurrentUser or SystemCurrentUser.

### Requirement 11: AuthOptions Configuration

**User Story:** As a framework consumer, I want to configure auth module behavior via options, so that I can customize claim mappings and cache settings.

#### Acceptance Criteria

1. THE AuthOptions class SHALL expose `PermissionCacheTtlMinutes` (int, default 15).
2. THE AuthOptions class SHALL expose `UserIdClaimType` (string, default "sub").
3. THE AuthOptions class SHALL expose `EmailClaimType` (string, default "email").
4. THE AuthOptions class SHALL expose `DisplayNameClaimType` (string, default "name").
5. THE AuthOptions class SHALL expose `TenantIdClaimType` (string, default "tenant_id").
6. THE AddGroundUpAuth extension SHALL bind AuthOptions from the `IConfiguration` section named "GroundUp:Auth".

### Requirement 12: AddGroundUpAuth DI Registration

**User Story:** As a framework consumer, I want a single extension method to register all auth service layer components, so that setup is simple and consistent with other GroundUp modules.

#### Acceptance Criteria

1. WHEN `services.AddGroundUpAuth()` is called, THE extension method SHALL register IPermissionService as a scoped service.
2. WHEN `services.AddGroundUpAuth()` is called, THE extension method SHALL register JwtCurrentUser as the scoped ICurrentUser implementation.
3. WHEN `services.AddGroundUpAuth()` is called, THE extension method SHALL register JwtTenantContext as the scoped ITenantContext implementation.
4. WHEN `services.AddGroundUpAuth()` is called, THE extension method SHALL register all Cache_Invalidation_Handler implementations as IEventHandler<T> in the DI container.
5. WHEN `services.AddGroundUpAuth()` is called, THE extension method SHALL configure AuthOptions from the configuration section "GroundUp:Auth".
6. WHEN `services.AddGroundUpAuth(Action<AuthOptions> configure)` is called with an explicit configuration action, THE extension method SHALL apply the configuration action to AuthOptions.
7. THE AddGroundUpAuth extension SHALL add IMemoryCache to the service collection if not already registered.
8. THE AddGroundUpAuth extension SHALL add IHttpContextAccessor to the service collection if not already registered.

### Requirement 13: Sample App Integration

**User Story:** As a framework developer, I want the Sample app to demonstrate auth enforcement, so that consumers have a working reference implementation.

#### Acceptance Criteria

1. THE Sample app SHALL reference GroundUp.Auth.Data.Postgres and GroundUp.Auth.Services projects.
2. THE Sample app SHALL call `AddGroundUpAuthPostgres(connectionString)` and `AddGroundUpAuth()` in Program.cs.
3. THE Sample app SHALL include at least one service method decorated with `[RequiresPermission]` registered via `AddAuthorized<TInterface, TImplementation>()`.
4. THE Sample app SHALL include a migration that applies the auth schema to the local Postgres database.

### Requirement 14: GroundUp.Auth.Services Project Structure

**User Story:** As a framework developer, I want the Auth.Services project to follow established framework conventions, so that it integrates cleanly with the existing module system.

#### Acceptance Criteria

1. THE GroundUp.Auth.Services project SHALL target net8.0 with nullable reference types enabled.
2. THE GroundUp.Auth.Services project SHALL reference GroundUp.Core, GroundUp.Auth.Core, GroundUp.Auth.Data.Abstractions, GroundUp.Services, and GroundUp.Events.
3. THE GroundUp.Auth.Services project SHALL reference Microsoft.Extensions.Caching.Memory and Microsoft.AspNetCore.Http.Abstractions NuGet packages.
4. THE GroundUp.Auth.Services project SHALL use file-scoped namespaces, sealed classes, and XML doc comments on all public members.
5. THE GroundUp.Auth.Services project SHALL follow one class per file organization.

### Requirement 15: System Role Repository Extension

**User Story:** As a framework developer, I want to query a user's system-level role assignments without tenant filtering, so that the permission service and authorization proxy can resolve system roles that transcend tenant boundaries.

#### Acceptance Criteria

1. THE IUserRoleRepository SHALL expose a `GetSystemRolesForUserAsync(Guid userId)` method that returns all UserRole records where the associated Role has RoleType == System, regardless of the current tenant context.
2. THE `GetSystemRolesForUserAsync` method SHALL bypass the BaseTenantRepository tenant filter (similar to how `GetAllMembershipsForUserAsync` bypasses it in IUserTenantRepository).
3. THE method SHALL return `OperationResult<List<UserRoleDto>>`.
4. THE method SHALL include the Role name in the result so that role name comparisons can be performed without additional lookups.
