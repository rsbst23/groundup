# Design: Auth Layering (GU-81)

Implements `requirements.md` in this folder.

## 1. Moved operations

| From | To | New or changed member |
|---|---|---|
| `AuthController.SetTenant` (external-id lookup) | `IAuthSessionService` | `SetTenantForExternalUserAsync(string externalUserId, Guid? tenantId)` resolves the user via `IUserRepository.GetByExternalUserIdAsync` (404 `User not found.`), then calls the existing `SetTenantAsync(userId, tenantId, originalAuthTime: null)` |
| `AuthController.GetMyTenants` | `IMembershipService` | `GetMyTenantsAsync()` → `OperationResult<IReadOnlyList<MyTenantDto>>`, using `ICurrentUser` and `ITenantContext` |
| `HostTokenReconciliationMiddleware` membership check | `IMembershipService` | `IsActiveMemberAsync(Guid userId, Guid tenantId)` |
| `AuthController.CreateOrganization` (user load, name checks) | `IOrganizationProvisioningService` | `CreateForCurrentUserAsync(CreateOrganizationRequestDto)` loads the current user (401 when missing or inactive), validates with `CreateOrganizationRequestDtoValidator`, then calls the existing `ProvisionAsync` |

- **`MyTenantDto`** (Auth.Core): `Id`, `Name`, `Slug`, `Type` ("standard" or "enterprise"), `Switchable`, `Current` (null for enterprise), `Subdomain` (null for standard). The controller maps it to today's anonymous JSON shapes, so the response is byte-for-byte the same (standard: `id, name, slug, type, switchable, current`; enterprise: `id, name, slug, type, switchable, subdomain`).
- **`GetMyTenantsAsync` failures** keep today's behavior: if memberships or tenants cannot be read, the list is empty (200 `[]`).
- **Current tenant** comes from `ITenantContext.TenantId` (hydrated from `tid` by `JwtTenantResolutionMiddleware`), the same value the controller read from the claim.
- **Validator** messages: "Organization name is required." and "Organization name must not exceed 200 characters." The service trims before validating and passes the trimmed name on, as today.
- The controller keeps only HTTP work: authentication checks, rejecting an identity-provider principal where it does today, reading `auth_time`, mapping results and writing cookies.

## 2. First-admin provisioning without `AuthDbContext`

New repository interface in `GroundUp.Auth.Data.Abstractions`:

```csharp
public interface IIdentityBootstrapRepository
{
    Task<Guid?> FindSystemRoleIdByNameAsync(string roleName, CancellationToken ct = default);
    Task<UserDto?> FindUserByEmailAsync(string email, CancellationToken ct = default);
    Task<OperationResult<Guid>> EnsureSuperAdminAsync(SuperAdminRecord record, CancellationToken ct = default);
    Task<bool> AnyUserHasRoleAsync(string roleName, CancellationToken ct = default);
    Task<Guid?> GetFirstUserIdWithRoleAsync(string roleName, CancellationToken ct = default);
    Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> RoleExistsAsync(string roleName, CancellationToken ct = default);
}
```

- **Why a dedicated repository.** These are system-level reads and writes that deliberately bypass tenant filtering (the System tenant, a role looked up by name across tenants). The existing tenant-scoped repositories would need bypass variants of several methods; one small repository keeps the bypass in one documented place, the same way `UserTenantRepository` documents its "SYSTEM BYPASS" methods.
- **`EnsureSuperAdminAsync`** runs, in one transaction: existence check then insert for the user (by email), the membership (user, tenant), and the user-role (user, role). It returns the user id. A unique-constraint violation returns `OperationResult.Fail(..., 409, ErrorCodes.Conflict)`.
- **Where it lives.** Implementation `IdentityBootstrapRepository` in `GroundUp.Auth.Repositories` (uses `DbContext` like every repository). The transaction uses `Database.BeginTransactionAsync` on the context, which is provider-neutral EF Core.
- **Unique violations.** The repository catches `DbUpdateException` and asks an `IUniqueConstraintDetector` (new, `GroundUp.Data.Abstractions`) whether it is a unique violation. `GroundUp.Data.Postgres` registers `PostgresUniqueConstraintDetector`, a wrapper over the existing `PostgresErrorHelper.IsUniqueConstraintViolation`. Registration in `AddGroundUpAuthPostgres` (TryAdd, so the core Postgres registration can share it). `AuthDbContextUnitOfWork` keeps its private copy for now (it is already in Data.Postgres).
- **Service.** `IdentityBootstrapService` keeps every decision: missing SuperAdmin role → 500, existing email with same display name → idempotent success, different display name → 409, then `EnsureSuperAdminAsync`. The other four methods call the repository. Logging stays in the service.
- `GroundUp.Auth.Services.csproj` drops the `GroundUp.Auth.Data.Postgres` reference. `AddGroundUpAuthPostgres` registers `IIdentityBootstrapRepository`.

