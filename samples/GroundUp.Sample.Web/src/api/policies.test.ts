import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { policiesListSearch, policyEditorSearchSchema, toCatalogSearch, toCatalogState } from '../features/policies/policySearch';
import {
  addKey,
  addKeys,
  changes,
  currentKeys,
  isDirty,
  removeKey,
  startWorkingSet,
  undoRemoval,
  workingRows,
} from '../features/policies/workingSet';
import { clearCsrfToken } from './csrf';
import { clearInspector } from './inspector';
import {
  createPolicy,
  deletePolicy,
  getPermissionModules,
  getPolicy,
  listPermissionCatalog,
  listPolicyPage,
  setPolicyPermissions,
  updatePolicy,
} from './policies';
import { jsonResponse, mockFetch } from './testing';
import type { PolicyDetailDto } from './types';

beforeEach(() => {
  clearCsrfToken();
  clearInspector();
});
afterEach(() => vi.unstubAllGlobals());

const csrf = () => jsonResponse(200, { token: 'csrf-1' });
const ok = <T>(data: T, statusCode = 200) => () => jsonResponse(statusCode, { data, success: true, message: 'Success', statusCode });
const done = () => jsonResponse(200, { success: true, statusCode: 200, message: 'Success' });
const page = <T>(items: T[]) => ({ items, pageNumber: 1, pageSize: 25, totalRecords: items.length, totalPages: 1 });

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
    mockFetch({ '/api/policies?pageNumber=1&pageSize=25&sortBy=roleCount+desc&searchTerm=bas': [ok(page([policy]))] });

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
    await createPolicy({ name: 'Basics', description: null });
    await updatePolicy('p/1', { name: 'B', description: 'x' });
    await setPolicyPermissions('p/1', ['a.read', 'b.read']);
    await deletePolicy('p/1');

    expect(callsTo(mock, '/api/policies')[0]!.body).toBe('{"name":"Basics","description":null}');
    expect(callsTo(mock, '/api/policies/p%2F1').map((c) => c.method ?? 'GET')).toEqual(['GET', 'PUT', 'DELETE']);
    const set = callsTo(mock, '/api/policies/p%2F1/permissions')[0]!;
    expect(set.method).toBe('PUT');
    expect(set.body).toBe('{"permissionKeys":["a.read","b.read"]}');
  });

  it('lists a view of the catalogue, with the module filter', async () => {
    mockFetch({
      '/api/permissions?pageNumber=2&pageSize=25&sortBy=key&searchTerm=read&filters%5Bmodule%5D=auth': [ok(page([permission('auth.members.read')]))],
      '/api/permissions?pageNumber=1&pageSize=25': [ok(page([]))],
    });

    expect((await listPermissionCatalog({ page: 2, sort: 'key', q: 'read', module: 'auth' })).items).toHaveLength(1);
    expect((await listPermissionCatalog({ page: 1 })).items).toEqual([]);
  });

  it('reads the modules', async () => {
    mockFetch({ '/api/permissions/modules': [ok(['auth', 'settings'])] });

    await expect(getPermissionModules()).resolves.toEqual(['auth', 'settings']);
  });
});

describe('working set', () => {
  const saved = startWorkingSet(['a', 'b']);

  it('starts clean', () => {
    expect(isDirty(saved)).toBe(false);
    expect([...currentKeys(saved)].sort()).toEqual(['a', 'b']);
  });

  it('adds new keys and ignores keys already in the policy', () => {
    const ws = addKeys(saved, ['c', 'a', 'c']);

    expect(changes(ws)).toEqual({ added: ['c'], removed: [] });
    expect([...currentKeys(ws)].sort()).toEqual(['a', 'b', 'c']);
  });

  it('removes a saved key as an unsaved removal, and an added key outright', () => {
    const ws = removeKey(removeKey(addKey(saved, 'c'), 'c'), 'a');

    expect(changes(ws)).toEqual({ added: [], removed: ['a'] });
    expect([...currentKeys(ws)]).toEqual(['b']);
    expect(isDirty(ws)).toBe(true);
  });

  it('restores a removed key by undo or by adding it again', () => {
    const removed = removeKey(saved, 'a');

    expect(isDirty(undoRemoval(removed, 'a'))).toBe(false);
    expect(isDirty(addKey(removed, 'a'))).toBe(false);
  });

  it('lists the rows with their state, removed ones included', () => {
    const ws = addKey(removeKey(saved, 'a'), 'c');

    expect(workingRows(ws)).toEqual([
      { key: 'a', state: 'removed' },
      { key: 'b', state: 'saved' },
      { key: 'c', state: 'added' },
    ]);
  });

  it('does not change the input', () => {
    removeKey(addKey(saved, 'c'), 'a');

    expect(isDirty(saved)).toBe(false);
  });
});

describe('policy editor search', () => {
  it('keeps the list view and the catalogue view apart', () => {
    const search = policyEditorSearchSchema.parse({ page: 2, available: { page: 3, q: 'read', module: 'auth', sort: 'key desc' } });

    expect(toCatalogState(search.available)).toEqual({ page: 3, sort: 'key desc', q: 'read', module: 'auth' });
    expect(policiesListSearch(search)).toEqual({ page: 2, sort: undefined, q: undefined, status: undefined });
  });

  it('drops invalid values and leaves defaults out', () => {
    expect(policyEditorSearchSchema.parse({ available: 'x' }).available).toBeUndefined();
    expect(policyEditorSearchSchema.parse({ available: { module: '' } }).available?.module).toBeUndefined();
    expect(toCatalogState(undefined)).toEqual({ page: 1, sort: undefined, q: undefined, module: undefined });
    expect(toCatalogSearch({ page: 1, q: ' ', module: '' })).toEqual({
      page: undefined,
      sort: undefined,
      q: undefined,
      status: undefined,
      module: undefined,
    });
  });
});
