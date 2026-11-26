# Requirements: Invitations, Join Links and Role Assignment (GU-68)

## Context

Tenants can't add people yet, except through sign-up into the default tenant or by creating an organization. Admins also can't list members, change a member's role or remove a member. Role assignment has no single entry point:
- Organization provisioning, SuperAdmin bootstrap and the leave flow write `UserRole` rows through repositories.
- Nothing publishes the role-change events, so the Phase 9 permission-cache handlers never run.
- The default-role step in the Login and SimpleSignup flows is a stub.

The permission check has a gap: `HasPermissionAsync` returns true for any TenantAdmin. So a permission-only rule cannot keep SuperAdmin assignment away from TenantAdmins.

The flow groundwork exists: `FlowType.Invitation`/`JoinLink`, `AuthFlowState.InvitationId`/`JoinLinkId` and the `auth.roles.assign-system` key. The dispatcher marks a flow state consumed before its handler runs, so a check that fails in the handler uses up the flow.

The data model can't hold an invited person yet:
- `User.ExternalUserId` is required and unique, so a user can't exist before signing in.
- `UserTenant.IsActive` is a bool, so there is no pending membership.
- `UserRole` has no record of why a role was granted. The unique index is (UserId, RoleId, TenantId).

## Scope

**In scope:**
- `IUserRoleService`, the role-granting rules and the role-assignment source
- pending users and pending memberships (one migration)
- member management (list members, change roles, remove a member)
- moving organization provisioning onto `IUserRoleService`
- the default-role setting
- invitations and join links (entities, services, flow handlers, endpoints)
- an email-sender stub
- the dev `realm.json` redirect URI check

**Out of scope:**
- **Sending real email.** Notifications (GU-64) replaces the logging stub.
- **Enterprise tenants** (GU-69). Invitations and join links are refused for them in GU-68 (Requirement 9.4). GU-69 adds enterprise sign-in, access policies, IdP groups as a role source, pre-registration by list and guests. The pending model and the source column here are built so GU-69 can use them without another redesign.
- **A built-in "no self-assignment" rule** (decided). Applications can add one through the hook in Requirement 3.5.
- **Rate limiting the public invitation and join-link endpoints.** The tokens are 256-bit, so guessing one is not practical.

**Delivery:** three PRs.
- PR A: role and member foundation, pending model and source migration (Requirements 1-5).
- PR B: invitations (Requirements 6-8).
- PR C: join links and the end-to-end checks (Requirements 9-11).

## Requirements

### Requirement 1: Role kinds

The code already distinguishes three kinds of role:

| Kind | How it is identified | Examples |
|---|---|---|
| SuperAdmin | `RoleType.System` in the System tenant | SuperAdmin |
| TenantAdmin | the built-in per-tenant role (`IsSystem = true`, name `TenantAdmin`) | TenantAdmin of each tenant |
| Other roles | `RoleType.Application` or `RoleType.Workspace`, not built-in | roles defined by the app or by a tenant |

1. These rules SHALL use the existing data. There is no migration of role types.

### Requirement 2: Pending users and memberships

An invited person becomes a member right away, as a **pending member**. The app can then refer to them (for example, assign them work) before they first sign in.

1. **Membership status.** A membership SHALL have a status: Pending, Active or Inactive. The migration maps today's `IsActive = true` to Active and `false` to Inactive.
2. **Pending users.** A user record MAY exist without an identity-provider id until someone claims it. `ExternalUserId` on the user and the membership becomes nullable. Its uniqueness applies only to non-null values.
3. **Pending is not active.** A pending membership SHALL NOT count as active anywhere:
   - the tenant picker and `GetMyTenantsAsync`
   - `IsActiveMemberAsync` and the host-pinned login check
   - permission and role resolution, and tokens
   - the last-admin guard
   A pending member's roles are stored but have no effect until the membership is Active.
4. **Claiming at sign-in.** When a person signs in for the first time through any flow, and an unlinked pending user has the same email (ignoring case), the system SHALL link that user to the signed-in identity only if:
   - the sign-in is through an invitation for that pending user, or
   - the identity provider marks the email as verified.
   Otherwise a new user is created. Linking a user does not activate any pending membership; only accepting the invitation does (Requirement 7).
5. **Merging.** If an invitation's pending user P is accepted by a different, existing user U, P's memberships and role assignments SHALL move to U. P is marked as merged into U and kept, so the app's references to P still resolve. A `PendingUserMerged` event (from P, to U) lets the app move its references.
6. GU-69 will add more ways to claim a pending user (enterprise email, UPN or employee-id match). The claim step SHALL be a single service so GU-69 can add to it.

### Requirement 3: Granting and removing roles

