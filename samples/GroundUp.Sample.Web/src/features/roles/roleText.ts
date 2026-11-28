import type { TenantRoleDto } from '../../api/types';

/** The API's limits (CreateRoleDtoValidator, UpdateRoleDtoValidator). */
export const ROLE_NAME_MAX = 200;
export const ROLE_DESCRIPTION_MAX = 1000;

/** The roles that can be the default: every role except the built-in TenantAdmin, by name. */
export function defaultRoleChoices(roles: TenantRoleDto[]): TenantRoleDto[] {
  return roles.filter((r) => !r.isSystem).sort((a, b) => a.name.localeCompare(b.name));
}

/** The Policies column: "All" for TenantAdmin (it needs none), otherwise the count. */
export function policiesText(role: TenantRoleDto): string {
  return role.isSystem ? 'All' : String(role.policyCount);
}

/** The default-role line above the table. */
export function defaultRoleText(role: TenantRoleDto | null | undefined): string {
  return role
    ? `Default role: ${role.name}. New members who join by signing in get it.`
    : 'No default role. New members who join by signing in get no role.';
}
