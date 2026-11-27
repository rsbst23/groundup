import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch } from './client';
import { clearCsrfToken, getCsrfToken } from './csrf';
import { clearInspector } from './inspector';
import { emptyResponse, headersOf, jsonResponse, mockFetch } from './testing';

beforeEach(() => {
  clearCsrfToken();
  clearInspector();
});
afterEach(() => vi.unstubAllGlobals());

describe('CSRF token', () => {
  it('is fetched once for the first unsafe request and reused', async () => {
    const fetchMock = mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 'csrf-1' })],
      '/auth/refresh': [() => jsonResponse(200, {}), () => jsonResponse(200, {})],
    });

    await apiFetch('/auth/refresh', { method: 'POST' });
    await apiFetch('/auth/refresh', { method: 'POST' });

    expect(fetchMock.mock.calls.map((call) => String(call[0]))).toEqual(['/auth/csrf', '/auth/refresh', '/auth/refresh']);
    expect(headersOf(fetchMock, 1)['X-CSRF-Token']).toBe('csrf-1');
    expect(headersOf(fetchMock, 2)['X-CSRF-Token']).toBe('csrf-1');
  });

  it('is shared by concurrent requests (one fetch)', async () => {
    const fetchMock = mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 'csrf-1' })],
      '/api/invitations': [() => jsonResponse(201, {}), () => jsonResponse(201, {})],
    });

    await Promise.all([
      apiFetch('/api/invitations', { method: 'POST', body: {} }),
      apiFetch('/api/invitations', { method: 'POST', body: {} }),
    ]);

    expect(fetchMock.mock.calls.filter((call) => String(call[0]) === '/auth/csrf')).toHaveLength(1);
  });

  it('is never fetched for GET', async () => {
    const fetchMock = mockFetch({ '/auth/me': [() => jsonResponse(200, { userId: 'u' })] });

    await apiFetch('/auth/me');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(headersOf(fetchMock, 0)['X-CSRF-Token']).toBeUndefined();
  });

  it('is left out when nobody is signed in (401 from /auth/csrf)', async () => {
    const fetchMock = mockFetch({
      '/auth/csrf': [() => emptyResponse(401)],
      '/auth/invitations/lookup': [() => jsonResponse(200, { usable: true })],
    });

    await apiFetch('/auth/invitations/lookup', { method: 'POST', body: { token: 'x' } });

    expect(headersOf(fetchMock, 1)['X-CSRF-Token']).toBeUndefined();
  });

  it('is fetched again after clearCsrfToken', async () => {
    const fetchMock = mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 'csrf-1' }), () => jsonResponse(200, { token: 'csrf-2' })],
      '/auth/logout': [() => jsonResponse(200, {}), () => jsonResponse(200, {})],
    });

    await apiFetch('/auth/logout', { method: 'POST' });
    clearCsrfToken();
    await apiFetch('/auth/logout', { method: 'POST' });

    expect(headersOf(fetchMock, 3)['X-CSRF-Token']).toBe('csrf-2');
  });

  it('is not stored when cleared while the fetch is in flight', async () => {
    let release: (token: string) => void = () => undefined;
    const slow = new Promise<string>((resolve) => (release = resolve));

    const first = getCsrfToken(() => slow);
    clearCsrfToken();
    release('stale');
    await first;

    const second = await getCsrfToken(() => Promise.resolve('fresh'));
    expect(second).toBe('fresh');
  });
});