1. `IUserRoleService` SHALL be the only way to add or remove a user's role in a tenant. Every caller goes through it, including member management, invitations, join links, the default role and organization provisioning. The one exception is the SuperAdmin bootstrap, which runs before any user exists. It stays on its repository and is documented as the exception.
2. Who may grant or remove a role (decided):
   - **SuperAdmin:** only a SuperAdmin.
   - **TenantAdmin:** only a TenantAdmin of that tenant, or a SuperAdmin.
   - **Any other role:** a holder of the new permission `auth.roles.assign` in that tenant. TenantAdmins and SuperAdmins always may.
   - The SuperAdmin and TenantAdmin rules SHALL check the caller's roles, not permissions, because TenantAdmins pass every permission check in their tenant.
3. A role can only be assigned in its own tenant. SuperAdmin is the exception: it lives in the System tenant. A user must be an Active or Pending member of a tenant to receive a role in it.
4. The existing last-admin guard (`LastAdminGuard`, used today when a member leaves) SHALL also apply to removing the TenantAdmin role and to removing a member. Its existing remove-TenantAdmin check is not called anywhere yet. The tenant's last active TenantAdmin cannot lose that role or be removed. Pending TenantAdmins don't count.
5. Applications MAY register additional role-assignment rules (for example, "no self-assignment"). Every registered rule runs after the built-in rules, and any one can refuse with a message. The default is none.
6. After a change is saved:
   - The service SHALL publish the existing `EntityCreatedEvent<UserRoleDto>` / `EntityDeletedEvent<UserRoleDto>`, so the permission-cache handlers run.
   - It SHALL evict the user's cached permissions for the tenant.
   - On removal, it SHALL record a token revocation cutoff (GU-71) for the user and tenant, because roles are baked into issued tokens (decided).
7. Refusals return 403 `FORBIDDEN` with a reason. An unknown user, role or membership returns 404. Assigning a role the user already holds from the same source, or removing one they don't hold, succeeds without a change.

### Requirement 4: Role-assignment source

Each role assignment SHALL record why it exists (decided), so automatic processes never remove a grant they didn't make.

1. Sources: Manual, Invitation, JoinLink, DefaultRole and Provisioning. GU-69 adds IdP group.
2. A user can hold the same role from more than one source. The unique index becomes (UserId, RoleId, TenantId, Source). The user has the role while any assignment exists.
3. An automatic process removes only assignments with its own source. For example, revoking an invitation removes only that invitation's grants.
4. An admin removing a role removes all its assignments for that user and tenant, except those from an IdP group (GU-69), which only the group sync can remove.
5. The migration marks existing assignments as Provisioning (TenantAdmin from organization creation and the SuperAdmin bootstrap) or Manual (everything else).
6. Member listings show each role with its sources.

### Requirement 5: Member management, existing role writes and the default role

1. An admin can list a tenant's members with their roles and membership status (Pending or Active). This needs the new permission `auth.members.read`.
2. An admin can remove a member. This needs the new permission `auth.members.manage`. Removal goes through the existing `IMembershipService`, like leaving:
   - the membership is deactivated
   - the roles are removed
   - a revocation cutoff is recorded
   - the cache is evicted
   - the last-admin guard applies
   Removing a pending member also revokes their open invitation.
3. Role changes for members go through `IUserRoleService` (Requirement 3).
4. All of this works as SDK calls: the rules live in the services, and controllers only translate to HTTP.
5. Organization provisioning SHALL assign TenantAdmin through `IUserRoleService` (source Provisioning), inside its existing transaction. The creator becomes TenantAdmin of the new tenant, a grant the system makes on the creator's behalf, not one checked against the creator's current roles.
6. A new setting `auth.application.default-role` (a role name; tenant and System levels; empty by default) SHALL be defined.
7. When the Login or SimpleSignup flow creates a membership in the default tenant, it SHALL assign that role through `IUserRoleService` (source DefaultRole) if the setting names a role in that tenant. A missing role is logged and skipped. A rejoin (reactivated membership) gets it too, since leaving removed the user's roles.

### Requirement 6: Invitations

1. An invitation belongs to one tenant and records:
   - the invited email (required)
   - the pending user and membership it created
   - the role to grant (required, a role in that tenant)
   - an expiry
   - who created it
   - its status: Pending, Accepted, Expired or Revoked
   - who accepted it and when
2. Creating one needs `auth.invitations.send`, and the creator must be allowed to grant the role (Requirement 3.2). The default lifetime comes from a new setting `auth.invitations.lifetime-hours` (default 168, i.e. 7 days, maximum 720). A per-invitation expiry may be shorter.
3. **Creating the pending member.** In one transaction, creating an invitation SHALL:
   - refuse with 409 `ALREADY_MEMBER` if a user with that email is already an Active member of the tenant
   - refuse with 409 `INVITATION_PENDING` if that email already has a pending invitation to the tenant (the admin can resend it instead)
   - find the user: the one existing user with that email; otherwise an unlinked pending user with that email; otherwise a new pending user
   - create the Pending membership, or set an Inactive one back to Pending
   - grant the role through `IUserRoleService` (source Invitation)
   An admin can add more roles to a pending member through the normal role screens.
