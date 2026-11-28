import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defaultRoleChoices, defaultRoleText, policiesText } from '../features/roles/roleText';
import { clearCsrfToken } from './csrf';
import { clearInspector } from './inspector';
import { roleTypeLabel } from './labels';
import { listAllPolicies } from './policies';
import {
  attachPolicy,
  clearDefaultRole,
  createRole,
  deleteRole,
  detachPolicy,
  getDefaultRole,
  getRole,
  getRolePermissions,
  getRolePolicies,
  listRolePage,
  setDefaultRole,
  updateRole,
} from './roles';
import { jsonResponse, mockFetch } from './testing';
import type { TenantRoleDto } from './types';

beforeEach(() => {
  clearCsrfToken();
  clearInspector();
});
afterEach(() => vi.unstubAllGlobals());

const csrf = () => jsonResponse(200, { token: 'csrf-1' });
const ok = <T>(data: T, statusCode = 200) => () => jsonResponse(statusCode, { data, success: true, message: 'Success', statusCode });
const done = () => jsonResponse(200, { success: true, statusCode: 200, message: 'Success' });
const page = <T>(items: T[]) => ({ items, pageNumber: 1, pageSize: 25, totalRecords: items.length, totalPages: 1 });

const role = (overrides: Partial<TenantRoleDto> = {}): TenantRoleDto => ({
  id: 'r1',
  name: 'Member',
  description: null,
  roleType: 1,
  tenantId: 't1',
  isSystem: false,
  isDefault: false,
  policyCount: 1,
  ...overrides,
});

function callTo(mock: ReturnType<typeof mockFetch>, url: string, method = 'GET') {
  const found = mock.mock.calls.find((c) => String(c[0]) === url && ((c[1] as RequestInit | undefined)?.method ?? 'GET') === method);
  if (!found) {
    throw new Error(`No ${method} ${url}; calls were ${mock.mock.calls.map((c) => `${(c[1] as RequestInit)?.method ?? 'GET'} ${String(c[0])}`).join(', ')}`);
  }
  return found[1] as RequestInit;
}

describe('roles API', () => {
  it('lists a view of the roles table', async () => {
    mockFetch({ '/api/roles?pageNumber=2&pageSize=25&sortBy=policyCount+desc&searchTerm=mem': [ok(page([role()]))] });

    const result = await listRolePage({ page: 2, sort: 'policyCount desc', q: 'mem' });

    expect(result.items[0]!.policyCount).toBe(1);
  });

  it('reads, creates, updates and deletes a role', async () => {
    const mock = mockFetch({
      '/api/roles/r%2F1': [ok(role()), ok(role({ name: 'Members' })), done],
      '/auth/csrf': [csrf],
      '/api/roles': [ok(role(), 201)],
    });

    expect((await getRole('r/1')).name).toBe('Member');
    await createRole({ name: 'Member', description: null, roleType: 2 });
    expect((await updateRole('r/1', { name: 'Members', description: 'All' })).name).toBe('Members');
    await deleteRole('r/1');

    expect(callTo(mock, '/api/roles', 'POST').body).toBe('{"name":"Member","description":null,"roleType":2}');
    expect(callTo(mock, '/api/roles/r%2F1', 'PUT').body).toBe('{"name":"Members","description":"All"}');
    callTo(mock, '/api/roles/r%2F1', 'DELETE');
  });

  it('reads, sets and clears the default role', async () => {
    const mock = mockFetch({
      '/api/roles/default': [ok(null), done],
      '/auth/csrf': [csrf],
      '/api/roles/r1/default': [ok(role({ isDefault: true }))],
    });

    await expect(getDefaultRole()).resolves.toBeNull();
    expect((await setDefaultRole('r1')).isDefault).toBe(true);
    await clearDefaultRole();

    callTo(mock, '/api/roles/r1/default', 'PUT');
    callTo(mock, '/api/roles/default', 'DELETE');
  });

  it('lists, attaches and detaches policies, and reads the permissions', async () => {
    const link = { id: 'l1', roleId: 'r1', policyId: 'p1' };
    const permissions = { roleId: 'r1', allPermissions: false, permissions: [] };
    const mock = mockFetch({
      '/api/roles/r1/policies': [ok([{ id: 'p1', name: 'Basics', description: null, tenantId: 't1' }])],
      '/auth/csrf': [csrf],
      '/api/roles/r1/policies/p1': [ok(link, 201), done],
      '/api/roles/r1/permissions': [ok(permissions)],
    });

    expect(await getRolePolicies('r1')).toHaveLength(1);
    await expect(attachPolicy('r1', 'p1')).resolves.toEqual(link);
    await detachPolicy('r1', 'p1');
    await expect(getRolePermissions('r1')).resolves.toEqual(permissions);

    callTo(mock, '/api/roles/r1/policies/p1', 'POST');
    callTo(mock, '/api/roles/r1/policies/p1', 'DELETE');
  });

  it('lists up to 100 policies for the attach picker', async () => {
    const policy = { id: 'p1', name: 'Basics', description: null, permissionKeys: [], roleCount: 0 };
    mockFetch({ '/api/policies?pageNumber=1&pageSize=100': [ok(page([policy]))] });

    await expect(listAllPolicies()).resolves.toEqual([policy]);
  });
});

describe('role texts', () => {
  it('labels the role types', () => {
    expect(roleTypeLabel(0)).toBe('Built-in');
    expect(roleTypeLabel(1)).toBe('Application');
    expect(roleTypeLabel(2)).toBe('Workspace');
  });

  it('shows All policies for TenantAdmin, otherwise the count', () => {
    expect(policiesText(role({ isSystem: true, policyCount: 0 }))).toBe('All');
    expect(policiesText(role({ policyCount: 3 }))).toBe('3');
  });

  it('describes the default role or its absence', () => {
    expect(defaultRoleText(null)).toBe('No default role. New members who join by signing in get no role.');
    expect(defaultRoleText(role())).toBe('Default role: Member. New members who join by signing in get it.');
  });

  it('offers every role but TenantAdmin as the default, by name', () => {
    const choices = defaultRoleChoices([role({ id: 'b', name: 'Viewer' }), role({ id: 'a', name: 'TenantAdmin', isSystem: true }), role({ id: 'c', name: 'Editor' })]);

    expect(choices.map((r) => r.name)).toEqual(['Editor', 'Viewer']);
  });
});
