import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { keysChanged, moduleState, sortedKeys, toggleKey, toggleModule } from '../features/policies/permissionChoices';
import { savePolicyEdit } from '../features/policies/policiesQuery';
import { clearCsrfToken } from './csrf';
import { clearInspector } from './inspector';
import {
  createPolicy,
  deletePolicy,
  getPermissionCatalog,
  getPolicy,
  listPolicyPage,
  setPolicyPermissions,
  updatePolicy,
} from './policies';
import { jsonResponse, mockFetch } from './testing';
import type { PermissionModuleDto, PolicyDetailDto } from './types';

beforeEach(() => {
  clearCsrfToken();
  clearInspector();
});
afterEach(() => vi.unstubAllGlobals());

const csrf = () => jsonResponse(200, { token: 'csrf-1' });
const ok = <T>(data: T, statusCode = 200) => () => jsonResponse(statusCode, { data, success: true, message: 'Success', statusCode });
const done = () => jsonResponse(200, { success: true, statusCode: 200, message: 'Success' });

const permission = (key: string) => ({ id: key, key, name: key, description: null, module: key.split('.')[0]! });
const detail = (overrides: Partial<PolicyDetailDto> = {}): PolicyDetailDto => ({
  id: 'p1',
  name: 'Basics',
  description: null,
  permissions: [permission('auth.members.read')],
  roles: [],
  ...overrides,
});

function callsTo(mock: ReturnType<typeof mockFetch>, url: string) {
  return mock.mock.calls.filter((c) => String(c[0]) === url).map((c) => c[1] as RequestInit);
}

describe('policies API', () => {
  it('lists a view of the policies table', async () => {
    const policy = { id: 'p1', name: 'Basics', description: null, permissionKeys: [], roleCount: 2 };
    mockFetch({ '/api/policies?pageNumber=1&pageSize=25&sortBy=roleCount+desc&searchTerm=bas': [ok({ items: [policy], pageNumber: 1, pageSize: 25, totalRecords: 1, totalPages: 1 })] });

    const result = await listPolicyPage({ page: 1, sort: 'roleCount desc', q: 'bas' });

    expect(result.items).toEqual([policy]);
  });

  it('reads, creates, updates, sets permissions and deletes', async () => {
    const mock = mockFetch({
      '/api/policies/p%2F1': [ok(detail()), ok(detail({ name: 'B' })), done],
      '/auth/csrf': [csrf],
      '/api/policies': [ok(detail(), 201)],
      '/api/policies/p%2F1/permissions': [ok(detail())],
    });

    expect((await getPolicy('p/1')).name).toBe('Basics');
    await createPolicy({ name: 'Basics', description: null, permissionKeys: ['auth.members.read'] });
    await updatePolicy('p/1', { name: 'B', description: 'x' });
    await setPolicyPermissions('p/1', ['a.read', 'b.read']);
    await deletePolicy('p/1');

    expect(callsTo(mock, '/api/policies')[0]!.body).toBe('{"name":"Basics","description":null,"permissionKeys":["auth.members.read"]}');
    expect(callsTo(mock, '/api/policies/p%2F1').map((c) => c.method ?? 'GET')).toEqual(['GET', 'PUT', 'DELETE']);
    expect(callsTo(mock, '/api/policies/p%2F1')[1]!.body).toBe('{"name":"B","description":"x"}');
    const set = callsTo(mock, '/api/policies/p%2F1/permissions')[0]!;
    expect(set.method).toBe('PUT');
    expect(set.body).toBe('{"permissionKeys":["a.read","b.read"]}');
  });

  it('reads the permission catalogue', async () => {
    const catalog = [{ module: 'auth', permissions: [permission('auth.members.read')] }];
    mockFetch({ '/api/permissions': [ok(catalog)] });

    await expect(getPermissionCatalog()).resolves.toEqual(catalog);
  });
});

describe('savePolicyEdit', () => {
  it('sends only the name when only the name changed', async () => {
    const mock = mockFetch({ '/auth/csrf': [csrf], '/api/policies/p1': [ok(detail({ name: 'New' }))] });

    await savePolicyEdit({ before: detail(), name: 'New', description: null, keys: new Set(['auth.members.read']) });

    expect(callsTo(mock, '/api/policies/p1')).toHaveLength(1);
    expect(callsTo(mock, '/api/policies/p1/permissions')).toHaveLength(0);
  });

  it('sends only the permissions, sorted, when only they changed', async () => {
    const mock = mockFetch({ '/auth/csrf': [csrf], '/api/policies/p1/permissions': [ok(detail())] });

    await savePolicyEdit({ before: detail(), name: 'Basics', description: null, keys: new Set(['z.read', 'auth.members.read']) });

    expect(callsTo(mock, '/api/policies/p1')).toHaveLength(0);
    expect(callsTo(mock, '/api/policies/p1/permissions')[0]!.body).toBe('{"permissionKeys":["auth.members.read","z.read"]}');
  });

  it('sends nothing when nothing changed, and stops at a failure', async () => {
    const unchanged = mockFetch({});
    await savePolicyEdit({ before: detail(), name: 'Basics', description: null, keys: new Set(['auth.members.read']) });
    expect(unchanged).not.toHaveBeenCalled();

    mockFetch({
      '/auth/csrf': [csrf],
      '/api/policies/p1': [() => jsonResponse(409, { success: false, statusCode: 409, message: 'Taken', errorCode: 'POLICY_NAME_TAKEN' })],
    });
    await expect(
      savePolicyEdit({ before: detail(), name: 'Taken', description: null, keys: new Set(['other.read']) }),
    ).rejects.toMatchObject({ errorCode: 'POLICY_NAME_TAKEN' });
  });
});

describe('permission choices', () => {
  const module: PermissionModuleDto = { module: 'auth', permissions: [permission('auth.a'), permission('auth.b')] };

  it('toggles one key without changing the input', () => {
    const selected = new Set(['auth.a']);

    expect([...toggleKey(selected, 'auth.b')]).toEqual(['auth.a', 'auth.b']);
    expect([...toggleKey(selected, 'auth.a')]).toEqual([]);
    expect([...selected]).toEqual(['auth.a']);
  });

  it('reports a module as all, some or none', () => {
    expect(moduleState(module, new Set())).toBe('none');
    expect(moduleState(module, new Set(['auth.a']))).toBe('some');
    expect(moduleState(module, new Set(['auth.a', 'auth.b', 'other.x']))).toBe('all');
  });

  it('selects a whole module, or clears it when all were selected', () => {
    expect(sortedKeys(toggleModule(module, new Set(['auth.a', 'other.x'])))).toEqual(['auth.a', 'auth.b', 'other.x']);
    expect(sortedKeys(toggleModule(module, new Set(['auth.a', 'auth.b', 'other.x'])))).toEqual(['other.x']);
  });

  it('detects a changed set, ignoring order', () => {
    expect(keysChanged(['a', 'b'], new Set(['b', 'a']))).toBe(false);
    expect(keysChanged(['a'], new Set(['a', 'b']))).toBe(true);
    expect(keysChanged(['a', 'b'], new Set(['a', 'c']))).toBe(true);
  });
});
