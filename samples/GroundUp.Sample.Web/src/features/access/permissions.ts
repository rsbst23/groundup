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

/**
 * "You need the permission auth.members.read to see this page." (or "the permissions a, b"; all of
 * them are needed). Shows the keys until permissions have labels (GU-99).
 */
export function permissionRequiredText(keys: string[], purpose?: string): string {
  const what = keys.length === 1 ? `the permission ${keys[0]}` : `the permissions ${keys.join(', ')}`;
  return `You need ${what}${purpose ? ` to ${purpose}` : ''}.`;
}

/**
 * The text for a refusal of the API's authorization proxy (GU-98): `PERMISSION_REQUIRED` lists the
 * missing keys (all needed), `ROLE_REQUIRED` the roles (any one is enough). Undefined for other errors.
 */
export function refusalText(error: { errorCode: string | null; errors: string[] }): string | undefined {
  if (error.errors.length === 0) {
    return undefined;
  }
  if (error.errorCode === 'PERMISSION_REQUIRED') {
    return permissionRequiredText(error.errors);
  }
  if (error.errorCode === 'ROLE_REQUIRED') {
    return error.errors.length === 1
      ? `You need the role ${error.errors[0]}.`
      : `You need one of the roles ${error.errors.join(', ')}.`;
  }
  return undefined;
}
