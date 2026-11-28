import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  createPolicy,
  deletePolicy,
  getPermissionCatalog,
  getPolicy,
  listPolicyPage,
  setPolicyPermissions,
  updatePolicy,
} from '../../api/policies';
import type { CreatePolicyRequest, PolicyDetailDto, TenantPolicyDto } from '../../api/types';
import { withoutPageItems } from '../list/cachedPages';
import type { ListState } from '../list/listSearch';
import { rolesKey } from '../members/membersQuery';
import { policiesKey } from '../roles/rolesQuery';
import { onSessionRefreshed } from '../session/sessionQuery';
import { keysChanged, sortedKeys } from './permissionChoices';

/** The permission catalogue: global and fixed while the app runs. */
export const permissionCatalogKey = ['permissions'] as const;

/** One view (page, sort, search) of the Policies table. */
export function policyPageQueryOptions(tenantId: string, state: ListState) {
  return queryOptions({
    queryKey: [...policiesKey, tenantId, 'page', state] as const,
    queryFn: ({ signal }) => listPolicyPage(state, signal),
    placeholderData: keepPreviousData,
  });
}

/** One policy with its permissions and roles (the edit dialog). */
export function policyQueryOptions(tenantId: string, policyId: string | null) {
  return queryOptions({
    queryKey: [...policiesKey, tenantId, 'policy', policyId] as const,
    queryFn: ({ signal }) => getPolicy(policyId!, signal),
    enabled: policyId !== null,
  });
}

/** The permission catalogue by module. */
export function permissionCatalogQueryOptions() {
  return queryOptions({
    queryKey: permissionCatalogKey,
    queryFn: ({ signal }) => getPermissionCatalog(signal),
    staleTime: Infinity,
  });
}

/** What the edit dialog saves: the policy as loaded, and the new values. */
export interface PolicyEdit {
  before: PolicyDetailDto;
  name: string;
  description: string | null;
  keys: ReadonlySet<string>;
}

/**
 * Saves an edit with only the calls needed: the name and description, then the permission set.
 * If the second call fails the first stays saved; the error says what failed.
 */
export async function savePolicyEdit(edit: PolicyEdit): Promise<void> {
  const { before } = edit;
  if (edit.name !== before.name || edit.description !== before.description) {
    await updatePolicy(before.id, { name: edit.name, description: edit.description });
  }
  if (keysChanged(before.permissions.map((p) => p.key), edit.keys)) {
    await setPolicyPermissions(before.id, sortedKeys(edit.keys));
  }
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
    // Reload even after a failure: the name may have been saved before the permissions failed
    save: useMutation({ mutationFn: savePolicyEdit, onSettled: reload }),
    remove: useMutation({
      mutationFn: (policy: TenantPolicyDto) => deletePolicy(policy.id),
      onSuccess: (_, policy) => {
        // Take the row out of every cached page at once, so it is gone before the reload
        queryClient.setQueriesData({ queryKey: policiesKey }, (cached: unknown) =>
          withoutPageItems<TenantPolicyDto>(cached, (p) => p.id === policy.id),
        );
        return reload();
      },
    }),
  };
}
