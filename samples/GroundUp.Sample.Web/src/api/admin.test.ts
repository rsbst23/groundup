import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearCsrfToken } from './csrf';
import { listDevEmails } from './devMailbox';
import { ApiError } from './errors';
import { clearInspector } from './inspector';
import { createInvitation, listInvitations, resendInvitation, revokeInvitation } from './invitations';
import { createJoinLink, listJoinLinks, regenerateJoinLink, revokeJoinLink } from './joinLinks';
import { addMemberRole, listMembers, removeMember, removeMemberRole } from './members';
import { listPath, pagedPath } from './paging';
import { listRoles } from './roles';
import { emptyResponse, headersOf, jsonResponse, mockFetch } from './testing';

beforeEach(() => {
  clearCsrfToken();
  clearInspector();
});
afterEach(() => vi.unstubAllGlobals());

const csrf = () => jsonResponse(200, { token: 'csrf-1' });
const ok = <T>(data: T, statusCode = 200) => () => jsonResponse(statusCode, { data, success: true, message: 'Success', statusCode });
const page = <T>(items: T[], pageNumber = 1) => ({ items, pageNumber, pageSize: 25, totalRecords: items.length, totalPages: 1 });

function callTo(mock: ReturnType<typeof mockFetch>, url: string) {
  const found = mock.mock.calls.find((c) => String(c[0]) === url);
  if (!found) {
    throw new Error(`No call to ${url}; calls were ${mock.mock.calls.map((c) => String(c[0])).join(', ')}`);
  }
  return found[1] as RequestInit;
}

describe('paging', () => {
  it('builds the page query, never below page 1', () => {
    expect(pagedPath('/api/members', 3)).toBe('/api/members?pageNumber=3&pageSize=25');
    expect(pagedPath('/api/members', 0)).toBe('/api/members?pageNumber=1&pageSize=25');
    expect(pagedPath('/api/roles', 1, 100)).toBe('/api/roles?pageNumber=1&pageSize=100');
  });

  it('builds the FilterParams query for a list view', () => {
    expect(listPath('/api/members', { page: 1 })).toBe('/api/members?pageNumber=1&pageSize=25');
    const url = new URL(listPath('/api/invitations', { page: 2, sort: 'email desc', q: ' a&b ', status: 3 }), 'http://x');
    expect(url.searchParams.get('pageNumber')).toBe('2');
    expect(url.searchParams.get('sortBy')).toBe('email desc');
    expect(url.searchParams.get('searchTerm')).toBe('a&b');
    expect(url.searchParams.get('filters[status]')).toBe('3');
    expect(listPath('/api/join-links', { page: 0, sort: '', q: '  ', status: 0 })).toBe(
      '/api/join-links?pageNumber=1&pageSize=25&filters%5Bstatus%5D=0',
    );
  });
});

describe('members', () => {
  it('lists a page of members', async () => {
    const member = { userId: 'u1', email: 'a@example.com', displayName: 'A', status: 0, roles: [] };
    mockFetch({ '/api/members?pageNumber=2&pageSize=25': [ok(page([member], 2))] });

    const result = await listMembers({ page: 2 });

    expect(result.items).toEqual([member]);
    expect(result.pageNumber).toBe(2);
  });

  it('sends the sort, search and status of the view', async () => {
    const mock = mockFetch({
      '/api/members?pageNumber=1&pageSize=25&sortBy=email+desc&searchTerm=ali&filters%5Bstatus%5D=2': [ok(page([]))],
    });

    await listMembers({ page: 1, sort: 'email desc', q: 'ali', status: 2 });

    expect(mock).toHaveBeenCalledOnce();
  });

  it('removes a member with DELETE and the CSRF token', async () => {
    const mock = mockFetch({ '/auth/csrf': [csrf], '/api/members/u%2F1': [() => jsonResponse(200, { success: true, statusCode: 200, message: 'Success' })] });

    await removeMember('u/1');

    expect(callTo(mock, '/api/members/u%2F1').method).toBe('DELETE');
    expect(headersOf(mock, 1)['X-CSRF-Token']).toBe('csrf-1');
  });

  it('adds and removes a role', async () => {
    const granted = { id: 'ur1', userId: 'u1', roleId: 'r1', tenantId: 't1', roleName: 'Editor', source: 0 };
    const mock = mockFetch({
      '/auth/csrf': [csrf],
      '/api/members/u1/roles/r1': [ok(granted, 201), () => jsonResponse(200, { success: true, statusCode: 200, message: 'Success' })],
    });

    await expect(addMemberRole('u1', 'r1')).resolves.toEqual(granted);
    await removeMemberRole('u1', 'r1');

    const methods = mock.mock.calls.filter((c) => String(c[0]) === '/api/members/u1/roles/r1').map((c) => (c[1] as RequestInit).method);
    expect(methods).toEqual(['POST', 'DELETE']);
  });

  it('surfaces a refusal with its code and reasons', async () => {
    mockFetch({
      '/auth/csrf': [csrf],
      '/api/members/u1/roles/r1': [
        () => jsonResponse(409, { success: false, statusCode: 409, message: 'Last admin', errorCode: 'LAST_ADMIN' }),
      ],
    });

    const error = await removeMemberRole('u1', 'r1').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).errorCode).toBe('LAST_ADMIN');
    expect((error as ApiError).status).toBe(409);
  });
});

