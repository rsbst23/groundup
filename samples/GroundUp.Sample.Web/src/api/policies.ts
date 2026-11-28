import type { ListState } from '../features/list/listSearch';
import { apiFetch } from './client';
import { listPath, pagedPath, type PaginatedData } from './paging';
import { segment } from './query';
import type { CreatePolicyRequest, PermissionDto, PolicyDetailDto, TenantPolicyDto, UpdatePolicyRequest } from './types';

/** The API's largest page; the attach picker shows at most this many policies, by name. */
export const MAX_POLICIES = 100;

/** GET /api/policies: the current tenant's policies by name, for pickers. Needs auth.permissions.read. */
export async function listAllPolicies(signal?: AbortSignal): Promise<TenantPolicyDto[]> {
  return (await apiFetch<PaginatedData<TenantPolicyDto>>(pagedPath('/api/policies', 1, MAX_POLICIES), { signal })).data.items;
}

/**
 * GET /api/policies for the Policies table (GU-97): by name unless sorted. Sort: name, roleCount,
 * permissionCount; search: name and description. Needs auth.permissions.read.
 */
export async function listPolicyPage(state: ListState, signal?: AbortSignal): Promise<PaginatedData<TenantPolicyDto>> {
  return (await apiFetch<PaginatedData<TenantPolicyDto>>(listPath('/api/policies', state), { signal })).data;
}

/** GET /api/policies/{id}: the permissions and the roles using it. */
export async function getPolicy(policyId: string, signal?: AbortSignal): Promise<PolicyDetailDto> {
  return (await apiFetch<PolicyDetailDto>(`/api/policies/${segment(policyId)}`, { signal })).data;
}

/** POST /api/policies. Needs auth.permissions.write; 409 POLICY_NAME_TAKEN; 403 PERMISSION_NOT_HELD. */
export async function createPolicy(request: CreatePolicyRequest): Promise<PolicyDetailDto> {
  return (await apiFetch<PolicyDetailDto>('/api/policies', { method: 'POST', body: request })).data;
}

/** PUT /api/policies/{id}: name and description. */
export async function updatePolicy(policyId: string, request: UpdatePolicyRequest): Promise<PolicyDetailDto> {
  return (await apiFetch<PolicyDetailDto>(`/api/policies/${segment(policyId)}`, { method: 'PUT', body: request })).data;
}

/** PUT /api/policies/{id}/permissions: replaces the set. 403 PERMISSION_NOT_HELD past the escalation guard. */
export async function setPolicyPermissions(policyId: string, permissionKeys: string[]): Promise<PolicyDetailDto> {
  return (
    await apiFetch<PolicyDetailDto>(`/api/policies/${segment(policyId)}/permissions`, { method: 'PUT', body: { permissionKeys } })
  ).data;
}

/** DELETE /api/policies/{id}. 409 POLICY_IN_USE with the roles in `errors`. */
export async function deletePolicy(policyId: string): Promise<void> {
  await apiFetch<unknown>(`/api/policies/${segment(policyId)}`, { method: 'DELETE' });
}

/** A view of the permission catalogue: the list view plus a module filter. */
export interface CatalogState extends ListState {
  module?: string;
}

/**
 * GET /api/permissions (GU-97d): a page of the catalogue, by module then key unless sorted. Sort: name,
 * key, module; filter: module; search: name, key, description. System-only keys are never listed.
 */
export async function listPermissionCatalog(state: CatalogState, signal?: AbortSignal): Promise<PaginatedData<PermissionDto>> {
  const base = listPath('/api/permissions', { page: state.page, sort: state.sort, q: state.q });
  const path = state.module ? `${base}&${new URLSearchParams({ 'filters[module]': state.module })}` : base;
  return (await apiFetch<PaginatedData<PermissionDto>>(path, { signal })).data;
}

/** GET /api/permissions/modules: the catalogue's modules, sorted. */
export async function getPermissionModules(signal?: AbortSignal): Promise<string[]> {
  return (await apiFetch<string[]>('/api/permissions/modules', { signal })).data;
}
