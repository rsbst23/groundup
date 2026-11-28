import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { acceptInvitation, joinWithLink, lookupInvitation, lookupJoinLink } from './acceptance';
import { clearCsrfToken } from './csrf';
import { ApiError } from './errors';
import { clearInspector } from './inspector';
import { emptyResponse, headersOf, jsonResponse, mockFetch } from './testing';

beforeEach(() => {
  clearCsrfToken();
  clearInspector();
});
afterEach(() => vi.unstubAllGlobals());

/** Signed out, /auth/csrf is 401: the public endpoints are called without a CSRF header. */
const signedOutCsrf = () => emptyResponse(401);

describe('invitation acceptance', () => {
  it('looks up a token and returns the bare lookup body', async () => {
    const lookup = { usable: true, reason: null, organizationName: 'My Test Org', roleName: 'Member' };
    const mock = mockFetch({ '/auth/csrf': [signedOutCsrf], '/auth/invitations/lookup': [() => jsonResponse(200, lookup)] });

    await expect(lookupInvitation('tok')).resolves.toEqual(lookup);

    expect((mock.mock.calls[1]?.[1] as RequestInit).body).toBe('{"token":"tok"}');
    expect(headersOf(mock, 1)['X-CSRF-Token']).toBeUndefined();
  });

  it('starts the accept with the return URL and forceLogin', async () => {
    const mock = mockFetch({
      '/auth/csrf': [signedOutCsrf, signedOutCsrf],
      '/auth/invitations/accept?returnUrl=%2Finvite%2Ftok': [
        () => jsonResponse(200, { redirectUrl: 'https://kc/one' }),
        () => jsonResponse(200, { redirectUrl: 'https://kc/two' }),
      ],
    });

    await expect(acceptInvitation('tok', { returnUrl: '/invite/tok' })).resolves.toEqual({ redirectUrl: 'https://kc/one' });
    await acceptInvitation('tok', { returnUrl: '/invite/tok', forceLogin: true });

    const bodies = mock.mock.calls.filter((c) => String(c[0]).startsWith('/auth/invitations/accept')).map((c) => (c[1] as RequestInit).body);
    expect(bodies).toEqual(['{"token":"tok","forceLogin":false}', '{"token":"tok","forceLogin":true}']);
  });

  it('reads the public endpoints\' error shape', async () => {
    mockFetch({
      '/auth/csrf': [signedOutCsrf],
      '/auth/invitations/accept?returnUrl=%2Finvite%2Ftok': [
        () => jsonResponse(410, { error: 'Already used', errorCode: 'INVITATION_ALREADY_USED' }),
      ],
    });

    const error = await acceptInvitation('tok', { returnUrl: '/invite/tok' }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(410);
    expect((error as ApiError).errorCode).toBe('INVITATION_ALREADY_USED');
  });
});

describe('join link acceptance', () => {
  it('looks up and joins', async () => {
    const lookup = { usable: false, reason: 'used_up', organizationName: null, roleName: null };
    const mock = mockFetch({
      '/auth/csrf': [signedOutCsrf, signedOutCsrf],
      '/auth/join-links/lookup': [() => jsonResponse(200, lookup)],
      '/auth/join-links/join?returnUrl=%2Fjoin%2Fabc': [() => jsonResponse(200, { redirectUrl: 'https://kc/join' })],
    });

    await expect(lookupJoinLink('abc')).resolves.toEqual(lookup);
    await expect(joinWithLink('abc', { returnUrl: '/join/abc' })).resolves.toEqual({ redirectUrl: 'https://kc/join' });

    expect((mock.mock.calls[3]?.[1] as RequestInit).body).toBe('{"token":"abc","forceLogin":false}');
  });
});