describe('roles', () => {
  it('lists up to 100 roles for pickers', async () => {
    const role = { id: 'r1', name: 'Editor', description: null, roleType: 1, tenantId: 't1', isSystem: false, isDefault: false };
    mockFetch({ '/api/roles?pageNumber=1&pageSize=100': [ok(page([role]))] });

    await expect(listRoles()).resolves.toEqual([role]);
  });
});

describe('invitations', () => {
  const invitation = {
    id: 'i1', tenantId: 't1', email: 'c@example.com', roleId: 'r1', roleName: 'Member', userId: 'u3', createdByUserId: 'u1',
    expiresAt: '2026-12-01T00:00:00Z', status: 0, acceptedByUserId: null, acceptedAt: null, revokedAt: null, createdAt: '2026-11-24T00:00:00Z',
  };

  it('lists, creates, resends and revokes', async () => {
    const mock = mockFetch({
      '/api/invitations?pageNumber=1&pageSize=25': [ok(page([invitation]))],
      '/auth/csrf': [csrf],
      '/api/invitations': [ok(invitation, 201)],
      '/api/invitations/i1/resend': [ok(invitation)],
      '/api/invitations/i1/revoke': [() => jsonResponse(200, { success: true, statusCode: 200, message: 'Success' })],
    });

    expect((await listInvitations({ page: 1 })).items).toEqual([invitation]);
    await expect(createInvitation({ email: 'c@example.com', roleId: 'r1', expiresAt: '2026-11-30T22:59:59.999Z' })).resolves.toEqual(invitation);
    await resendInvitation('i1');
    await revokeInvitation('i1');

    expect(callTo(mock, '/api/invitations').body).toBe('{"email":"c@example.com","roleId":"r1","expiresAt":"2026-11-30T22:59:59.999Z"}');
    expect(callTo(mock, '/api/invitations/i1/resend').method).toBe('POST');
    expect(callTo(mock, '/api/invitations/i1/revoke').method).toBe('POST');
  });
});

describe('join links', () => {
  const link = {
    id: 'j1', tenantId: 't1', roleId: 'r1', roleName: 'Member', createdByUserId: 'u1', expiresAt: null, maxUses: 10,
    useCount: 0, status: 0, revokedAt: null, createdAt: '2026-11-24T00:00:00Z',
  };

  it('lists, creates (with the one-time token), regenerates and revokes', async () => {
    const mock = mockFetch({
      '/api/join-links?pageNumber=1&pageSize=25': [ok(page([link]))],
      '/auth/csrf': [csrf],
      '/api/join-links': [ok({ link, token: 'tok-1' }, 201)],
      '/api/join-links/j1/regenerate': [ok({ link, token: 'tok-2' })],
      '/api/join-links/j1/revoke': [() => jsonResponse(200, { success: true, statusCode: 200, message: 'Success' })],
    });

    expect((await listJoinLinks({ page: 1 })).items).toEqual([link]);
    expect((await createJoinLink({ roleId: 'r1', maxUses: 10 })).token).toBe('tok-1');
    expect((await regenerateJoinLink('j1')).token).toBe('tok-2');
    await revokeJoinLink('j1');

    expect(callTo(mock, '/api/join-links').body).toBe('{"roleId":"r1","maxUses":10}');
  });
});

describe('dev mailbox', () => {
  it('lists the kept emails, and a 404 means the API is not in Development', async () => {
    const email = {
      invitationId: 'i1', to: 'c@example.com', organizationName: 'My Test Org', roleName: 'Member',
      acceptUrl: 'http://localhost:5173/invite/abc', expiresAt: '2026-12-01T00:00:00Z', sentAt: '2026-11-24T00:00:00Z',
    };
    mockFetch({ '/api/dev/invitation-emails': [ok([email]), () => emptyResponse(404)] });

    await expect(listDevEmails()).resolves.toEqual([email]);
    const error = await listDevEmails().catch((e: unknown) => e);
    expect((error as ApiError).status).toBe(404);
  });
});
