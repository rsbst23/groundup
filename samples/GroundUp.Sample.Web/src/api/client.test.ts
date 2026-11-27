import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch } from './client';
import { clearCsrfToken } from './csrf';
import { ApiError } from './errors';
import { clearInspector, getInspectorSnapshot } from './inspector';
import { headersOf, jsonResponse, mockFetch } from './testing';

beforeEach(() => {
  clearCsrfToken();
  clearInspector();
});
afterEach(() => vi.unstubAllGlobals());

const csrfFailed = () => jsonResponse(403, { message: 'The CSRF token is missing or invalid.', errorCode: 'CSRF_VALIDATION_FAILED' });

describe('apiFetch', () => {
  it.each(['POST', 'PUT', 'PATCH', 'DELETE'] as const)('sends X-CSRF-Token on %s', async (method) => {
    const fetchMock = mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 'csrf-1' })],
      '/api/things/1': [() => jsonResponse(200, {})],
    });

    await apiFetch('/api/things/1', { method });

    expect(headersOf(fetchMock, 1)['X-CSRF-Token']).toBe('csrf-1');
  });

  it('after 403 CSRF_VALIDATION_FAILED fetches a new token and retries once', async () => {
    const fetchMock = mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 'old' }), () => jsonResponse(200, { token: 'new' })],
      '/auth/refresh': [csrfFailed, () => jsonResponse(200, { ok: true })],
    });

    const result = await apiFetch<{ ok: boolean }>('/auth/refresh', { method: 'POST' });

    expect(result.data).toEqual({ ok: true });
    expect(headersOf(fetchMock, 3)['X-CSRF-Token']).toBe('new');
  });

  it('throws when the retry also fails CSRF (no second retry)', async () => {
    const fetchMock = mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 'a' }), () => jsonResponse(200, { token: 'b' })],
      '/auth/refresh': [csrfFailed, csrfFailed],
    });

    await expect(apiFetch('/auth/refresh', { method: 'POST' })).rejects.toMatchObject({
      status: 403,
      errorCode: 'CSRF_VALIDATION_FAILED',
    });
    expect(fetchMock).toHaveBeenCalledTimes(4);
  });

  it('does not retry another 403', async () => {
    const fetchMock = mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 'a' })],
      '/api/members/1': [() => jsonResponse(403, { success: false, message: 'Forbidden', statusCode: 403, errorCode: 'FORBIDDEN' })],
    });

    await expect(apiFetch('/api/members/1', { method: 'DELETE' })).rejects.toMatchObject({ status: 403, errorCode: 'FORBIDDEN' });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('unwraps OperationResult data', async () => {
    mockFetch({ '/api/roles': [() => jsonResponse(200, { data: [{ id: 'r1' }], success: true, message: 'Success', statusCode: 200 })] });

    const result = await apiFetch<{ id: string }[]>('/api/roles');

    expect(result.data).toEqual([{ id: 'r1' }]);
  });

  it('returns a body that is not an OperationResult as is', async () => {
    mockFetch({ '/auth/me': [() => jsonResponse(200, { userId: 'u1', roles: [] })] });

    const result = await apiFetch('/auth/me');

    expect(result.data).toEqual({ userId: 'u1', roles: [] });
  });

  it('skips unwrapping with raw', async () => {
    const body = { data: 1, success: true, message: 'Created', statusCode: 201 };
    mockFetch({ '/api/things': [() => jsonResponse(201, body)] });

    const result = await apiFetch('/api/things', { raw: true });

    expect(result.data).toEqual(body);
  });

  it('exposes the response headers and the correlation ID', async () => {
    mockFetch({
      '/api/todoitems': [() => jsonResponse(200, { data: { items: [] }, success: true, message: 'Success', statusCode: 200 }, { 'X-Total-Count': '42', 'X-Correlation-Id': 'cid-9' })],
    });

    const result = await apiFetch('/api/todoitems');

    expect(result.headers.get('X-Total-Count')).toBe('42');
    expect(result.correlationId).toBe('cid-9');
  });

  it("sends the caller's correlation ID", async () => {
    const fetchMock = mockFetch({ '/health': [() => new Response('Healthy', { status: 200 })] });

    const result = await apiFetch('/health', { correlationId: 'my-cid' });

    expect(headersOf(fetchMock, 0)['X-Correlation-Id']).toBe('my-cid');
    expect(result.data).toBe('Healthy');
  });

  it('sends a JSON body with its content type', async () => {
    const fetchMock = mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 't' })],
      '/api/invitations': [() => jsonResponse(201, {})],
    });

    await apiFetch('/api/invitations', { method: 'POST', body: { email: 'a@example.com' } });

    const init = fetchMock.mock.calls[1]?.[1] as RequestInit;
    expect(init.body).toBe('{"email":"a@example.com"}');
    expect(headersOf(fetchMock, 1)['Content-Type']).toBe('application/json');
    expect(init.credentials).toBe('same-origin');
  });

  it('throws ApiError with the parsed error and correlation ID', async () => {
    mockFetch({
      '/api/members': [() => jsonResponse(403, { success: false, message: 'A tenant must be selected.', statusCode: 403, errorCode: 'TENANT_REQUIRED' }, { 'X-Correlation-Id': 'cid-4' })],
    });

    const error = await apiFetch('/api/members').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 403, errorCode: 'TENANT_REQUIRED', correlationId: 'cid-4', message: 'A tenant must be selected.' });
  });

  it('turns a network failure into ApiError with status 0', async () => {
    mockFetch({ '/auth/me': [() => Promise.reject(new TypeError('Failed to fetch'))] });

    await expect(apiFetch('/auth/me')).rejects.toMatchObject({ status: 0, message: 'Could not reach the API: Failed to fetch' });
    expect(getInspectorSnapshot()[0]?.error).toBe('Failed to fetch');
  });

  it('rethrows an abort unchanged and records it as cancelled', async () => {
    mockFetch({ '/auth/me': [() => Promise.reject(new DOMException('The operation was aborted.', 'AbortError'))] });

    await expect(apiFetch('/auth/me')).rejects.toMatchObject({ name: 'AbortError' });
    expect(getInspectorSnapshot()[0]?.cancelled).toBe(true);
  });

  it('does not record a network failure as cancelled', async () => {
    mockFetch({ '/auth/me': [() => Promise.reject(new TypeError('Failed to fetch'))] });

    await apiFetch('/auth/me').catch(() => undefined);

    expect(getInspectorSnapshot()[0]?.cancelled).toBe(false);
  });

  it('keeps an unreadable JSON body as text', async () => {
    mockFetch({ '/api/broken': [() => new Response('{not json', { status: 500, headers: { 'Content-Type': 'application/json' } })] });

    await expect(apiFetch('/api/broken')).rejects.toMatchObject({ status: 500, body: '{not json' });
  });

  it('records each call in the inspector, including the CSRF fetch', async () => {
    mockFetch({
      '/auth/csrf': [() => jsonResponse(200, { token: 'csrf-1' })],
      '/auth/refresh': [() => jsonResponse(200, {})],
    });

    await apiFetch('/auth/refresh', { method: 'POST' });

    const urls = getInspectorSnapshot().map((entry) => entry.url);
    expect(urls).toEqual(['/auth/refresh', '/auth/csrf']);
    expect(JSON.stringify(getInspectorSnapshot())).not.toContain('csrf-1');
  });
});