## 3. `NewOrganizationFlowHandler`

Remove the `catch (Exception) when (IsUniqueConstraintViolation(ex))` block and the method. `AddAsync` returns a failed result on a conflict, and the code already falls through to the re-read. Behavior unchanged; a unit test covers "add returns 409 → re-read returns the user".

## 4. Architecture tests

- **No package (decided).** The compiler writes an assembly reference only for assemblies whose types the code uses, including inside method bodies, so `Assembly.GetReferencedAssemblies()` sees the middleware's `GetRequiredService<IUserTenantRepository>()`. Verified on today's build: `GroundUp.Auth.Api` lists `GroundUp.Auth.Data.Abstractions` and `GroundUp.Data.Abstractions` (only `AuthController` and `HostTokenReconciliationMiddleware` use them); `GroundUp.Auth.Services` lists `GroundUp.Auth.Data.Postgres`, `Microsoft.EntityFrameworkCore` and `Npgsql` (only `IdentityBootstrapService`); `GroundUp.Services` lists `GroundUp.Data.Postgres` and EF Core (GU-82).
- **Trade-off:** a failure names the assembly, not the class. The message says which assembly uses which; the developer searches for the type.
- **Location:** `tests/GroundUp.Tests.Unit/Architecture/LayerRulesTests.cs`. Assemblies are loaded through a type from each (`typeof(AuthController).Assembly` and so on), so every `GroundUp.*` production assembly the unit tests reference is covered. A test fails if a `GroundUp.*` assembly in `src` is missing from that list, so a new module is not silently skipped.
- **Rules:** as Requirement 3.1, one `[Theory]` row per (layer, forbidden pattern).
- **Allowlist** (`LayerRulesAllowlist`): (assembly, used assembly) pairs. After this story only GU-82's remain: `GroundUp.Services` → `GroundUp.Data.Postgres`, `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`. A separate test fails when an allowlisted pair no longer occurs.

## 5. Risks

| Risk | Handling |
|---|---|
| Changing the `me/tenants` JSON by accident | Controller tests assert the exact property names for both shapes; existing integration tests stay green |
| First-admin provisioning regresses (no real test today) | New Postgres integration test (Requirement 4.2) before and after the move; run first against the current code to prove it covers today's behavior |
| Removing the project reference breaks a consumer | The sample and tests reference `Auth.Data.Postgres` directly; build the whole solution |
| An assembly reference without a real dependency (for example only a type forwarded through an attribute) | Not seen today; if it happens, the allowlist entry names it with a reason |

## 6. Task groups

1. **Architecture tests first.** The rules and an allowlist that also lists today's auth violations (so the suite is green), and the integration test for first-admin provisioning against the current code. Commit. This proves the tests catch today's state before anything moves.
2. **Controller and middleware.** Move the four operations (section 1), the validator, `MyTenantDto`; thin controller and middleware; remove the auth entries from the allowlist. Unit tests.
3. **Services and data.** `IIdentityBootstrapRepository`, `IUniqueConstraintDetector`, service rewrite, `NewOrganizationFlowHandler` cleanup, remove the project reference and the last auth allowlist entries. The integration test from group 1 must pass unchanged.
4. **Docs and Jira.** CLAUDE.md and steering mention the architecture tests; ARCHITECTURE.md layer section; GU-82 references the allowlist.
