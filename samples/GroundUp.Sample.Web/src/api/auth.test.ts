import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createOrganization, leaveTenant, login, logout, refreshSession, setTenant, signup } from './auth';
import { clearCsrfToken } from './csrf';
import { clearInspector } from './inspector';
import { emptyResponse, jsonResponse, mockFetch } from './testing';

beforeEach(() => {
  clearCsrfToken();
  clearInspector();
});
afterEach(() => vi.unstubAllGlobals());

/** The API answers /auth/csrf before every first unsafe call; signed out it is 401 (no token). */
const signedOutCsrf = () => emptyResponse(401);

function call(mock: ReturnType<typeof mockFetch>, url: string) {
  const found = mock.mock.calls.find((c) => String(c[0]) === url);
  if (!found) {
    throw new Error(`No call to ${url}; calls were ${mock.mock.calls.map((c) => String(c[0])).join(', ')}`);
  }
  return found[1] as RequestInit;
}

describe('auth endpoints', () => {
  it('login sends the return URL, and forceLogin only when asked', async () => {
    const mock = mockFetch({
      '/auth/csrf': [signedOutCsrf, signedOutCsrf],
      '/auth/login?returnUrl=%2Fsession%3Fnext%3D%252Fmembers': [() => jsonResponse(200, { redirectUrl: 'https://kc/auth?a=1' })],
      '/auth/login?forceLogin=true&returnUrl=%2Fsession': [() => jsonResponse(200, { redirectUrl: 'https://kc/auth?b=2' })],
    });

    await expect(login({ returnUrl: '/session?next=%2Fmembers' })).resolves.toEqual({ redirectUrl: 'https://kc/auth?a=1' });
    clearCsrfToken();
    await expect(login({ returnUrl: '/session', forceLogin: true })).resolves.toEqual({ redirectUrl: 'https://kc/auth?b=2' });
    expect(call(mock, '/auth/login?forceLogin=true&returnUrl=%2Fsession').method).toBe('POST');
  });

  it('signup sends the organization name, or an empty body for a simple sign-up', async () => {
    const mock = mockFetch({
      // Signed out, no token is kept, so each unsafe call asks again
      '/auth/csrf': [signedOutCsrf, signedOutCsrf],
      '/auth/signup?returnUrl=%2Fsession': [
        () => jsonResponse(200, { redirectUrl: 'https://kc/one' }),
        () => jsonResponse(200, { redirectUrl: 'https://kc/two' }),
      ],
    });

    await signup({ returnUrl: '/session', organizationName: 'Acme' });
    await signup({ returnUrl: '/session' });

    const bodies = mock.mock.calls.filter((c) => String(c[0]).startsWith('/auth/signup')).map((c) => (c[1] as RequestInit).body);
    expect(bodies).toEqual(['{"organizationName":"Acme"}', '{}']);
  });

  it('setTenant posts the tenant id', async () => {
    const mock = mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 't' })],
      '/auth/set-tenant': [() => emptyResponse(200)],
    });

    await setTenant('tenant-1');

    expect(call(mock, '/auth/set-tenant').body).toBe('{"tenantId":"tenant-1"}');
  });

  it('refreshSession posts with no body', async () => {
    const mock = mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 't' })],
      '/auth/refresh': [() => emptyResponse(200)],
    });

    await refreshSession();

    expect(call(mock, '/auth/refresh').body).toBeUndefined();
  });

  it('createOrganization posts the name and returns the new organization', async () => {
    const mock = mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 't' })],
      '/auth/organizations': [() => jsonResponse(201, { id: 'o1', name: 'Second Org', slug: 'second-org' })],
    });

    await expect(createOrganization('Second Org')).resolves.toEqual({ id: 'o1', name: 'Second Org', slug: 'second-org' });
    expect(call(mock, '/auth/organizations').body).toBe('{"name":"Second Org"}');
  });

  it('leaveTenant posts to the tenant path and returns the new current tenant', async () => {
    mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 't' })],
      '/auth/tenants/t%201/leave': [() => jsonResponse(200, { success: true, currentTenantId: null })],
    });

    await expect(leaveTenant('t 1')).resolves.toEqual({ success: true, currentTenantId: null });
  });

  it('logout returns the Keycloak end-session address', async () => {
    mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 't' })],
      '/auth/logout': [() => jsonResponse(200, { logoutUrl: 'https://kc/logout' })],
    });

    await expect(logout()).resolves.toEqual({ logoutUrl: 'https://kc/logout' });
  });

  it('a refusal comes back as ApiError with its code', async () => {
    mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 't' })],
      '/auth/tenants/t1/leave': [() => jsonResponse(403, { error: 'You are the last admin.', errorCode: 'LAST_ADMIN' })],
    });

    await expect(leaveTenant('t1')).rejects.toMatchObject({ status: 403, errorCode: 'LAST_ADMIN' });
  });
});
