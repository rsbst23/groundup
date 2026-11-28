import { apiFetch } from './client';
import { pagedPath, type PaginatedData } from './paging';
import { segment } from './query';
import type { TenantMemberDto, UserRoleDto } from './types';

/** GET /api/members: the current tenant's Active and Pending members, by email. Needs auth.members.read. */
export async function listMembers(page: number, signal?: AbortSignal): Promise<PaginatedData<TenantMemberDto>> {
  return (await apiFetch<PaginatedData<TenantMemberDto>>(pagedPath('/api/members', page), { signal })).data;
}

/**
 * DELETE /api/members/{userId}: removes the member and all their roles; their sessions for the
 * tenant end. Needs auth.members.manage and the right to remove each role they hold.
 */
export async function removeMember(userId: string): Promise<void> {
  await apiFetch<unknown>(`/api/members/${segment(userId)}`, { method: 'DELETE' });
}

/** POST /api/members/{userId}/roles/{roleId}: grants the role (source Manual). */
export async function addMemberRole(userId: string, roleId: string): Promise<UserRoleDto> {
  return (await apiFetch<UserRoleDto>(`/api/members/${segment(userId)}/roles/${segment(roleId)}`, { method: 'POST' })).data;
}

/** DELETE /api/members/{userId}/roles/{roleId}: removes the role, whatever its sources. */
export async function removeMemberRole(userId: string, roleId: string): Promise<void> {
  await apiFetch<unknown>(`/api/members/${segment(userId)}/roles/${segment(roleId)}`, { method: 'DELETE' });
}
