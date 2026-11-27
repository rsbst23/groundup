# Design: Invitations, Join Links and Role Assignment (GU-68)

Requirements: `requirements.md` in this folder. Paths are relative to `src/`.

## 1. Data model and migration (PR A)

One migration in `GroundUp.Auth.Data.Postgres`: `AddPendingMembersAndRoleSource`.

### 1.1 Enums (`GroundUp.Auth.Core/Enums`)

```csharp
public enum MembershipStatus { Active = 0, Inactive = 1, Pending = 2 }
public enum RoleAssignmentSource { Manual = 0, Invitation = 1, JoinLink = 2, DefaultRole = 3, Provisioning = 4 }
```
`Active = 0` keeps `default(MembershipStatus)` equal to today's `IsActive = true` default. GU-69 appends `IdpGroup = 5`.

### 1.2 Entity changes

| Entity | Change |
|---|---|
| `User` | `ExternalUserId` becomes `string?`. New `Guid? MergedIntoUserId`. |
| `UserTenant` | `ExternalUserId` becomes `string?`. `bool IsActive` is replaced by `MembershipStatus Status`. |
| `UserRole` | New `RoleAssignmentSource Source`. |

DTOs:
- `UserDto`: `string? ExternalUserId`, trailing `Guid? MergedIntoUserId = null`.
- `UserTenantDto`: the `bool IsActive` parameter becomes `MembershipStatus Status`. A computed `bool IsActive => Status == MembershipStatus.Active` keeps the existing read sites (`.Where(m => m.IsActive)`) compiling and correct. Construction sites (Login, SimpleSignup, provisioning, bootstrap) change to `Status: MembershipStatus.Active`.
- `UserRoleDto`: trailing `RoleAssignmentSource Source = RoleAssignmentSource.Manual`.

### 1.3 Configuration and indexes

- `AuthUsers.ExternalUserId`: nullable. The unique index stays, filtered `"ExternalUserId" IS NOT NULL` (explicit, although Postgres already treats NULLs as distinct). New non-unique index on `lower("Email")` for the pending-user lookups.
- `AuthUserTenants.Status`: int, default 0. `ExternalUserId` nullable.
- `AuthUserRoles.Source`: int, default 0. The unique index changes from (UserId, RoleId, TenantId) to (UserId, RoleId, TenantId, Source).

### 1.4 Data migration (hand-written SQL in `Up`)

1. Add `Status`, then `UPDATE "AuthUserTenants" SET "Status" = 1 WHERE NOT "IsActive"`, then drop `IsActive`.
2. Add `Source`. Set `Source = 4` (Provisioning) for rows whose role is SuperAdmin or the tenant's built-in TenantAdmin. Leave the rest as 0 (Manual).
3. `Down` reverses: `IsActive = ("Status" = 0)` (Pending becomes inactive), drop the extra index columns, delete duplicate-source rows before restoring the old unique index.

### 1.5 Pending is not active (Requirement 2.3)

The read sites already go through `IsActive`, so the computed property covers the picker, `IsActiveMemberAsync`, `AuthSessionService` and the Login/SimpleSignup membership checks.

Role reads do not look at the membership today. They rely on leave deleting the rows. Pending members hold rows, so the authorization reads SHALL join on an Active membership:
- `UserRoleRepository.GetByUserIdAsync`, `GetByUserIdForTenantAsync`, `GetSystemRolesForUserAsync` (used by `PermissionService` and `TokenService`)
- `GetTenantAdminHoldersAsync` (already joins `ut.IsActive`; changes to `Status == Active`)

The join is `Context.Set<UserTenant>().Any(ut => ut.UserId == ur.UserId && ut.TenantId == ur.TenantId && ut.Status == Active)`. The SuperAdmin bootstrap already creates a System-tenant membership, so SuperAdmin keeps working. A test pins that.

Because a role can now have several rows (one per source), these reads return distinct roles (`GroupBy RoleId` or `DistinctBy` after mapping). Member listing uses a new query that includes Pending members and every source.

## 2. Role assignment (PR A)

### 2.1 Interfaces

