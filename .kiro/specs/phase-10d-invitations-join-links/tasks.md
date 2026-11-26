# Tasks: Invitations, Join Links and Role Assignment (GU-68)

Execute one top-level task at a time. After each: build, run all three suites against the baselines, commit, stop for review.

Baselines (main after #48): unit 1793 / 0; integration 426 / 0 / 0 skipped; sample 22/22.

Requirements: `requirements.md`. Design: `design.md` (section numbers below refer to it).

## PR A: role and member foundation

- [x] 1. Pending model and role source (design 1)
  - [x] 1.1 `MembershipStatus`, `RoleAssignmentSource` enums; `User`, `UserTenant`, `UserRole` entity changes; DTO changes (`UserTenantDto.Status` with computed `IsActive`, `UserRoleDto.Source`, nullable `ExternalUserId`, `MergedIntoUserId`)
  - [x] 1.2 EF configurations and indexes (filtered unique `ExternalUserId`, `lower("Email")`, new UserRole unique index); migration `AddPendingMembersAndRoleSource` with the data SQL and `Down`
  - [x] 1.3 Construction sites move to `Status: Active` (Login, SimpleSignup, provisioning, bootstrap, `SetMembershipActiveAsync`); bootstrap writes `Source = Provisioning`
  - [x] 1.4 Role reads join on an Active membership and return distinct roles (`GetByUserIdAsync`, `GetByUserIdForTenantAsync`, `GetSystemRolesForUserAsync`, `GetTenantAdminHoldersAsync`)
  - [x] 1.5 Tests: migration on old-shape data (status and source mapping); pending membership gives no permissions, no token roles, not in the picker, not counted by the last-admin guard; SuperAdmin still resolves; existing suites green (unit 1795 / 0; integration 437 / 0; sample 22/22). Existing tests that gave roles without a membership now seed one; the auth migrations no longer try to create the bootstrap tables, which the app context owns

- [ ] 2. Role service and rules (design 2.1-2.6)
  - [ ] 2.1 `RoleGrantPolicy`, `IRoleAssignmentRule`, `RoleAssignmentContext`, `RoleChange`; error codes (design 8) and resource strings
  - [ ] 2.2 `IUserRoleService` and internal `IRoleGrantWriter` in one `UserRoleService`: assign, remove, `CanGrantRoleAsync`, events with the affected user, cache eviction, revocation on removal, System-tenant membership for SuperAdmin grants
  - [ ] 2.3 Guards: `LastAdminGuard.CanRemoveTenantAdminAsync` checks the user is a holder and is now called; `LAST_SUPERADMIN`
  - [ ] 2.4 `TenantAccessRevoker` extracted from `MembershipService`; `DeactivateMembershipAsync` returns the removed roles and leave publishes deleted events
  - [ ] 2.5 `OrganizationProvisioningService` grants TenantAdmin through `IRoleGrantWriter` (source Provisioning), events after commit
  - [ ] 2.6 Unit tests: the full rule matrix (caller kind × role kind × assign/remove), app rule refusal, both guards, idempotent assign/remove, events and revocation; integration: concurrent assign, provisioning still atomic

- [ ] 3. Claiming, default role and member management (design 2.7, 3, 4)
  - [ ] 3.1 `SignInUserResolver` replaces the three `ResolveOrCreateUserAsync` copies (known identity, invitation claim, verified-email claim, new user)
  - [ ] 3.2 `IUserRepository.MergePendingUserAsync` and the `PendingUserMerged` event
  - [ ] 3.3 `auth.application.default-role` definition, `IRoleRepository.GetByNameAsync`, `DefaultRoleAssigner` used by Login and SimpleSignup (new and reactivated memberships)
  - [ ] 3.4 Permission keys (`RolesAssign`, `MembersRead`, `MembersManage`, `InvitationsSend`, `JoinLinksManage`) in `PermissionKeysAuth.All`
  - [ ] 3.5 `IMemberManagementService` (list, remove), `MembersController` (list, remove, assign and remove role via `IUserRoleService`); startup check extended; sample registration
  - [ ] 3.6 Tests: resolver order and races, merge cases (none / Active / Inactive or Pending at U), default role assigned and missing-role skip, member list includes pending with sources, removal runs the leave steps; endpoints through the sample app
  - PR A boundary: push, give the compare URL, title and description

## PR B: invitations

- [ ] 4. Invitation records and admin service (design 5.1-5.5, 5.10 admin rows)
  - [ ] 4.1 `Invitation` entity, configuration (partial unique index on pending email), migration `AddInvitations`, `IInvitationRepository` (bypass lookup by hash, conditional accept)
  - [ ] 4.2 `OpaqueToken` (create, hash)
  - [ ] 4.3 Invitation settings in `DefaultAuthSettingsSeeder`; `IInvitationEmailSender`, `LoggingInvitationEmailSender`, URL template
  - [ ] 4.4 `IInvitationService` (create, list, resend, revoke) with `CreateInvitationDto` validator; `InvitationsController` admin endpoints; startup check
  - [ ] 4.5 Tests: create paths (new pending user, existing user, unlinked pending user reused, Inactive member back to Pending), `ALREADY_MEMBER`, `INVITATION_PENDING` (including concurrent creates), enterprise refused, URL not configured, resend rotates the token, revoke removes only Invitation-source roles, link logged only in Development

- [ ] 5. Accepting an invitation (design 5.6-5.9, 5.10 public rows)
  - [ ] 5.1 Flow plumbing: `AuthPrompt`, `login_hint`, `InvitationId`/`JoinLinkId` passed through, validator rules, `FlowResult.AlreadyMember` and callback body
  - [ ] 5.2 `IIdentityProviderAdminService.FindUserByEmailAsync` and the Keycloak implementation
  - [ ] 5.3 `IInvitationAcceptanceService` (look-up, start with page choice, host check)
  - [ ] 5.4 `InvitationFlowHandler` and registration; public endpoints
  - [ ] 5.5 Tests: page choice (no session found / not found / lookup failure, same-email session, different-email session, `forceLogin`), host mismatch, every callback outcome (email mismatch, verified-email setting off and on, creator lost the right, already a member, merge, revoked, expired, concurrent acceptance)
  - PR B boundary: push, give the compare URL, title and description

## PR C: join links and end-to-end

- [ ] 6. Join links (design 6.1-6.2)
  - [ ] 6.1 `JoinLink` entity, configuration, migration `AddJoinLinks`, repository with the conditional use-count update
  - [ ] 6.2 `IJoinLinkService` (create, list, regenerate, revoke), `IJoinLinkAcceptanceService`, `JoinLinkFlowHandler`, controller, startup check
  - [ ] 6.3 Tests: new, former, pending (invitation closed) and active members; revoked, expired and used-up links; regenerate kills the old token; concurrent last use never exceeds the maximum

- [ ] 7. End-to-end checks and documentation
  - [ ] 7.1 Keycloak end-to-end tests (`KeycloakFixture`, PKCE login form): invitation with registration, invitation with sign-in, different-session forced sign-in, join link sign-in
  - [ ] 7.2 Sample app registration and Swagger check
  - [ ] 7.3 `realm.json` redirect URI check and fix if needed
  - [ ] 7.4 ARCHITECTURE.md, BUILD_PHASES.md, MANUAL_TESTING.md (invitations and join links part)
  - [ ] 7.5 Jira (GU-68 comment, Done after merge)
  - PR C boundary: push, give the compare URL, title and description
