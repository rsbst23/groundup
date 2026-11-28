import type { MeDto } from '../../api/types';
import { useSession } from '../session/sessionQuery';

/** The permission keys the pages check, as in the API's PermissionKeysAuth. */
export const PermissionKeys = {
  MembersRead: 'auth.members.read',
  MembersManage: 'auth.members.manage',
  RolesRead: 'auth.roles.read',
  RolesAssign: 'auth.roles.assign',
  InvitationsSend: 'auth.invitations.send',
  JoinLinksManage: 'auth.joinlinks.manage',
} as const;

export type PermissionKey = (typeof PermissionKeys)[keyof typeof PermissionKeys];

/**
 * Whether the session holds the permission in its current tenant. Only for showing or disabling
 * actions: the API checks every request and has rules of its own (the last admin, who may grant
 * TenantAdmin, the escalation guard).
 */
export function can(me: MeDto | null | undefined, key: string): boolean {
  if (!me) {
    return false;
  }
  return me.allPermissions === true || (me.permissions ?? []).includes(key);
}

/** `can` for the signed-in session. */
export function useCan(key: string): boolean {
  return can(useSession().me, key);
}

/** The tooltip for an action disabled by a missing permission. */
export function missingPermissionText(key: string): string {
  return `Needs the permission ${key}`;
}