```csharp
// GroundUp.Auth.Services/Roles — public, for admin UIs and SDK callers
public interface IUserRoleService
{
    Task<OperationResult<UserRoleDto>> AssignRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default);
    Task<OperationResult> RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default);
    Task<OperationResult<bool>> CanGrantRoleAsync(Guid roleId, CancellationToken ct = default);
}

// internal — trusted callers in GroundUp.Auth.Services only
internal interface IRoleGrantWriter
{
    Task<OperationResult<UserRoleDto>> GrantAsync(Guid userId, Guid roleId, Guid tenantId, RoleAssignmentSource source, CancellationToken ct = default);
    Task<OperationResult> RemoveBySourceAsync(Guid userId, Guid tenantId, RoleAssignmentSource source, CancellationToken ct = default);
}
```

Both are implemented by one `internal sealed class UserRoleService`.
- `IUserRoleService` acts in the current tenant, as the current user, and always checks the rules. The source is Manual.
- `IRoleGrantWriter` is the system path for provisioning, the default role, invitations and join links. It skips the caller rules; each caller checks what it needs (for example, invitation creation checks the creator). Being `internal` keeps SDK callers off it, so "every caller goes through `IUserRoleService`" holds for code outside the module.
- Both paths share the save, event and cache steps, so there is still one place that writes `UserRole` rows.

`IUserRoleService` is registered plainly (`AddScoped`), not through `AddAuthorized`. Its rules depend on the target role, which an attribute can't express. The service rejects an anonymous caller itself (401). The startup check (section 6.3) does not apply to it for that reason.

### 2.2 Grant rules (`RoleGrantPolicy`, internal)

`Task<OperationResult> CheckAsync(Guid callerId, RoleDto role, Guid tenantId, RoleChange change, CancellationToken ct)`:

1. Classify the role (Requirement 1): SuperAdmin = `RoleType.System` and tenant = `AuthRoleNames.SystemTenantId`; TenantAdmin = `IsSystem && Name == AuthRoleNames.TenantAdmin`; otherwise Other.
2. Read the caller's roles from the repository, not from `IPermissionService`:
   - `isSuperAdmin`: `GetSystemRolesForUserAsync(callerId)` contains SuperAdmin.
   - `isTenantAdmin`: `GetByUserIdForTenantAsync(callerId, tenantId)` contains TenantAdmin.
3. SuperAdmin role: allowed only if `isSuperAdmin`. TenantAdmin role: `isSuperAdmin || isTenantAdmin`. Other: those, or `IPermissionService.HasPermissionAsync(callerId, auth.roles.assign)` with the tenant context set to `tenantId`.
4. Run every registered `IRoleAssignmentRule` in registration order. The first refusal wins.

```csharp
public interface IRoleAssignmentRule
{
    Task<OperationResult> EvaluateAsync(RoleAssignmentContext context, CancellationToken ct = default);
}
public sealed record RoleAssignmentContext(Guid CallerUserId, Guid TargetUserId, RoleDto Role, Guid TenantId, RoleChange Change, RoleAssignmentSource Source);
public enum RoleChange { Assign, Remove }
```
The rules run for every caller-checked change: manual changes, invitation and join-link creation, and the acceptance re-check. They do not run for provisioning or the default role.

Refusals: 403 `FORBIDDEN` with a reason message (localized resource string).

### 2.3 Assign

`AssignRoleAsync(userId, roleId)` in the current tenant:
1. 401 if anonymous. Load the role (404 if missing or in another tenant; SuperAdmin is looked up in the System tenant). 404 if the target has no Active or Pending membership in the role's tenant. SuperAdmin is the exception: nobody joins the System tenant, so granting SuperAdmin creates the Active System-tenant membership if it is missing (as the bootstrap does).
2. `RoleGrantPolicy.CheckAsync`.
3. If a row with the same (user, role, tenant, source) exists, return it (no change).
4. Insert. A unique violation (concurrent assign) is treated as "already there" and re-read.
5. After save: publish `EntityCreatedEvent<UserRoleDto>` with `TenantId` and `UserId` set to the **target** user (the existing `UserRoleChangedHandler` reads them that way), and evict `PermissionCacheKeys.For(userId, tenantId)`.

### 2.4 Remove

