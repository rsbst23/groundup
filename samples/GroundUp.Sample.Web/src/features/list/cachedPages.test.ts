import { describe, expect, it } from 'vitest';
import { isPage, withoutPageItems } from './cachedPages';

const page = (ids: string[], pageSize = 25, totalRecords = ids.length) => ({
  items: ids.map((id) => ({ id })),
  pageNumber: 1,
  pageSize,
  totalRecords,
  totalPages: Math.ceil(totalRecords / pageSize),
});

describe('cached pages', () => {
  it('recognises a page', () => {
    expect(isPage(page(['a']))).toBe(true);
    expect(isPage([{ id: 'a' }])).toBe(false);
    expect(isPage({ items: [] })).toBe(false);
    expect(isPage(undefined)).toBe(false);
  });

  it('removes the matching rows and lowers the counts', () => {
    expect(withoutPageItems<{ id: string }>(page(['a', 'b'], 1, 2), (row) => row.id === 'a')).toEqual({
      items: [{ id: 'b' }],
      pageNumber: 1,
      pageSize: 1,
      totalRecords: 1,
      totalPages: 1,
    });
  });

  it('counts from the whole list, not only this page', () => {
    const result = withoutPageItems<{ id: string }>(page(['a', 'b'], 25, 30), (row) => row.id === 'b') as { totalRecords: number };

    expect(result.totalRecords).toBe(29);
  });

  it('returns the same object when nothing matches or it is not a page', () => {
    const cached = page(['a']);
    const other = { roleId: 'x', permissions: [] };

    expect(withoutPageItems<{ id: string }>(cached, (row) => row.id === 'z')).toBe(cached);
    expect(withoutPageItems(other, () => true)).toBe(other);
  });
});
