import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  createPolicy,
  deletePolicy,
  getPermissionModules,
  getPolicy,
  listPermissionCatalog,
  listPolicyPage,
  setPolicyPermissions,
  updatePolicy,
  type CatalogState,
} from '../../api/policies';
import type { CreatePolicyRequest, TenantPolicyDto, UpdatePolicyRequest } from '../../api/types';
import { withoutPageItems } from '../list/cachedPages';
import type { ListState } from '../list/listSearch';
import { rolesKey } from '../members/membersQuery';
import { policiesKey } from '../roles/rolesQuery';
import { onSessionRefreshed } from '../session/sessionQuery';

/** The permission catalogue: global (not per tenant) and read-only from a tenant. */
export const permissionCatalogKey = ['permissions'] as const;

/** One view (page, sort, search) of the Policies table. */
export function policyPageQueryOptions(tenantId: string, state: ListState) {
  return queryOptions({
    queryKey: [...policiesKey, tenantId, 'page', state] as const,
    queryFn: ({ signal }) => listPolicyPage(state, signal),
    placeholderData: keepPreviousData,
  });
}

/** One policy with its permissions and roles (the editor page). */
export function policyQueryOptions(tenantId: string, policyId: string) {
  return queryOptions({
    queryKey: [...policiesKey, tenantId, 'policy', policyId] as const,
    queryFn: ({ signal }) => getPolicy(policyId, signal),
  });
}

/** One view (page, sort, search, module) of the permission catalogue. */
export function permissionCatalogQueryOptions(state: CatalogState) {
  return queryOptions({
    queryKey: [...permissionCatalogKey, 'catalog', state] as const,
    queryFn: ({ signal }) => listPermissionCatalog(state, signal),
    placeholderData: keepPreviousData,
  });
}

/** The catalogue's modules, for the filter; fixed while the app runs. */
export function permissionModulesQueryOptions() {
  return queryOptions({
    queryKey: [...permissionCatalogKey, 'modules'] as const,
    queryFn: ({ signal }) => getPermissionModules(signal),
    staleTime: Infinity,
  });
}

/**
 * The policy actions. Each reloads the policies and the roles (their permissions and counts) and
 * refreshes the session: the signed-in user's own permissions may have changed.
 */
export function usePolicyActions() {
  const queryClient = useQueryClient();
  const reload = async () => {
    await queryClient.invalidateQueries({ queryKey: policiesKey });
    await queryClient.invalidateQueries({ queryKey: rolesKey });
    await onSessionRefreshed(queryClient);
  };
  return {
    create: useMutation({ mutationFn: (request: CreatePolicyRequest) => createPolicy(request), onSuccess: reload }),
    update: useMutation({
      mutationFn: ({ policyId, request }: { policyId: string; request: UpdatePolicyRequest }) => updatePolicy(policyId, request),
      onSuccess: reload,
    }),
    setPermissions: useMutation({
      mutationFn: ({ policyId, keys }: { policyId: string; keys: string[] }) => setPolicyPermissions(policyId, keys),
      onSuccess: reload,
    }),
    /** `leave` runs after the delete and before the reload, like deleting a role from its page. */
    remove: useMutation({
      mutationFn: ({ policy }: { policy: TenantPolicyDto | { id: string }; leave?: () => Promise<unknown> }) =>
        deletePolicy(policy.id),
      onSuccess: async (_, { policy, leave }) => {
        // Take the row out of every cached page at once, so it is gone before the reload
        queryClient.setQueriesData({ queryKey: policiesKey }, (cached: unknown) =>
          withoutPageItems<TenantPolicyDto>(cached, (p) => p.id === policy.id),
        );
        await leave?.();
        queryClient.removeQueries({ queryKey: [...policiesKey], predicate: (q) => q.queryKey[2] === 'policy' && q.queryKey[3] === policy.id });
        await reload();
      },
    }),
  };
}
