import { apiFetch } from './client';
import { ApiError } from './errors';
import type {
  CreatedOrganizationDto,
  LeaveTenantResultDto,
  LogoutResultDto,
  MeDto,
  MyTenantDto,
  SignInStartDto,
} from './types';

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

function withQuery(path: string, query: Record<string, string | undefined>): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined) {
      params.set(key, value);
    }
  }
  const text = params.toString();
  return text ? `${path}?${text}` : path;
}

/**
 * POST /auth/login. `returnUrl` is where the callback sends the browser afterwards (GU-87);
 * `forceLogin` makes Keycloak ask for the password even with a live session.
 */
export async function login(options: { returnUrl: string; forceLogin?: boolean }): Promise<SignInStartDto> {
  const path = withQuery('/auth/login', {
    forceLogin: options.forceLogin ? 'true' : undefined,
    returnUrl: options.returnUrl,
  });
  return (await apiFetch<SignInStartDto>(path, { method: 'POST' })).data;
}

/**
 * POST /auth/signup. With an organization name the user founds that organization; without one it
 * is a simple sign-up into the configured default tenant.
 */
export async function signup(options: { returnUrl: string; organizationName?: string }): Promise<SignInStartDto> {
  const path = withQuery('/auth/signup', { returnUrl: options.returnUrl });
  const body = options.organizationName ? { organizationName: options.organizationName } : {};
  return (await apiFetch<SignInStartDto>(path, { method: 'POST', body })).data;
}

/** POST /auth/set-tenant: makes the organization the session's current tenant (the cookie is replaced). */
export async function setTenant(tenantId: string): Promise<void> {
  await apiFetch<unknown>('/auth/set-tenant', { method: 'POST', body: { tenantId } });
}

/** POST /auth/refresh: issues a new token for the same session (the cookie is replaced). */
export async function refreshSession(): Promise<void> {
  await apiFetch<unknown>('/auth/refresh', { method: 'POST' });
}

/** POST /auth/organizations: creates an organization with the user as TenantAdmin; the session does not move. */
export async function createOrganization(name: string): Promise<CreatedOrganizationDto> {
  return (await apiFetch<CreatedOrganizationDto>('/auth/organizations', { method: 'POST', body: { name } })).data;
}

/** POST /auth/tenants/{id}/leave. Leaving the current tenant leaves the session with no current tenant. */
export async function leaveTenant(tenantId: string): Promise<LeaveTenantResultDto> {
  return (
    await apiFetch<LeaveTenantResultDto>(`/auth/tenants/${encodeURIComponent(tenantId)}/leave`, { method: 'POST' })
  ).data;
}

/** POST /auth/logout: clears the app's cookies and returns Keycloak's end-session address. */
export async function logout(): Promise<LogoutResultDto> {
  return (await apiFetch<LogoutResultDto>('/auth/logout', { method: 'POST' })).data;
}
