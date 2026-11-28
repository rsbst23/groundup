/**
 * The API sends enums as numbers. These are the values the pages show, in one place, with a
 * readable fallback for a value added to the API later.
 */

export const MembershipStatus = { Active: 0, Inactive: 1, Pending: 2 } as const;
export const RoleAssignmentSource = { Manual: 0, Invitation: 1, JoinLink: 2, DefaultRole: 3, Provisioning: 4 } as const;
export const InvitationStatus = { Pending: 0, Accepted: 1, Revoked: 2, Expired: 3 } as const;
export const JoinLinkStatus = { Active: 0, Revoked: 1, Expired: 2, UsedUp: 3 } as const;
/** System is the built-in TenantAdmin's type; new roles are Application or Workspace. */
export const RoleType = { System: 0, Application: 1, Workspace: 2 } as const;

function labeller(labels: Record<number, string>): (value: number) => string {
  return (value) => labels[value] ?? `Unknown (${value})`;
}

export const membershipStatusLabel = labeller({ 0: 'Active', 1: 'Inactive', 2: 'Pending' });
export const roleSourceLabel = labeller({ 0: 'Manual', 1: 'Invitation', 2: 'Join link', 3: 'Default role', 4: 'Provisioning' });
export const invitationStatusLabel = labeller({ 0: 'Pending', 1: 'Accepted', 2: 'Revoked', 3: 'Expired' });
export const joinLinkStatusLabel = labeller({ 0: 'Active', 1: 'Revoked', 2: 'Expired', 3: 'Used up' });
export const roleTypeLabel = labeller({ 0: 'Built-in', 1: 'Application', 2: 'Workspace' });
