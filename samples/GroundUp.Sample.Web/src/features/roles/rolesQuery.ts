import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { listAllPolicies } from '../../api/policies';
import {
  attachPolicy,
  clearDefaultRole,
  createRole,
  deleteRole,
  detachPolicy,
  getDefaultRole,
  getRole,
  getRolePermissions,
  getRolePolicies,
  listRolePage,
  setDefaultRole,
  updateRole,
} from '../../api/roles';
import type { CreateRoleRequest, PolicyDto, TenantPolicyDto, TenantRoleDto, UpdateRoleRequest } from '../../api/types';
import { isPage, withoutPageItems } from '../list/cachedPages';
import type { ListState } from '../list/listSearch';
import { rolesKey } from '../members/membersQuery';
import { onSessionRefreshed } from '../session/sessionQuery';

/** Every policies query key starts with this; the tenant is part of the key. */
export const policiesKey = ['policies'] as const;

// All role keys sit under ['roles', tenantId], so a change reaches the role pickers too.

/** One view (page, sort, search) of the Roles table. */
export function rolePageQueryOptions(tenantId: string, state: ListState) {
  return queryOptions({
    queryKey: [...rolesKey, tenantId, 'page', state] as const,
    queryFn: ({ signal }) => listRolePage(state, signal),
    placeholderData: keepPreviousData,
  });
}

/** The tenant's default role, or null. */
export function defaultRoleQueryOptions(tenantId: string) {
  return queryOptions({
    queryKey: [...rolesKey, tenantId, 'default'] as const,
    queryFn: ({ signal }) => getDefaultRole(signal),
  });
}

/** One role. */
export function roleQueryOptions(tenantId: string, roleId: string) {
  return queryOptions({
    queryKey: [...rolesKey, tenantId, 'role', roleId] as const,
    queryFn: ({ signal }) => getRole(roleId, signal),
  });
}

/** The role's attached policies. */
export function rolePoliciesQueryOptions(tenantId: string, roleId: string, enabled: boolean) {
  return queryOptions({
    queryKey: [...rolesKey, tenantId, 'role', roleId, 'policies'] as const,
    queryFn: ({ signal }) => getRolePolicies(roleId, signal),
    enabled,
  });
}

/** What the role grants, with the policies granting each permission. */
export function rolePermissionsQueryOptions(tenantId: string, roleId: string) {
  return queryOptions({
    queryKey: [...rolesKey, tenantId, 'role', roleId, 'permissions'] as const,
    queryFn: ({ signal }) => getRolePermissions(roleId, signal),
  });
}

/** The tenant's policies for the attach picker (up to 100). Only fetched when the user may read them. */
export function allPoliciesQueryOptions(tenantId: string, enabled: boolean) {
  return queryOptions({
    queryKey: [...policiesKey, tenantId, 'all'] as const,
    queryFn: ({ signal }) => listAllPolicies(signal),
    enabled,
  });
}

/** Whether a query key is one of a role's own queries (`['roles', tenantId, 'role', roleId, …]`). */
export function isRoleQuery(queryKey: readonly unknown[], roleId: string): boolean {
  return queryKey[0] === rolesKey[0] && queryKey[2] === 'role' && queryKey[3] === roleId;
}

/**
 * Cached role data without the deleted role: a page of the table (with its counts lowered) or a
 * picker's list. Anything else (a role, its policies or permissions) is returned unchanged.
 */
export function withoutRole(cached: unknown, roleId: string): unknown {
  if (Array.isArray(cached)) {
    return isRoleList(cached) ? cached.filter((role) => role.id !== roleId) : cached;
  }
  if (isPage(cached) && isRoleList(cached.items)) {
    return withoutPageItems<TenantRoleDto>(cached, (role) => role.id === roleId);
  }
  return cached;
}

function isRoleList(value: unknown[]): value is TenantRoleDto[] {
  return value.every((v) => typeof v === 'object' && v !== null && 'id' in v && 'isSystem' in v);
}

/** The policies not attached yet, by name. */
export function attachablePolicies(all: TenantPolicyDto[], attached: PolicyDto[]): TenantPolicyDto[] {
  const held = new Set(attached.map((p) => p.id));
  return all.filter((p) => !held.has(p.id)).sort((a, b) => a.name.localeCompare(b.name));
}

/**
 * The role actions. Each reloads the roles (lists, pickers, the role page) and the policies (their
 * role counts), and refreshes the session: the signed-in user's own permissions may have changed.
 */
export function useRoleActions() {
  const queryClient = useQueryClient();
  const reload = async () => {
    await queryClient.invalidateQueries({ queryKey: rolesKey });
    await queryClient.invalidateQueries({ queryKey: policiesKey });
    await onSessionRefreshed(queryClient);
  };
  return {
    create: useMutation({ mutationFn: (request: CreateRoleRequest) => createRole(request), onSuccess: reload }),
    update: useMutation({
      mutationFn: ({ roleId, request }: { roleId: string; request: UpdateRoleRequest }) => updateRole(roleId, request),
      onSuccess: reload,
    }),
    /**
     * `leave` runs after the delete and before the reload: the role's own page navigates away first,
     * so its queries are no longer shown when the reload would fetch them (a 404).
     */
    remove: useMutation({
      mutationFn: ({ roleId }: { roleId: string; leave?: () => Promise<unknown> }) => deleteRole(roleId),
      onSuccess: async (_, { roleId, leave }) => {
        // Take the row out of every cached list and picker at once, so it is gone before the reload
        queryClient.setQueriesData({ queryKey: rolesKey }, (cached: unknown) => withoutRole(cached, roleId));
        await leave?.();
        queryClient.removeQueries({ predicate: (query) => isRoleQuery(query.queryKey, roleId) });
        await reload();
      },
    }),
    setDefault: useMutation({ mutationFn: (roleId: string) => setDefaultRole(roleId), onSuccess: reload }),
    clearDefault: useMutation({ mutationFn: () => clearDefaultRole(), onSuccess: reload }),
    attach: useMutation({
      mutationFn: ({ roleId, policyId }: { roleId: string; policyId: string }) => attachPolicy(roleId, policyId),
      onSuccess: reload,
    }),
    detach: useMutation({
      mutationFn: ({ roleId, policyId }: { roleId: string; policyId: string }) => detachPolicy(roleId, policyId),
      onSuccess: reload,
    }),
  };
}
