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

- **Library:** `TngTech.ArchUnitNET.xUnit` (Apache 2.0, maintained by TNG). It reads the compiled IL, so dependencies inside method bodies (for example `GetRequiredService<IUserTenantRepository>()`) are found. Version pinned when added.
- **Location:** `tests/GroundUp.Tests.Unit/Architecture/LayerRulesTests.cs`. Loads every `GroundUp.*` assembly that the unit test project references.
- **Rules:** as Requirement 3.1. "Repository interface" = interface whose name ends in `Repository` in a `*.Data.Abstractions` assembly.
- **Allowlist** (`LayerRulesAllowlist`): the 7 `GroundUp.Services` types for GU-82, each with a comment. A test also fails if an allowlisted type no longer violates any rule, so GU-82 must shrink the list.
- If ArchUnitNET cannot express a rule cleanly, the fallback is a small Mono.Cecil scan written in the test project (same rules, same allowlist).

## 5. Risks

| Risk | Handling |
|---|---|
| Changing the `me/tenants` JSON by accident | Controller tests assert the exact property names for both shapes; existing integration tests stay green |
| First-admin provisioning regresses (no real test today) | New Postgres integration test (Requirement 4.2) before and after the move; run first against the current code to prove it covers today's behavior |
| Removing the project reference breaks a consumer | The sample and tests reference `Auth.Data.Postgres` directly; build the whole solution |
| ArchUnitNET false positives (for example generated code) | Restrict to `GroundUp.*` namespaces; ignore compiler-generated types |

## 6. Task groups

1. **Architecture tests first.** Add ArchUnitNET, the rules and an allowlist that also lists today's auth violations (so the suite is green), and the integration test for first-admin provisioning against the current code. Commit. This proves the tests catch today's state before anything moves.
2. **Controller and middleware.** Move the four operations (section 1), the validator, `MyTenantDto`; thin controller and middleware; remove the auth entries from the allowlist. Unit tests.
3. **Services and data.** `IIdentityBootstrapRepository`, `IUniqueConstraintDetector`, service rewrite, `NewOrganizationFlowHandler` cleanup, remove the project reference and the last auth allowlist entries. The integration test from group 1 must pass unchanged.
4. **Docs and Jira.** CLAUDE.md and steering mention the architecture tests; ARCHITECTURE.md layer section; GU-82 references the allowlist.
