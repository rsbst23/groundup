import { apiFetch } from './client';
import type { ListState } from '../features/list/listSearch';
import { listPath, type PaginatedData } from './paging';
import { segment } from './query';
import type { CreateInvitationRequest, InvitationDto } from './types';

/**
 * GET /api/invitations: the current tenant's invitations (newest first unless sorted). Sort: email,
 * roleName, status, expiresAt, createdAt; filter: status (as shown); search: email. Needs auth.invitations.send.
 */
export async function listInvitations(state: ListState, signal?: AbortSignal): Promise<PaginatedData<InvitationDto>> {
  return (await apiFetch<PaginatedData<InvitationDto>>(listPath('/api/invitations', state), { signal })).data;
}

/** POST /api/invitations: creates the invitation and emails its link (the link is never returned). */
export async function createInvitation(request: CreateInvitationRequest): Promise<InvitationDto> {
  return (await apiFetch<InvitationDto>('/api/invitations', { method: 'POST', body: request })).data;
}

/** POST /api/invitations/{id}/resend: a new link (the old one stops working) and a new expiry. */
export async function resendInvitation(id: string): Promise<InvitationDto> {
  return (await apiFetch<InvitationDto>(`/api/invitations/${segment(id)}/resend`, { method: 'POST' })).data;
}

/** POST /api/invitations/{id}/revoke: the link stops working and the waiting membership is removed. */
export async function revokeInvitation(id: string): Promise<void> {
  await apiFetch<unknown>(`/api/invitations/${segment(id)}/revoke`, { method: 'POST' });
}
