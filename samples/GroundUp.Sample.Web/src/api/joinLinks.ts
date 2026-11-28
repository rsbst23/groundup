import { apiFetch } from './client';
import type { ListState } from '../features/list/listSearch';
import { listPath, type PaginatedData } from './paging';
import { segment } from './query';
import type { CreatedJoinLinkDto, CreateJoinLinkRequest, JoinLinkDto } from './types';

/**
 * GET /api/join-links: the current tenant's join links (newest first unless sorted). Sort: roleName,
 * status, useCount, expiresAt, createdAt; filter: status (as shown); search: role name. Needs auth.joinlinks.manage.
 */
export async function listJoinLinks(state: ListState, signal?: AbortSignal): Promise<PaginatedData<JoinLinkDto>> {
  return (await apiFetch<PaginatedData<JoinLinkDto>>(listPath('/api/join-links', state), { signal })).data;
}

/** POST /api/join-links: the response's token is shown only now. */
export async function createJoinLink(request: CreateJoinLinkRequest): Promise<CreatedJoinLinkDto> {
  return (await apiFetch<CreatedJoinLinkDto>('/api/join-links', { method: 'POST', body: request })).data;
}

/** POST /api/join-links/{id}/regenerate: a new token; the old link stops working. */
export async function regenerateJoinLink(id: string): Promise<CreatedJoinLinkDto> {
  return (await apiFetch<CreatedJoinLinkDto>(`/api/join-links/${segment(id)}/regenerate`, { method: 'POST' })).data;
}

/** POST /api/join-links/{id}/revoke: the link stops working; people who joined stay. */
export async function revokeJoinLink(id: string): Promise<void> {
  await apiFetch<unknown>(`/api/join-links/${segment(id)}/revoke`, { method: 'POST' });
}
