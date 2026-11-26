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

- [x] 2. Role service and rules (design 2.1-2.6)
  - [x] 2.1 `RoleGrantPolicy`, `IRoleAssignmentRule`, `RoleAssignmentContext`, `RoleChange`; error codes (design 8) and resource strings
  - [x] 2.2 `IUserRoleService` and internal `IRoleGrantWriter` in one `UserRoleService`: assign, remove, `CanGrantRoleAsync`, events with the affected user, cache eviction, revocation on removal, System-tenant membership for SuperAdmin grants
  - [x] 2.3 Guards: `LastAdminGuard.CanRemoveTenantAdminAsync` checks the user is a holder and is now called; `LAST_SUPERADMIN`
  - [x] 2.4 `TenantAccessRevoker` extracted from `MembershipService`; `DeactivateMembershipAsync` returns the removed roles and leave publishes deleted events
  - [x] 2.5 `OrganizationProvisioningService` grants TenantAdmin through `IRoleGrantWriter` (source Provisioning), events after commit
  - [x] 2.6 Unit tests: the full rule matrix (caller kind × role kind × assign/remove), app rule refusal, both guards, idempotent assign/remove, events and revocation; integration: concurrent assign, invitation revoke keeps a Manual grant, removal revokes tokens, pending member role not effective, provisioning grant has source Provisioning (unit 1870 / 0; integration 442 / 0; sample 22/22)

- [x] 3. Claiming, default role and member management (design 2.7, 3, 4)
  - [x] 3.1 `SignInUserResolver` replaces the three `ResolveOrCreateUserAsync` copies (known identity, invitation claim, verified-email claim, new user)
  - [x] 3.2 `IUserRepository.MergePendingUserAsync` and the `PendingUserMerged` event
  - [x] 3.3 `auth.application.default-role` definition, `IRoleRepository.GetByNameAsync`, `DefaultRoleAssigner` used by Login and SimpleSignup (new and reactivated memberships)
  - [x] 3.4 Permission keys (`RolesAssign`, `MembersRead`, `MembersManage`, `InvitationsSend`, `JoinLinksManage`) in `PermissionKeysAuth.All`
  - [x] 3.5 `IMemberManagementService` (list, remove; removal needs the right to remove each role held), `MembersController` under `/api/members`; registered by `AddGroundUpAuth` through `AddAuthorized` (no sample registration needed); startup check extended
  - [x] 3.6 Tests: resolver order and races, merge cases (none / Active / Inactive or Pending at U), default role assigned and missing-role skip, member list includes pending with sources, removal runs the leave steps; endpoints through the sample app; default role and verified-email claim through the real callback (unit 1893 / 0; integration 459 / 0; sample 22/22)
  - PR boundary (PR A2): push, give the compare URL, title and description

## PR B: invitations

- [x] 4. Invitation records and admin service (design 5.1-5.5, 5.10 admin rows)
  - [x] 4.1 `Invitation` entity, configuration (partial unique index on pending email), migration `AddInvitations`, `IInvitationRepository` (bypass lookup by hash, conditional accept)
  - [x] 4.2 `OpaqueToken` (create, hash)
  - [x] 4.3 Invitation settings in `DefaultAuthSettingsSeeder`; `IInvitationEmailSender`, `LoggingInvitationEmailSender`, URL template
  - [x] 4.4 `IInvitationService` (create, list, resend, revoke) with `CreateInvitationDto` validator; `InvitationsController` admin endpoints under `/api/invitations`; startup check
  - [x] 4.5a `RemoveMemberAsync` on a Pending member also revokes their Pending invitation
  - [x] 4.5 Tests: create paths (new pending user, existing user, unlinked pending user reused, Inactive member back to Pending), `ALREADY_MEMBER`, `INVITATION_PENDING` (including concurrent creates), enterprise refused, URL not configured, resend rotates the token, revoke removes only Invitation-source roles, link logged only in Development; refusals before any write; expiry clamp (unit 1922 / 0; integration 472 / 0; sample 22/22). Revoke deactivates the pending membership with all its roles (design 5.3)

- [ ] 5. Accepting an invitation (design 5.6-5.9, 5.10 public rows)
  - [ ] 5.1 Flow plumbing: `AuthPrompt`, `login_hint`, `InvitationId`/`JoinLinkId` passed through, validator rules, `FlowResult.AlreadyMember` and callback body
  - [ ] 5.2 `IIdentityProviderAdminService.FindUserByEmailAsync` and the Keycloak implementation
  - [ ] 5.3 `IInvitationAcceptanceService` (look-up, start with page choice, host check)
  - [ ] 5.4 `InvitationFlowHandler` and registration; public endpoints
  - [ ] 5.4a A Pending membership activated by another path (default-tenant sign-in, join link) closes its invitation as Accepted
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
