import type { ListState } from '../features/list/listSearch';
import { apiFetch } from './client';
import { listPath, pagedPath, type PaginatedData } from './paging';
import { segment } from './query';
import type { CreateRoleRequest, PolicyDto, RolePermissionsDto, RolePolicyLink, TenantMemberDto, TenantRoleDto, UpdateRoleRequest } from './types';

/** The API's largest page. A tenant has few roles; past this the picker shows the first ones by name. */
export const MAX_ROLES = 100;

/** GET /api/roles: the current tenant's roles by name, for role pickers. Needs auth.roles.read. */
export async function listRoles(signal?: AbortSignal): Promise<TenantRoleDto[]> {
  return (await apiFetch<PaginatedData<TenantRoleDto>>(pagedPath('/api/roles', 1, MAX_ROLES), { signal })).data.items;
}

/**
 * GET /api/roles for the Roles table (GU-97): by name unless sorted. Sort: name, roleType,
 * policyCount; search: name and description. Needs auth.roles.read.
 */
export async function listRolePage(state: ListState, signal?: AbortSignal): Promise<PaginatedData<TenantRoleDto>> {
  return (await apiFetch<PaginatedData<TenantRoleDto>>(listPath('/api/roles', state), { signal })).data;
}

/** GET /api/roles/{id}. 404 for a role of another tenant. */
export async function getRole(roleId: string, signal?: AbortSignal): Promise<TenantRoleDto> {
  return (await apiFetch<TenantRoleDto>(`/api/roles/${segment(roleId)}`, { signal })).data;
}

/** POST /api/roles. Needs auth.roles.write; 409 ROLE_NAME_TAKEN when the name is used (ignoring case). */
export async function createRole(request: CreateRoleRequest): Promise<TenantRoleDto> {
  return (await apiFetch<TenantRoleDto>('/api/roles', { method: 'POST', body: request })).data;
}

/** PUT /api/roles/{id}: name and description. 409 ROLE_IS_BUILT_IN when TenantAdmin's name changes. */
export async function updateRole(roleId: string, request: UpdateRoleRequest): Promise<TenantRoleDto> {
  return (await apiFetch<TenantRoleDto>(`/api/roles/${segment(roleId)}`, { method: 'PUT', body: request })).data;
}

/** DELETE /api/roles/{id}. Needs auth.roles.delete; 409 ROLE_IN_USE with the reasons in `errors`. */
export async function deleteRole(roleId: string): Promise<void> {
  await apiFetch<unknown>(`/api/roles/${segment(roleId)}`, { method: 'DELETE' });
}

/** GET /api/roles/default: the tenant's default role, or null when there is none. */
export async function getDefaultRole(signal?: AbortSignal): Promise<TenantRoleDto | null> {
  return (await apiFetch<TenantRoleDto | null>('/api/roles/default', { signal })).data ?? null;
}

/** PUT /api/roles/{id}/default. Needs auth.roles.write and the right to grant the role. */
export async function setDefaultRole(roleId: string): Promise<TenantRoleDto> {
  return (await apiFetch<TenantRoleDto>(`/api/roles/${segment(roleId)}/default`, { method: 'PUT' })).data;
}

/** DELETE /api/roles/default: new members get no role. */
export async function clearDefaultRole(): Promise<void> {
  await apiFetch<unknown>('/api/roles/default', { method: 'DELETE' });
}

/** GET /api/roles/{id}/policies. Needs auth.roles.read. */
export async function getRolePolicies(roleId: string, signal?: AbortSignal): Promise<PolicyDto[]> {
  return (await apiFetch<PolicyDto[]>(`/api/roles/${segment(roleId)}/policies`, { signal })).data;
}

/** POST /api/roles/{id}/policies/{policyId}. Needs auth.roles.write; 403 PERMISSION_NOT_HELD past the escalation guard. */
export async function attachPolicy(roleId: string, policyId: string): Promise<RolePolicyLink> {
  return (await apiFetch<RolePolicyLink>(`/api/roles/${segment(roleId)}/policies/${segment(policyId)}`, { method: 'POST' })).data;
}

/** DELETE /api/roles/{id}/policies/{policyId}. */
export async function detachPolicy(roleId: string, policyId: string): Promise<void> {
  await apiFetch<unknown>(`/api/roles/${segment(roleId)}/policies/${segment(policyId)}`, { method: 'DELETE' });
}

/**
 * GET /api/roles/{id}/members (GU-97d): the Active and Pending members holding the role, with the members
 * list's fields (sort email, displayName, status; filter status; search). Needs auth.members.read and auth.roles.read.
 */
export async function listRoleMembers(roleId: string, state: ListState, signal?: AbortSignal): Promise<PaginatedData<TenantMemberDto>> {
  return (await apiFetch<PaginatedData<TenantMemberDto>>(listPath(`/api/roles/${segment(roleId)}/members`, state), { signal })).data;
}

/** GET /api/roles/{id}/permissions: each permission with the policies granting it, or allPermissions. */
export async function getRolePermissions(roleId: string, signal?: AbortSignal): Promise<RolePermissionsDto> {
  return (await apiFetch<RolePermissionsDto>(`/api/roles/${segment(roleId)}/permissions`, { signal })).data;
}
