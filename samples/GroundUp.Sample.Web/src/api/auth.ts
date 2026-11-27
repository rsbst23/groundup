import { apiFetch } from './client';
import { ApiError } from './errors';
import type { MeDto, MyTenantDto } from './types';

/** The signed-in session, or null when nobody is signed in (401 is a normal state here, not an error). */
export async function getMe(signal?: AbortSignal): Promise<MeDto | null> {
  try {
    return (await apiFetch<MeDto>('/auth/me', { signal })).data;
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null;
    }
    throw error;
  }
}

/**
 * The signed-in user's organizations. Empty when the API answers 401, which it also does for an
 * identity-provider token on the tenant picker (that token is signed in but is not a GroundUp user yet).
 */
export async function getMyTenants(signal?: AbortSignal): Promise<MyTenantDto[]> {
  try {
    return (await apiFetch<MyTenantDto[]>('/auth/me/tenants', { signal })).data ?? [];
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return [];
    }
    throw error;
  }
}

/** POST /auth/refresh: a harmless cookie-authenticated POST, used by Home's CSRF check. */
export async function refreshSession(): Promise<unknown> {
  return (await apiFetch<unknown>('/auth/refresh', { method: 'POST' })).data;
}
