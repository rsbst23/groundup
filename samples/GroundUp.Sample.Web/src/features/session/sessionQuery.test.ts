import { QueryObserver } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch } from '../../api/client';
import { clearCsrfToken } from '../../api/csrf';
import { ApiError } from '../../api/errors';
import { clearInspector } from '../../api/inspector';
import { emptyResponse, headersOf, jsonResponse, mockFetch } from '../../api/testing';
import { queryClient, shouldRetry } from '../../queryClient';
import { formatRemaining } from './formatRemaining';
import { myTenantsQueryOptions, onSessionChanged, sessionKey, sessionQueryOptions } from './sessionQuery';

beforeEach(() => {
  queryClient.clear();
  clearCsrfToken();
  clearInspector();
});
afterEach(() => vi.unstubAllGlobals());

const me = { userId: 'u1', email: 'alice@example.com', displayName: 'Alice', tenantId: 't1', roles: ['TenantAdmin'], issuedAt: null, expiresAt: null, authTime: null };

describe('session queries', () => {
  it('gives null for a 401 from /auth/me (signed out is not an error)', async () => {
    mockFetch({ '/auth/me': [() => emptyResponse(401)] });

    await expect(queryClient.fetchQuery(sessionQueryOptions)).resolves.toBeNull();
  });

  it('gives the session for a 200', async () => {
    mockFetch({ '/auth/me': [() => jsonResponse(200, me)] });

    await expect(queryClient.fetchQuery(sessionQueryOptions)).resolves.toEqual(me);
  });

  it('gives an empty tenant list for a 401 (a token on the tenant picker)', async () => {
    mockFetch({ '/auth/me/tenants': [() => emptyResponse(401)] });

    await expect(queryClient.fetchQuery(myTenantsQueryOptions(true))).resolves.toEqual([]);
  });

  it('onSessionChanged drops the CSRF token and refetches the session', async () => {
    const fetchMock = mockFetch({
      '/auth/me': [() => jsonResponse(200, me), () => jsonResponse(200, { ...me, tenantId: 't2' })],
      '/auth/csrf': [() => jsonResponse(200, { token: 'csrf-1' }), () => jsonResponse(200, { token: 'csrf-2' })],
      '/auth/set-tenant': [() => jsonResponse(200, {}), () => jsonResponse(200, {})],
    });
    // An observer keeps the query active, as the banner does, so invalidation refetches it
    const unsubscribe = new QueryObserver(queryClient, sessionQueryOptions).subscribe(() => undefined);
    await queryClient.fetchQuery(sessionQueryOptions);
    await apiFetch('/auth/set-tenant', { method: 'POST' });

    await onSessionChanged(queryClient);
    await apiFetch('/auth/set-tenant', { method: 'POST' });
    unsubscribe();

    const csrfHeaders = fetchMock.mock.calls
      .map((call, index) => ({ url: String(call[0]), index }))
      .filter((call) => call.url === '/auth/set-tenant')
      .map((call) => headersOf(fetchMock, call.index)['X-CSRF-Token']);
    expect(csrfHeaders).toEqual(['csrf-1', 'csrf-2']);
    expect(fetchMock.mock.calls.filter((call) => String(call[0]) === '/auth/me')).toHaveLength(2);
    expect(queryClient.getQueryData<typeof me>(sessionKey)?.tenantId).toBe('t2');
  });

  it('a 401 from any other query marks the session signed out', async () => {
    queryClient.setQueryData(sessionKey, me);
    mockFetch({ '/api/members': [() => emptyResponse(401)] });

    await queryClient
      .fetchQuery({ queryKey: ['members'], queryFn: () => apiFetch('/api/members').then((r) => r.data), retry: false })
      .catch(() => undefined);

    expect(queryClient.getQueryData(sessionKey)).toBeNull();
  });
});

describe('shouldRetry', () => {
  it('never retries a 4xx', () => {
    expect(shouldRetry(0, new ApiError({ status: 404, message: 'Not found' }))).toBe(false);
  });

  it('retries a 5xx or a network error once', () => {
    expect(shouldRetry(0, new ApiError({ status: 503, message: 'Unavailable' }))).toBe(true);
    expect(shouldRetry(0, new ApiError({ status: 0, message: 'No response' }))).toBe(true);
    expect(shouldRetry(1, new ApiError({ status: 503, message: 'Unavailable' }))).toBe(false);
  });
});

describe('formatRemaining', () => {
  const now = Date.parse('2026-11-27T12:00:00Z');

  it('formats minutes and seconds', () => {
    expect(formatRemaining('2026-11-27T12:14:05Z', now)).toBe('14:05');
  });

  it('formats hours', () => {
    expect(formatRemaining('2026-11-27T13:02:09Z', now)).toBe('1:02:09');
  });

  it('is null once expired or for a bad date', () => {
    expect(formatRemaining('2026-11-27T11:59:59Z', now)).toBeNull();
    expect(formatRemaining('not a date', now)).toBeNull();
  });
});
