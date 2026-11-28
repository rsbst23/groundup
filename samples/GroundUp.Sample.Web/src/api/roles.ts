import { apiFetch } from './client';
import { pagedPath, type PaginatedData } from './paging';
import type { TenantRoleDto } from './types';

/** The API's largest page. A tenant has few roles; past this the picker shows the first ones by name. */
export const MAX_ROLES = 100;

/** GET /api/roles: the current tenant's roles by name, for role pickers. Needs auth.roles.read. */
export async function listRoles(signal?: AbortSignal): Promise<TenantRoleDto[]> {
  return (await apiFetch<PaginatedData<TenantRoleDto>>(pagedPath('/api/roles', 1, MAX_ROLES), { signal })).data.items;
}