`RemoveRoleAsync(userId, roleId)`:
1. Same lookups and rules (`RoleChange.Remove`).
2. If the role is SuperAdmin and the target is the last active SuperAdmin: 409 `LAST_SUPERADMIN` (decided, open point 4). If the role is TenantAdmin: `LastAdminGuard.CanRemoveTenantAdminAsync`. The guard is fixed to check that the user is actually a holder (today it doesn't), so removing TenantAdmin from a non-holder is a no-op rather than a false `LAST_ADMIN`.
3. Delete all the user's rows for that role and tenant (every source today; GU-69 will exclude `IdpGroup`).
4. After save: one `EntityDeletedEvent<UserRoleDto>` per row, cache eviction, and `ITokenRevocationStore.RevokeTenantAccessAsync`.

`RemoveBySourceAsync` does the same without rules and guard, restricted to one source. It is used when an invitation is revoked, which never touches an Active member.

### 2.5 Shared access-ending step

`MembershipService.EndAccessAsync` (revocation cutoff plus cache eviction, failures logged) moves to an internal `TenantAccessRevoker` used by `MembershipService`, `UserRoleService` and `MemberManagementService`.

### 2.6 Existing writes moved onto the service

- `OrganizationProvisioningService.CreateOrganizationRowsAsync` calls `IRoleGrantWriter.GrantAsync(..., Provisioning)` inside its transaction. Events and cache eviction run after the transaction commits, so the writer offers a deferred form: `GrantAsync` returns the DTO, and the caller publishes through `IRoleGrantWriter.PublishGrantedAsync(dto)` after commit. (The cache for a brand-new tenant is empty, so this only matters for the audit trail.)
- `UserTenantRepository.DeactivateMembershipAsync` returns the removed `UserRoleDto`s. `MembershipService.LeaveTenantAsync` publishes a deleted event for each.
- `IdentityBootstrapRepository.EnsureSuperAdminAsync` stays as it is (documented exception), now writing `Source = Provisioning` and `Status = Active`.

### 2.7 Default role (Requirement 5.6-5.7)

- `DefaultAuthSettingsSeeder` defines `auth.application.default-role` (String, levels system and tenant, default empty).
- New `IRoleRepository.GetByNameAsync(Guid tenantId, string name)` (system bypass, exact match ignoring case).
- A shared internal `DefaultRoleAssigner.AssignAsync(userId, tenantId)` replaces the two stubs in `LoginFlowHandler` and `SimpleSignupFlowHandler`. It reads the setting with an explicit Tenant→System scope chain for `tenantId`, finds the role, and calls `GrantAsync(..., DefaultRole)`. A missing role is logged at Warning and skipped. It runs on new and reactivated memberships.

## 3. Pending users and claiming (PR A)

### 3.1 `SignInUserResolver` (internal, replaces the three `ResolveOrCreateUserAsync` copies)

```csharp
internal sealed class SignInUserResolver
{
    Task<UserDto> ResolveAsync(ExternalUserInfo info, Guid? invitedPendingUserId, CancellationToken ct);
}
```
Order:
1. **Known identity:** a user with this `ExternalUserId` exists → U. If `invitedPendingUserId` names a different, unlinked user P → merge P into U (3.2). Return U.
2. **Invitation claim:** `invitedPendingUserId` names an unlinked, unmerged user P → link P to the identity (set `ExternalUserId`; fill `DisplayName` if empty). Return P.
3. **Verified-email claim:** `email_verified` is `"true"` in `info.Attributes`, and exactly one unlinked, unmerged user has this email (ignoring case) → link it. Return it.
4. **New user** otherwise.

Linking also sets `ExternalUserId` on that user's memberships where it is null. A unique violation while linking or creating (concurrent first sign-in) re-reads by `ExternalUserId`, as today.

The resolver is the single claim step that GU-69 extends (Requirement 2.6). GU-69 can add a match strategy behind an internal interface, but GU-68 does not introduce one (YAGNI).

### 3.2 Merge (Requirement 2.5)

Runs in one transaction in a new repository method `IUserRepository.MergePendingUserAsync(Guid fromUserId, Guid toUserId)`:
- For each of P's memberships:
  - U has no membership in that tenant → move it to U (`UserId = U`, `ExternalUserId` from U).
  - U has an Active membership → delete P's membership and its role rows. U's roles don't change (the already-member rule).
  - U has an Inactive or Pending membership → U's membership takes P's status if P's is Pending; P's role rows move to U (skipping duplicates of the same role and source); P's membership is deleted.
- P: `MergedIntoUserId = U`, `IsActive = false`. The record is kept (decided).
- After commit, the service publishes `PendingUserMerged(FromUserId, ToUserId)` and evicts U's cached permissions for the affected tenants.

## 4. Member management (PR A)

```csharp
public interface IMemberManagementService
{
    [RequiresPermission(PermissionKeysAuth.MembersRead)]
    Task<OperationResult<IReadOnlyList<TenantMemberDto>>> ListMembersAsync(CancellationToken ct = default);

    [RequiresPermission(PermissionKeysAuth.MembersManage)]
    Task<OperationResult> RemoveMemberAsync(Guid userId, CancellationToken ct = default);
}
public sealed record TenantMemberDto(Guid UserId, string Email, string DisplayName, MembershipStatus Status, IReadOnlyList<MemberRoleDto> Roles);
public sealed record MemberRoleDto(Guid RoleId, string RoleName, IReadOnlyList<RoleAssignmentSource> Sources);
```
- Registered by `AddGroundUpAuth` itself with `AddAuthorized` (as built in group 3; the implementation is internal). It is the auth module's own service, so the app has nothing to register. The startup check still covers it in case an app replaces the registration (6.3).
- Current tenant only. Pending and Active members are listed; Inactive are not. Paged (`PaginationParams`), ordered by email; the endpoint also sets the pagination headers.
- `RemoveMemberAsync`: 404 if not Active or Pending; the caller must be allowed to remove **each role the member holds** (`RoleGrantPolicy`, Remove), so `auth.members.manage` alone cannot remove a TenantAdmin; `LastAdminGuard.CanLeaveTenantAsync`; `DeactivateMembershipAsync` (deletes all role rows); `TenantAccessRevoker` (deleted events, revocation, cache). For a Pending member it also revokes their Pending invitation (Requirement 5.2; wired in group 4, when invitations exist). Removing yourself is allowed and behaves like leaving, except the session isn't re-issued; the client calls `/auth/leave` for that instead.
- Endpoints (`MembersController`, `BaseController`, results as `OperationResult` JSON): `GET /api/members`, `DELETE /api/members/{userId}`, `POST` and `DELETE /api/members/{userId}/roles/{roleId}`. Under `/api`, not `/auth`, so the host/token tenant check applies to these tenant-scoped admin calls.

This is a new service rather than new methods on `IMembershipService`, because `IMembershipService` holds the caller's own operations and is used by middleware without the authorization proxy. Both share `DeactivateMembershipAsync` and `TenantAccessRevoker`, so removal and leaving run the same steps (Requirement 5.2).

## 5. Invitations (PR B)

### 5.1 Entity

`Invitation : BaseEntity, ITenantEntity, IAuditable, ISoftDeletable` (table `AuthInvitations`, migration `AddInvitations`):

| Field | Notes |
|---|---|
| `TenantId` | |
| `Email`, `NormalizedEmail` | normalized = trimmed, `ToLowerInvariant()` (same rule as the users' `lower("Email")` index) |
| `RoleId` | the invitation's role |
| `CreatedByUserId` | Guid, set by the service. The acceptance re-check uses it, not the audit string `CreatedBy` |
| `UserId` | the pending (or existing) user the membership belongs to |
| `TokenHash` | 64-char hex SHA-256, unique index |
| `ExpiresAt` | UTC |
| `Status` | `InvitationStatus { Pending, Accepted, Revoked }` stored |
| `AcceptedByUserId`, `AcceptedAt` | |
| `RevokedAt`, `RevokedBy` | |

- The reported status adds Expired: `Status == Pending && ExpiresAt <= now` → Expired (Requirement 8.2). No job is needed.
- Partial unique index on (TenantId, NormalizedEmail) where `Status = Pending AND NOT IsDeleted`. It enforces `INVITATION_PENDING` under concurrency.

### 5.2 Tokens (`OpaqueToken`, internal static)

- `Create()`: 32 bytes from `RandomNumberGenerator`, Base64Url without padding (43 characters).
- `Hash(token)`: `SHA256.HashData(UTF8)` → lowercase hex.
- Lookup always hashes the presented token and queries by hash. No plain token is stored or logged. A plain SHA-256 is enough because the token has 256 bits of entropy; a slow hash adds nothing.

### 5.3 `IInvitationService` (admin, current tenant)

```csharp
public interface IInvitationService
{
    [RequiresPermission(PermissionKeysAuth.InvitationsSend)]
    Task<OperationResult<CreatedInvitationDto>> CreateAsync(CreateInvitationDto request, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.InvitationsSend)]
    Task<OperationResult<PaginatedData<InvitationDto>>> ListAsync(PaginationParams paging, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.InvitationsSend)]
    Task<OperationResult<InvitationDto>> ResendAsync(Guid invitationId, CancellationToken ct = default);
    [RequiresPermission(PermissionKeysAuth.InvitationsSend)]
    Task<OperationResult> RevokeAsync(Guid invitationId, CancellationToken ct = default);
}
```
- `CreateInvitationDto(string Email, Guid RoleId, DateTime? ExpiresAt)` with a FluentValidation validator in `GroundUp.Auth.Core` (email format, max 320, expiry in the future and within the lifetime).
- `CreatedInvitationDto` = `InvitationDto` plus nothing secret: the token only goes into the email. The API never returns it (Requirement 6.9).

`CreateAsync` order:
1. Tenant is enterprise (`RealmName != null`) → 422 `ENTERPRISE_NOT_SUPPORTED`.
2. Role in this tenant (404), `RoleGrantPolicy.CheckAsync(caller, role, Assign)` with source Invitation.
3. Expiry: `min(request.ExpiresAt, now + auth.invitations.lifetime-hours)`.
4. In `IUnitOfWork.ExecuteInTransactionAsync`:
   - find the user (Requirement 6.3): exactly one non-merged user with the email → that user; else one unlinked pending user with the email → that user; else insert a new user (`ExternalUserId = null`, `DisplayName = email`).
   - membership: Active → 409 `ALREADY_MEMBER`; Pending → 409 `INVITATION_PENDING`; Inactive → set to Pending; none → insert Pending.
   - `IRoleGrantWriter.GrantAsync(user, role, tenant, Invitation)`.
   - insert the invitation. A violation of the partial index → 409 `INVITATION_PENDING`.
5. After commit: publish role event; send the email (5.5). A send failure is logged; the invitation stays and the admin can resend.

`ResendAsync`: Pending (including expired) only, else 409. The resender must be allowed to grant the role, and becomes the invitation's `CreatedByUserId`, so an invitation whose creator lost the right can be revived by an admin who has it. New token, new hash, expiry restarted. Saved before sending.

`RevokeAsync`: Pending (including expired) only. In one transaction: status Revoked (conditional update), and a membership that is still Pending is deactivated with **all** its roles (`DeactivateMembershipAsync`), not just the Invitation-source ones: roles added to a pending member by hand would otherwise linger on an inactive membership and come back on a later rejoin. A member who became Active another way keeps their access. The row is kept as Inactive (not deleted), so a later invitation or rejoin reuses it. After commit: role-removed events, revocation and cache eviction (`TenantAccessRevoker`), then `InvitationRevoked`. The user record stays.

### 5.4 Settings (`DefaultAuthSettingsSeeder`)

| Key | Type | Levels | Default |
|---|---|---|---|
| `auth.invitations.lifetime-hours` | Int, 1-720 | system, tenant | 168 |
| `auth.invitations.accept-url` | String | system | empty |
| `auth.invitations.require-verified-email` | Bool | system | false |

An empty `accept-url` makes `CreateAsync` and `ResendAsync` fail with 422 `INVITATION_URL_NOT_CONFIGURED` before anything is written.

### 5.5 Email

```csharp
public interface IInvitationEmailSender
{
    Task SendAsync(InvitationEmail email, CancellationToken ct = default);
}
public sealed record InvitationEmail(string To, string OrganizationName, string RoleName, string AcceptUrl, DateTime ExpiresAt, Guid InvitationId);
```
`AcceptUrl` replaces `{token}` (URL-escaped) and `{tenantSlug}`. `LoggingInvitationEmailSender` (`TryAddScoped`) logs `To` and `InvitationId`, plus `AcceptUrl` only when `IHostEnvironment.IsDevelopment()`. GU-64 replaces it.

### 5.6 Public look-up and start (`IInvitationAcceptanceService`)

Not wrapped by `AddAuthorized`; the token is the credential.

- `LookupAsync(string token)` → `InvitationLookupDto(bool Usable, string? Reason, string? OrganizationName, string? RoleName)`. Unknown/revoked/expired/accepted give only `Reason`.
- `StartAsync(string token, bool forceLogin, HttpContext http)` → `FlowInitiationResult`:
  1. Look up by hash (system bypass). Not usable → 410 with the reason code.
  2. Enterprise tenant → 422 `ENTERPRISE_NOT_SUPPORTED`.
  3. Host check: `HostResolvedTenant` set and different from the invitation tenant → 403 `INVITATION_WRONG_ORGANIZATION`.
  4. Page choice:
     - session email (claim `AuthOptions.EmailClaimType` on `http.User`) present and different from the invited email, or `forceLogin` → `prompt=login`
     - session email equal → no prompt (Keycloak SSO returns straight away)
     - no session → `IIdentityProviderAdminService.FindUserByEmailAsync(realm, email)`: found → no prompt; not found → `prompt=create`; failure → no prompt
  5. `AuthFlowService.InitiateFlowAsync` with `FlowType.Invitation`, `InvitationId`, `LoginHint = email`, the prompt.

The response is the same shape in every case (`{ redirectUrl }`), so it doesn't reveal whether an account exists (Requirement 7.2).

### 5.7 Flow plumbing changes

As built (group 5):
- `FlowInitiationRequest` gains `Guid? InvitationId`, `Guid? JoinLinkId`, `string? LoginHint` and `bool Register` (opens the registration page, like the sign-up flows).
- `AuthUrlRequest` gains `string? LoginHint`; `AuthUrlBuilderService` writes `login_hint` when set. The existing `IsSignup` (`prompt=create`) and `ForceLogin` (`prompt=login`) flags are kept instead of a new `AuthPrompt` enum: the acceptance service only ever sets one of them, so the enum would add churn without a new case.
- `AuthFlowService.InitiateFlowAsync` passes `InvitationId`/`JoinLinkId` through instead of `null`. `InitiateAuthFlowRequestValidator`: InvitationId required for `FlowType.Invitation`, JoinLinkId for `FlowType.JoinLink`, both null otherwise.
- `FlowResult` gains `bool AlreadyMember`. `AuthController.Callback` adds `alreadyMember` to the success body.
- A Pending membership activated by any other path (default-tenant sign-in now, join links in group 6) closes its pending invitations as Accepted inside `UserTenantRepository.ReactivateMembershipAsync`, in the same save, so the link cannot be used afterwards (task 5.4a).
### 5.8 `IIdentityProviderAdminService.FindUserByEmailAsync`

`Task<OperationResult<IdentityProviderUserDto?>> FindUserByEmailAsync(string realmName, string email, CancellationToken ct = default)`. Keycloak: `GET /admin/realms/{realm}/users?email={email}&exact=true&briefRepresentation=true`, existing token cache, client and status mapping. `IdentityProviderUserDto(string ExternalUserId, string Email, bool EmailVerified)`. Realm = tenant realm or `SharedRealmName`.

### 5.9 `InvitationFlowHandler` (callback)

Follows the `LoginFlowHandler` pattern (code exchange, id token, `CallbackSessionGuard` at system level, userinfo). Then:

1. Load the invitation by `state.InvitationId` (bypass). Revoked or expired → 410 `INVITATION_NO_LONGER_VALID`. Accepted → 410 `INVITATION_ALREADY_USED`.
2. Email: `string.Equals(info.Email, invitation.Email, OrdinalIgnoreCase)`, else 403 `INVITATION_EMAIL_MISMATCH` (message includes the invited address).
3. `require-verified-email` on and `email_verified != "true"` → 403 `EMAIL_NOT_VERIFIED`.
4. Point `TenantContext` at the invitation tenant.
5. `SignInUserResolver.ResolveAsync(info, invitation.UserId)` → U (links or merges the pending user).
6. U already Active in the tenant → mark Accepted (conditional update, 6), sign in, `AlreadyMember = true`. Stop.
7. `RoleGrantPolicy.CheckAsync(invitation.CreatedByUserId, role, Assign)` → refusal gives 403 `INVITATION_NO_LONGER_VALID`; the invitation stays Pending.
8. `CallbackSessionGuard.CheckAsync(authTime, tenantId)` before any change.
9. Transaction:
   - `UPDATE AuthInvitations SET Status = Accepted, AcceptedByUserId, AcceptedAt WHERE Id = @id AND Status = Pending AND ExpiresAt > now` (`ExecuteUpdateAsync`). 0 rows → 410 `INVITATION_ALREADY_USED`.
   - membership → Active.
10. After commit: evict cache, publish `InvitationAccepted(InvitationId, TenantId, UserId)`, issue the tenant token with `auth_time`, write the cookies. `FlowResult.Success`.

Any refusal after the state was consumed calls `MarkFailedAsync` with the reason, as the other handlers do. The invitation itself is unchanged by a refusal, so the user can start again from the link.

### 5.10 Endpoints (`InvitationsController`, `GroundUp.Auth.Api`)

| Method and route | Service call |
|---|---|
| `POST /api/invitations` | `CreateAsync` |
| `GET /api/invitations` | `ListAsync` |
| `POST /api/invitations/{id}/resend` | `ResendAsync` |
| `POST /api/invitations/{id}/revoke` | `RevokeAsync` |
| `POST /auth/invitations/lookup` `{ token }` | `LookupAsync` |
| `POST /auth/invitations/accept` `{ token, forceLogin }` | `StartAsync` → `{ redirectUrl }` |

- Admin endpoints are tenant-scoped and live under `/api` (like `/api/members`), so the host/token check applies. Two controllers: `InvitationsController` (`/api/invitations`, `BaseController`, `OperationResult` JSON) and the public endpoints on a controller under `/auth`.
- The token goes in the body, not the path or query, so it doesn't land in access logs.
- The public endpoints are under `/auth`, so the host/token check exempts them (Requirement 7.2).
- Cookie-authenticated POSTs need `X-CSRF-Token` as today. A signed-out accept page has no cookie and needs none.
- Public endpoint results return `{ error, errorCode }`, matching `AuthController`.

## 6. Join links, permissions, startup check (PR C)

### 6.1 Entity and service

`JoinLink : BaseEntity, ITenantEntity, IAuditable, ISoftDeletable` (table `AuthJoinLinks`, migration `AddJoinLinks`): `RoleId`, `CreatedByUserId`, `TokenHash` (unique), `DateTime? ExpiresAt`, `int? MaxUses`, `int UseCount`, `JoinLinkStatus { Active, Revoked }`, `RevokedAt`, `RevokedBy`.

`IJoinLinkService` (`AddAuthorized`, all `[RequiresPermission(JoinLinksManage)]`): `CreateAsync` → `CreatedJoinLinkDto` (the only response that carries the token), `ListAsync`, `RegenerateAsync` → `CreatedJoinLinkDto`, `RevokeAsync`. Create and regenerate check enterprise (422) and `RoleGrantPolicy` (source JoinLink).

`IJoinLinkAcceptanceService`: `LookupAsync(token)` and `StartAsync(token, forceLogin, http)`. The status, enterprise and host checks are the same as for invitations (5.6 steps 1-3). There is no email, so the page is the sign-in page (no prompt) unless `forceLogin`, and there is no `login_hint`.

### 6.2 `JoinLinkFlowHandler`

Same front part as the invitation handler. Then:
1. Load the link; revoked, expired or used up → 410 `JOIN_LINK_NO_LONGER_VALID`.
2. `SignInUserResolver.ResolveAsync(info, null)`.
3. U Active in the tenant → sign in, `AlreadyMember = true`, no count change.
4. `RoleGrantPolicy.CheckAsync(link.CreatedByUserId, role, Assign)` → 403 `JOIN_LINK_NO_LONGER_VALID` on refusal.
5. Tenant session guard.
6. Transaction:
   - `UPDATE AuthJoinLinks SET UseCount = UseCount + 1 WHERE Id = @id AND Status = Active AND NOT IsDeleted AND (MaxUses IS NULL OR UseCount < MaxUses) AND (ExpiresAt IS NULL OR ExpiresAt > now)`. 0 rows → 410.
   - membership: none → insert Active; Inactive → Active; Pending → Active and mark the user's Pending invitation in this tenant Accepted (Requirement 9.4).
   - `GrantAsync(U, role, tenant, JoinLink)`.
7. After commit: events, cache, token, cookies.

Endpoints mirror invitations under `/auth/join-links` (admin) with `POST /auth/join-links/lookup` and `POST /auth/join-links/join`.

### 6.3 Permissions and startup check

- `PermissionKeysAuth`: `RolesAssign`, `MembersRead`, `MembersManage`, `InvitationsSend`, `JoinLinksManage`, each added to `All` so `DefaultPermissionSeeder` seeds them.
- `SettingsAuthorizationStartupValidator` (renamed in docs to "authorization startup check"; the class name stays to avoid churn) also checks `IMemberManagementService`, `IInvitationService` and `IJoinLinkService`. Unregistered services pass, as today.
- The sample registers all three with `AddAuthorized` plus the plain registrations.

## 7. Events

New records in `GroundUp.Auth.Services/Events` (not `Auth.Core`: `Auth.Core` references only `GroundUp.Core`, and the events derive from `GroundUp.Events.BaseEvent`), all deriving from `BaseEvent`:
- `PendingUserMerged(Guid FromUserId, Guid ToUserId)`
- `InvitationAccepted(Guid InvitationId, Guid UserId)`
- `InvitationRevoked(Guid InvitationId, Guid UserId)`

`TenantId` is set on each. Role changes use the existing `EntityCreatedEvent<UserRoleDto>` / `EntityDeletedEvent<UserRoleDto>`, with `UserId` = the affected user, which is what `UserRoleChangedHandler` already expects.

## 8. Error codes

New `AuthErrorCodes` constants (or additions to the existing auth error-code class): `ALREADY_MEMBER`, `INVITATION_PENDING`, `INVITATION_EMAIL_MISMATCH`, `EMAIL_NOT_VERIFIED`, `INVITATION_NO_LONGER_VALID`, `INVITATION_ALREADY_USED`, `INVITATION_WRONG_ORGANIZATION`, `INVITATION_URL_NOT_CONFIGURED`, `JOIN_LINK_NO_LONGER_VALID`, `ENTERPRISE_NOT_SUPPORTED`, `LAST_SUPERADMIN`. User-facing messages are resource strings.

## 9. Tests

- **Unit (NSubstitute):** `RoleGrantPolicy` matrix (caller SuperAdmin/TenantAdmin/permission holder/none × role kind × assign/remove), app rule refusal, `SignInUserResolver` order, `OpaqueToken`, accept-page choice, URL template, validators.
- **Integration (Testcontainers Postgres):** migration on seeded old-shape data; pending exclusion; repository conditional updates under concurrency (two parallel accepts, parallel last join-link use); merge; revoke leaving a Manual grant.
- **Integration with Keycloak (`KeycloakFixture`):** end-to-end invitation and join-link sign-ins using the PKCE login form from GU-73, including register vs sign-in, `prompt=login` for a different session, and verified-email setting on/off.
- CI rules as GU-73: per-test data, pinned images, dynamic ports, no sleeps.

## 10. Task groups and PRs

| PR | Group | Content |
|---|---|---|
| A | 1 | Enums, entity/DTO/config changes, migration, `IsActive` call sites, Active-membership join in role reads |
| A | 2 | `RoleGrantPolicy`, `IRoleAssignmentRule`, `UserRoleService` (both interfaces), `TenantAccessRevoker`, `LastAdminGuard` fix, provisioning and leave moved over |
| A | 3 | `SignInUserResolver` + merge, default role, `IMemberManagementService` + controller, permission keys, startup check |
| B | 4 | Invitation entity, migration, repository, `OpaqueToken`, settings, email sender, `IInvitationService` + admin endpoints |
| B | 5 | Flow plumbing (prompt enum, `login_hint`, ids), `FindUserByEmailAsync`, acceptance service, `InvitationFlowHandler`, public endpoints |
| C | 6 | Join link entity to handler and endpoints |
| C | 7 | Keycloak end-to-end tests, sample registration, manual testing guide, `realm.json` check |

## 11. Open points and trade-offs

1. **`IUserRoleService` is not attribute-wrapped.** Its rules depend on the target role, so it checks in code. An SDK caller therefore can't bypass it by skipping `AddAuthorized`, which is safer than the attribute model.
2. **`IRoleGrantWriter` is internal.** A consuming app that wants its own automatic grant source (for example, a billing plan) can't add one yet. It can be made public with an app source later if needed.
3. **Member removal lives in a new service,** sharing the leave steps rather than going through `IMembershipService` itself (section 4).
4. **Last SuperAdmin** (decided): removing the SuperAdmin role from the last active SuperAdmin, including oneself, returns 409 `LAST_SUPERADMIN`, like the TenantAdmin guard.
5. **Email lookup** uses a functional index on `lower("Email")` for users and `NormalizedEmail` for invitations. User emails stay non-unique.