4. **Token:** the invitation token is 256 random bits, URL-safe, and stored only as a hash (decided). It appears once, in the invitation link.
5. **Email:** the link is sent through `IInvitationEmailSender`, built from a new setting `auth.invitations.accept-url` (the client app's accept page). The setting has a `{token}` placeholder and an optional `{tenantSlug}` placeholder, so an app that serves tenants on subdomains can send each invitee to their organization's own address (for example `https://{tenantSlug}.example.com/invite/{token}`).
   - The default `LoggingInvitationEmailSender` logs the recipient and invitation id.
   - It logs the link only in the Development environment, because the link is a credential.
6. **Admin operations:** callers with `auth.invitations.send` can list a tenant's invitations, revoke a pending or expired one, and resend one. Resending issues a new token, invalidates the old one and restarts the expiry.
7. **Revoking** removes the pending membership and its Invitation-source roles and publishes an event. The pending user record is kept, because the app may still refer to it.
8. **Expiry** does not remove the pending membership. It stays Pending, with no access, until an admin resends or revokes the invitation.
9. There SHALL be no endpoint that returns an existing token.

### Requirement 7: Accepting an invitation

1. **Look-up:** anyone holding the token can look up the invitation: the organization name, the role name, and whether it can still be used. An unknown, revoked, expired or accepted token gives a reason (`not_found`, `revoked`, `expired`, `accepted`) and nothing else.
2. **Starting the flow.** The invitation link opens the client app's accept page. That page calls the accept endpoint straight away and sends the browser to the Keycloak address it returns, so the invitee sees no form or button before Keycloak.
   - **Which page, chosen by the server** (decided), so the invitee never has to choose:
     - The server asks Keycloak (`IIdentityProviderAdminService`, new find-by-email method) whether an account with the invited email exists. If one does, the invitee gets the sign-in page; if not, the registration page (`prompt=create`).
     - If the look-up fails, the invitee gets the sign-in page, which links to registration.
     - The invited email is sent as `login_hint`, so Keycloak fills it in.
   - **Wrong account:** if the request carries a session for a different email than the invited one, the sign-in page is forced (`prompt=login`) for the invited address. Without that, Keycloak's existing session would sign in the wrong account, and the email check on the callback would fail.
   - **Already signed in:** an invitee already signed in to Keycloak as the right account goes straight back without seeing a Keycloak page.
   - **Refused up front:** a revoked, expired or accepted invitation is refused at this point, before the flow starts.
   - **Tenant subdomains:** on a host that names a tenant, the invitation must belong to that tenant. Otherwise it is refused, so an Acme invitation cannot be accepted on Globex's address. On the app's general host, any tenant's invitation works. The accept and look-up endpoints sit under `/auth`, which the host/token check already exempts, so an invitee signed in to another tenant is not blocked with `TENANT_SWITCH_REQUIRED`.
   - **Sessions:** this works signed out, signed in with a tenantless session, and signed in to another tenant.
   - **No account probing:** the accept response does not say whether an account exists. Only the Keycloak address differs.
3. **On the callback, the invitation SHALL be checked again,** because it may have changed during sign-in. Then:
   - **Email match:** the signed-in user's email must match the invited email, ignoring case. A mismatch returns 403 `INVITATION_EMAIL_MISMATCH`, and the message names the invited address so the user can sign in with it.
     - **The invitation link counts as proof of the email** (decided). The link was sent to that address, so a matching email is accepted even when Keycloak has not marked it verified. A new user in a realm with "Verify email" switched off therefore gets no second email.
     - A new setting `auth.invitations.require-verified-email` (System level, default false) can also require Keycloak's `email_verified`. An unverified email then returns 403 `EMAIL_NOT_VERIFIED`, telling the user to verify their email first.
     - This setting is only for applications that do not accept the link as proof. Sign-ins without an invitation are not affected.
   - **Re-checked grant:** the invitation's creator must still be allowed to grant the role (decided). If not, the result is 403 `INVITATION_NO_LONGER_VALID`, and the invitation is left Pending for an admin to revoke or resend.
   - **Claim:** the invitation's pending user is linked to the signed-in identity, or merged into the signed-in user if that is a different existing user (Requirement 2.5).
   - **Activate:** the membership becomes Active with the roles it already holds. The invitation is marked Accepted.
   - **Already an active member** (for example, they joined through a join link meanwhile): the user is signed in to that tenant, the response says they were already a member, the invitation is marked Accepted so it can't be reused, and **no role changes** (decided).
4. **Single use:** two concurrent acceptances of one invitation result in exactly one acceptance. The other gets 410 `INVITATION_ALREADY_USED`.
5. **Result:** a tenant-scoped token with the identity provider's `auth_time` and the tenant session rules (absolute lifetime, `REAUTH_REQUIRED`). The flow never shows the tenant picker.
6. A revoked or expired invitation at callback time returns 410 `INVITATION_NO_LONGER_VALID`.

### Requirement 8: Invitation records

1. Invitations are soft-deleted, tenant-isolated, and audited (created/updated by and at).
2. Expired invitations are reported as Expired once their expiry passes, whether or not a background job has updated the stored status.

### Requirement 9: Join links

1. A join link belongs to one tenant and records:
   - the role to grant (required)
   - an optional expiry
   - an optional maximum number of uses
   - the use count
   - who created it
   - its status: Active or Revoked
2. Creating, regenerating and revoking one needs `auth.joinlinks.manage`, and the creator must be allowed to grant the role. The token is 256 random bits, stored only as a hash, and shown once on creation. **Regenerate** replaces the token, so the old link stops working. Admins can list and revoke links.
3. Look-up and starting the flow work as for invitations (Requirements 7.1 and 7.2). A join link has no email, so the invitee always gets Keycloak's sign-in page, which links to registration. An invitee already signed in to Keycloak goes straight back.
4. **On the callback, the link is checked again** (active, not expired, uses left, creator still allowed to grant the role). Then:
   - **New member, or a former member:** the membership is created or reactivated as Active (no pending step), the role is granted through `IUserRoleService` (source JoinLink), and the use count goes up by one, atomically. Concurrent uses can never exceed the maximum.
   - **Pending member** (they have an open invitation): the membership becomes Active, keeping its invitation roles, and also gets the join link's role. The invitation is marked Accepted. The use count goes up.
   - **Already an active member:** the user is signed in to that tenant, told they were already a member, and the use count and roles don't change.
5. A revoked, expired or used-up link returns 410 `JOIN_LINK_NO_LONGER_VALID`. Join links are soft-deleted, tenant-isolated and audited.

### Requirement 10: Permissions and endpoints

1. New permission keys SHALL be seeded (decided):

| Permission | Allows |
|---|---|
| `auth.roles.assign` | assign and remove roles other than SuperAdmin and TenantAdmin |
| `auth.members.read` | list members and their roles |
| `auth.members.manage` | remove members |
| `auth.invitations.send` | create, list, resend and revoke invitations |
| `auth.joinlinks.manage` | create, list, regenerate and revoke join links |

TenantAdmins (in their tenant) and SuperAdmins pass all of these.
2. Admin endpoints (member list, role changes, removal, invitation and join-link management) act on the caller's current tenant. Public endpoints (look-up, accept, join) take the token.
3. Controllers are thin. The new services enforce the same rules when called directly as an SDK. The existing startup check that refuses to start when the settings services are not wrapped by `AddAuthorized` (`SettingsAuthorizationStartupValidator`) SHALL cover the new services too.
4. Creating an invitation or join link for an enterprise tenant returns 422 `ENTERPRISE_NOT_SUPPORTED` until GU-69.

### Requirement 11: Tests and checks

1. **Unit tests** for every rule in Requirements 3 and 4, including a TenantAdmin trying to grant SuperAdmin, a holder of `auth.roles.assign` trying to grant TenantAdmin, and a revoked invitation leaving a Manual grant of the same role in place.
2. **Integration tests against real Postgres:**
   - the migration on existing data (status and source mapping, nullable external id)
   - pending members excluded from the picker, permission checks, `IsActiveMemberAsync` and the last-admin guard
   - claim at sign-in: via invitation, via verified email, and no claim for an unverified email without an invitation
   - merging a pending user into an existing user, with the event
   - both flows: new user, existing user, former member, pending member using a join link, already-a-member
   - `ALREADY_MEMBER` and `INVITATION_PENDING` at creation
   - wrong email, an unverified email with the setting off (accepted) and on (refused)
   - registration vs sign-in page chosen from the Keycloak look-up, and the forced sign-in for a session with a different email
   - revoked, expired and used invitations or links
   - a creator who lost the right to grant
   - concurrent acceptance and concurrent last use of a join link
   - signed-out and tenantless sessions
   - enterprise tenants refused
3. **Event and revocation tests:** role changes publish the events and evict the cache; removing a role revokes the tenant's existing tokens.
4. **`realm.json`:** check during manual testing how the dev `groundup-app` client gets valid redirect URIs. Fix `keycloak/realm.json` if its `localhost:*` patterns are what is used (they don't work).
5. **CI-ready, as in GU-73:** isolated data, pinned images, no fixed ports, and no sleeps to wait for state.
6. The manual testing guide gains an invitations and join links part.
