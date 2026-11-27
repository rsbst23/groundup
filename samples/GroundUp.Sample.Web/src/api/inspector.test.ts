import { beforeEach, describe, expect, it } from 'vitest';
import {
  clearInspector,
  getInspectorSnapshot,
  INSPECTOR_CAPACITY,
  MASK,
  maskBody,
  maskHeaders,
  recordRequest,
  recordResponse,
  subscribeInspector,
} from './inspector';

describe('maskHeaders', () => {
  it('masks the credential headers, case-insensitively, and keeps the rest', () => {
    const masked = maskHeaders({
      Authorization: 'Bearer abc',
      Cookie: 'AuthToken=abc',
      'X-CSRF-Token': 'csrf',
      'X-Correlation-Id': 'cid',
    });

    expect(masked).toEqual({ authorization: MASK, cookie: MASK, 'x-csrf-token': MASK, 'x-correlation-id': 'cid' });
  });

  it('masks Set-Cookie in a Headers object', () => {
    const masked = maskHeaders(new Headers({ 'Set-Cookie': 'AuthToken=abc', 'Content-Type': 'application/json' }));

    expect(masked['set-cookie']).toBe(MASK);
    expect(masked['content-type']).toBe('application/json');
  });
});

describe('maskBody', () => {
  it('masks keys containing token, password or secret at any depth and in arrays', () => {
    const masked = maskBody({
      token: 't',
      link: { joinToken: 'j', name: 'Link' },
      users: [{ email: 'a@example.com', Password: 'p' }],
      adminClientSecret: 's',
      count: 2,
    });

    expect(masked).toEqual({
      token: MASK,
      link: { joinToken: MASK, name: 'Link' },
      users: [{ email: 'a@example.com', Password: MASK }],
      adminClientSecret: MASK,
      count: 2,
    });
  });

  it('leaves non-objects alone', () => {
    expect(maskBody('text')).toBe('text');
    expect(maskBody(null)).toBeNull();
    expect(maskBody(3)).toBe(3);
  });
});

describe('the request history', () => {
  beforeEach(() => clearInspector());

  it('records a call, masked at record time, and completes it with the response', () => {
    const id = recordRequest({ method: 'POST', url: '/auth/csrf', headers: { 'X-CSRF-Token': 'secret-value' }, body: { password: 'p' } });

    recordResponse(id, {
      status: 200,
      headers: new Headers({ 'X-Correlation-Id': 'cid' }),
      body: { token: 'value-abc' },
      correlationId: 'cid',
    });

    const [entry] = getInspectorSnapshot();
    expect(entry?.requestHeaders['x-csrf-token']).toBe(MASK);
    expect(entry?.requestBody).toEqual({ password: MASK });
    expect(entry?.responseBody).toEqual({ token: MASK });
    expect(entry?.status).toBe(200);
    expect(entry?.correlationId).toBe('cid');
    expect(entry?.durationMs).toBeGreaterThanOrEqual(0);
    expect(JSON.stringify(getInspectorSnapshot())).not.toContain('value-abc');
  });

  it('records a call that failed before a response', () => {
    const id = recordRequest({ method: 'GET', url: '/auth/me', headers: {}, body: null });

    recordResponse(id, { error: 'Failed to fetch' });

    const [entry] = getInspectorSnapshot();
    expect(entry?.status).toBe(0);
    expect(entry?.error).toBe('Failed to fetch');
  });

  it('keeps the newest entries first, up to the capacity', () => {
    for (let i = 0; i < INSPECTOR_CAPACITY + 5; i++) {
      recordRequest({ method: 'GET', url: `/api/items/${i}`, headers: {}, body: null });
    }

    const snapshot = getInspectorSnapshot();
    expect(snapshot).toHaveLength(INSPECTOR_CAPACITY);
    expect(snapshot[0]?.url).toBe(`/api/items/${INSPECTOR_CAPACITY + 4}`);
  });

  it('notifies subscribers and clears', () => {
    let calls = 0;
    const unsubscribe = subscribeInspector(() => calls++);

    recordRequest({ method: 'GET', url: '/health', headers: {}, body: null });
    clearInspector();
    unsubscribe();
    recordRequest({ method: 'GET', url: '/health', headers: {}, body: null });

    expect(calls).toBe(2);
    expect(getInspectorSnapshot()).toHaveLength(1);
  });
});
