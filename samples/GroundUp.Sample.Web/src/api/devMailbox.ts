import { apiFetch } from './client';
import type { DevEmailDto } from './types';

/**
 * GET /api/dev/invitation-emails: the invitation emails the sample API kept in memory, newest first.
 * Only exists when the API runs in Development (404 otherwise). The links contain tokens.
 */
export async function listDevEmails(signal?: AbortSignal): Promise<DevEmailDto[]> {
  return (await apiFetch<DevEmailDto[]>('/api/dev/invitation-emails', { signal })).data ?? [];
}
