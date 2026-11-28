import { apiFetch } from './client';
import { pagedPath, type PaginatedData } from './paging';
import type { TenantPolicyDto } from './types';

/** The API's largest page; the attach picker shows at most this many policies, by name. */
export const MAX_POLICIES = 100;

/** GET /api/policies: the current tenant's policies by name, for pickers. Needs auth.permissions.read. */
export async function listAllPolicies(signal?: AbortSignal): Promise<TenantPolicyDto[]> {
  return (await apiFetch<PaginatedData<TenantPolicyDto>>(pagedPath('/api/policies', 1, MAX_POLICIES), { signal })).data.items;
}
