/** DTO types for the endpoints the app calls. Written by hand for now (generating them from the OpenAPI document is a later decision). */

/** GET /auth/me. Times are UTC ISO 8601 strings, or null when the token lacks the claim. */
export interface MeDto {
  /** The GroundUp user id, or the identity provider's subject for a token on the tenant picker. */
  userId: string;
  email: string | null;
  displayName: string | null;
  /** Null when the session has no current tenant. */
  tenantId: string | null;
  roles: string[];
  /** True for a SuperAdmin and for a TenantAdmin of the current tenant: they pass every permission check (GU-96). */
  allPermissions: boolean;
  /** The permission keys held in the current tenant, sorted. Empty when `allPermissions` is true. */
  permissions: string[];
  issuedAt: string | null;
  expiresAt: string | null;
  authTime: string | null;
}

/** One entry of GET /auth/me/tenants. Standard tenants carry `current`; enterprise tenants carry `subdomain`. */
export interface MyTenantDto {
  id: string;
  name: string;
  slug: string;
  type: 'standard' | 'enterprise' | string;
  switchable: boolean;
  current?: boolean;
  subdomain?: string | null;
}

/** POST /auth/login and /auth/signup: the Keycloak address to send the whole page to. */
export interface SignInStartDto {
  redirectUrl: string;
}

/** POST /auth/organizations (201). */
export interface CreatedOrganizationDto {
  id: string;
  name: string;
  slug: string;
}

/** POST /auth/tenants/{id}/leave. `currentTenantId` is null when the session no longer has a current tenant. */
export interface LeaveTenantResultDto {
  success: boolean;
  currentTenantId: string | null;
}

/** POST /auth/logout. */
export interface LogoutResultDto {
  logoutUrl: string;
}

// ── Members, roles, invitations, join links (GU-90) ─────────────────────────
// Enums are numbers on the wire; see labels.ts.

/** One role a member holds, with every reason it was granted (RoleAssignmentSource values). */
export interface MemberRoleDto {
  roleId: string;
  roleName: string;
  sources: number[];
}

/** One entry of GET /api/members. `status` is a MembershipStatus (Active or Pending in the list). */
export interface TenantMemberDto {
  userId: string;
  email: string;
  displayName: string | null;
  status: number;
  roles: MemberRoleDto[];
}

/** POST /api/members/{userId}/roles/{roleId} (201). */
export interface UserRoleDto {
  id: string;
  userId: string;
  roleId: string;
  tenantId: string;
  roleName: string | null;
  source: number;
}

/** One entry of GET /api/roles (GU-95). */
export interface TenantRoleDto {
  id: string;
  name: string;
  description: string | null;
  roleType: number;
  tenantId: string;
  isSystem: boolean;
  isDefault: boolean;
  /** How many policies are attached (GU-97); 0 for TenantAdmin, which needs none. */
  policyCount: number;
}

/** A role–policy link (attach). */
export interface RolePolicyLink {
  id: string;
  roleId: string;
  policyId: string;
}

/** POST /api/roles. `roleType` is a RoleType (Application by default; System is refused). */
export interface CreateRoleRequest {
  name: string;
  description?: string | null;
  roleType?: number;
}

/** PUT /api/roles/{id}. TenantAdmin's name cannot change (its description can). */
export interface UpdateRoleRequest {
  name: string;
  description?: string | null;
}

/** A policy as the role's policy list returns it. */
export interface PolicyDto {
  id: string;
  name: string;
  description: string | null;
  tenantId: string;
}

/** A policy in the policies list: its permission keys and how many roles use it. */
export interface TenantPolicyDto {
  id: string;
  name: string;
  description: string | null;
  permissionKeys: string[];
  roleCount: number;
}

/** GET /api/policies/{id}: the policy with its permissions and the roles using it. */
export interface PolicyDetailDto {
  id: string;
  name: string;
  description: string | null;
  permissions: PermissionDto[];
  roles: EntityRefDto[];
}

/** POST /api/policies. Keys must be in the catalogue; System-only keys are refused. */
export interface CreatePolicyRequest {
  name: string;
  description?: string | null;
  permissionKeys?: string[];
}

/** PUT /api/policies/{id}: name and description only (the permissions have their own call). */
export interface UpdatePolicyRequest {
  name: string;
  description?: string | null;
}

/** An id and a name, for links (the roles using a policy, the policies granting a permission). */
export interface EntityRefDto {
  id: string;
  name: string;
}

/** A permission of the catalogue. `key` is what the API checks; `name` is for people. */
export interface PermissionDto {
  id: string;
  key: string;
  name: string;
  description: string | null;
  module: string;
}

/** GET /api/permissions: the catalogue by module. */
export interface PermissionModuleDto {
  module: string;
  permissions: PermissionDto[];
}

/** A permission a role grants and the policies granting it. */
export interface RolePermissionDto {
  key: string;
  name: string;
  module: string;
  grantedBy: EntityRefDto[];
}

/** GET /api/roles/{id}/permissions. `allPermissions` is true for TenantAdmin (no list then). */
export interface RolePermissionsDto {
  roleId: string;
  allPermissions: boolean;
  permissions: RolePermissionDto[];
}

/** An invitation (GU-68). The token is never returned; the link is only in the email. `status` is an InvitationStatus. */
export interface InvitationDto {
  id: string;
  tenantId: string;
  email: string;
  roleId: string;
  roleName: string | null;
  userId: string;
  createdByUserId: string;
  expiresAt: string;
  status: number;
  acceptedByUserId: string | null;
  acceptedAt: string | null;
  revokedAt: string | null;
  createdAt: string;
}

/** POST /api/invitations. `expiresAt` (UTC) can only shorten the configured lifetime. */
export interface CreateInvitationRequest {
  email: string;
  roleId: string;
  expiresAt?: string;
}

/** A join link (GU-68). `status` is a JoinLinkStatus; `maxUses` null means unlimited. */
export interface JoinLinkDto {
  id: string;
  tenantId: string;
  roleId: string;
  roleName: string | null;
  createdByUserId: string;
  expiresAt: string | null;
  maxUses: number | null;
  useCount: number;
  status: number;
  revokedAt: string | null;
  createdAt: string;
}

/** POST /api/join-links and POST /api/join-links/{id}/regenerate: the token is shown only here. */
export interface CreatedJoinLinkDto {
  link: JoinLinkDto;
  token: string;
}

/** POST /api/join-links. */
export interface CreateJoinLinkRequest {
  roleId: string;
  expiresAt?: string;
  maxUses?: number;
}

/**
 * POST /auth/invitations/lookup and /auth/join-links/lookup. When not usable, `reason` is one of
 * not_found, revoked, expired, accepted (invitations) or used_up (join links).
 */
export interface TokenLookupDto {
  usable: boolean;
  reason: string | null;
  organizationName: string | null;
  roleName: string | null;
}

/** One email kept by the sample's Development mailbox (GET /api/dev/invitation-emails). */
export interface DevEmailDto {
  invitationId: string;
  to: string;
  organizationName: string;
  roleName: string;
  acceptUrl: string;
  expiresAt: string;
  sentAt: string;
}
