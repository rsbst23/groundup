import { describe, expect, it } from 'vitest';
import { Route as InvitationsRoute } from '../../routes/invitations';
import { Route as JoinLinksRoute } from '../../routes/join-links';
import { Route as MembersRoute } from '../../routes/members';
import { Route as PoliciesRoute } from '../../routes/policies';
import { Route as RoleRoute } from '../../routes/roles.$roleId';
import { Route as RolesRoute } from '../../routes/roles.index';
import { roleDetailSearchSchema } from '../roles/roleSearch';
import { lastPageWithRows, listSearchSchema, nextSort, pageBackTarget, parseSort, toListSearch, toListState } from './listSearch';

describe('listSearchSchema', () => {
  it('keeps valid values', () => {
    expect(listSearchSchema.parse({ page: 3, sort: 'email desc', q: 'ali', status: 2 })).toEqual({
      page: 3,
      sort: 'email desc',
      q: 'ali',
      status: 2,
    });
  });

  it('accepts the string forms the URL can carry', () => {
    expect(listSearchSchema.parse({ page: '2', status: '1', q: 123 })).toEqual({ page: 2, status: 1, q: '123' });
  });

  it('drops invalid values instead of failing', () => {
    expect(
      listSearchSchema.parse({ page: 0, sort: 'email; drop', q: '   ', status: -1 }),
    ).toEqual({ page: undefined, sort: undefined, q: undefined, status: undefined });
    expect(listSearchSchema.parse({ page: 'x', sort: 'email sideways', status: 'Pending' })).toEqual({
      page: undefined,
      sort: undefined,
      q: undefined,
      status: undefined,
    });
    expect(listSearchSchema.parse({ q: 'x'.repeat(201) }).q).toBeUndefined();
  });

  it('defaults to an empty view', () => {
    expect(toListState(listSearchSchema.parse({}))).toEqual({ page: 1, sort: undefined, q: undefined, status: undefined });
  });
});

describe('list routes', () => {
  it('validate their search params with the list schema', () => {
    for (const route of [MembersRoute, InvitationsRoute, JoinLinksRoute, RolesRoute, PoliciesRoute]) {
      expect(route.options.validateSearch).toBe(listSearchSchema);
    }
    expect(RoleRoute.options.validateSearch).toBe(roleDetailSearchSchema);
  });
});

describe('toListSearch', () => {
  it('leaves the defaults out of the URL', () => {
    expect(toListSearch({ page: 1, sort: '', q: '  ' })).toEqual({ page: undefined, sort: undefined, q: undefined, status: undefined });
    expect(toListSearch({ page: 2, sort: 'email', q: ' ali ', status: 0 })).toEqual({ page: 2, sort: 'email', q: 'ali', status: 0 });
  });
});

describe('sorting', () => {
  it('parses the sort', () => {
    expect(parseSort(undefined)).toBeUndefined();
    expect(parseSort('email')).toEqual({ field: 'email', direction: 'asc' });
    expect(parseSort('email desc')).toEqual({ field: 'email', direction: 'desc' });
  });

  it('toggles a column, and starts another ascending', () => {
    expect(nextSort(undefined, 'email')).toBe('email');
    expect(nextSort('email', 'email')).toBe('email desc');
    expect(nextSort('email desc', 'email')).toBe('email');
    expect(nextSort('email desc', 'status')).toBe('status');
  });
});

describe('page back', () => {
  it('goes to the last page with rows when a later page came back empty', () => {
    expect(pageBackTarget({ items: [], pageNumber: 3, totalRecords: 30 }, 25)).toBe(2);
    expect(pageBackTarget({ items: [], pageNumber: 9, totalRecords: 5 }, 25)).toBe(1);
  });

  it('does nothing when the page has rows, is the first, or the list is empty', () => {
    expect(pageBackTarget({ items: [1], pageNumber: 3, totalRecords: 60 }, 25)).toBeUndefined();
    expect(pageBackTarget({ items: [], pageNumber: 1, totalRecords: 10 }, 25)).toBeUndefined();
    expect(pageBackTarget({ items: [], pageNumber: 2, totalRecords: 0 }, 25)).toBeUndefined();
  });

  it('counts pages', () => {
    expect(lastPageWithRows(0, 25)).toBe(1);
    expect(lastPageWithRows(25, 25)).toBe(1);
    expect(lastPageWithRows(26, 25)).toBe(2);
  });
});
