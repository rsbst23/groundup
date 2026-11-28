import { withoutPageItems } from '../list/cachedPages';
import type { ListState } from '../list/listSearch';
import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { RoleAssignmentSource, roleSourceLabel } from '../../api/labels';
import { addMemberRole, listMembers, removeMember, removeMemberRole } from '../../api/members';
import { listRoles } from '../../api/roles';
import type { MemberRoleDto, TenantMemberDto, TenantRoleDto } from '../../api/types';
import { onSessionChanged, onSessionRefreshed } from '../session/sessionQuery';

/** Every members query key starts with this. The tenant is part of the key, so a switch never shows the old rows. */
export const membersKey = ['members'] as const;
export const rolesKey = ['roles'] as const;

/** One view (page, sort, search, status) of the current tenant's members; each view is cached separately. */
export function membersQueryOptions(tenantId: string, state: ListState) {
  return queryOptions({
    queryKey: [...membersKey, tenantId, state] as const,
    queryFn: ({ signal }) => listMembers(state, signal),
    // Keep the current rows on screen while the next page loads
    placeholderData: keepPreviousData,
  });
}

/** The tenant's roles, for the role picker. Only fetched when the user may read roles. */
export function rolesQueryOptions(tenantId: string, enabled: boolean) {
  return queryOptions({
    queryKey: [...rolesKey, tenantId] as const,
    queryFn: ({ signal }) => listRoles(signal),
    enabled,
  });
}

/**
 * A role chip's text: the name, plus why it is held when that is not just a manual grant
 * ("Editor · Invitation, Manual").
 */
export function roleChipLabel(role: MemberRoleDto): string {
  const onlyManual = role.sources.length === 1 && role.sources[0] === RoleAssignmentSource.Manual;
  if (role.sources.length === 0 || onlyManual) {
    return role.roleName;
  }
  return `${role.roleName} · ${role.sources.map(roleSourceLabel).join(', ')}`;
}

/** The roles the member does not hold yet, by name. */
export function rolesToAdd(roles: TenantRoleDto[], member: TenantMemberDto): TenantRoleDto[] {
  const held = new Set(member.roles.map((r) => r.roleId));
  return roles.filter((r) => !held.has(r.id)).sort((a, b) => a.name.localeCompare(b.name));
}

/** The member's display name, or the email when there is none. */
export function memberName(member: TenantMemberDto): string {
  return member.displayName?.trim() || member.email;
}

/**
 * The member actions. Each reloads the list. A change to the signed-in user's own access also
 * reloads the session: removing yourself ends your access to the tenant (everything is reset); a
 * role change changes your permissions (the session reloads them).
 */
export function useMemberActions(currentUserId: string | null) {
  const queryClient = useQueryClient();
  const afterChange = async (userId: string, endsAccess: boolean) => {
    if (userId === currentUserId) {
      await (endsAccess ? onSessionChanged(queryClient) : onSessionRefreshed(queryClient));
    }
    await queryClient.invalidateQueries({ queryKey: membersKey });
  };

  return {
    removeMember: useMutation({
      mutationFn: (member: TenantMemberDto) => removeMember(member.userId),
      onSuccess: (_, member) => {
        // Take the row out of every cached page at once, so it is gone before the reload
        queryClient.setQueriesData({ queryKey: membersKey }, (cached: unknown) =>
          withoutPageItems<TenantMemberDto>(cached, (m) => m.userId === member.userId),
        );
        return afterChange(member.userId, true);
      },
    }),
    addRole: useMutation({
      mutationFn: ({ member, role }: { member: TenantMemberDto; role: TenantRoleDto }) => addMemberRole(member.userId, role.id),
      onSuccess: (_, { member }) => afterChange(member.userId, false),
    }),
    removeRole: useMutation({
      mutationFn: ({ member, role }: { member: TenantMemberDto; role: MemberRoleDto }) =>
        removeMemberRole(member.userId, role.roleId),
      onSuccess: (_, { member }) => afterChange(member.userId, false),
    }),
  };
}
