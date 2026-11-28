import type { PaginatedData } from '../../api/paging';

/** Whether cached query data is a page of a list (`items` plus the paging counts). */
export function isPage(value: unknown): value is PaginatedData<unknown> {
  return (
    typeof value === 'object' &&
    value !== null &&
    Array.isArray((value as { items?: unknown }).items) &&
    typeof (value as { totalRecords?: unknown }).totalRecords === 'number'
  );
}

/**
 * A cached page without the rows `isRemoved` matches, with its counts lowered, so a deleted row
 * disappears at once instead of after the reload. Returns the same object when nothing matched
 * (or when the data is not a page), so unrelated queries are left alone.
 */
export function withoutPageItems<T>(cached: unknown, isRemoved: (item: T) => boolean): unknown {
  if (!isPage(cached)) {
    return cached;
  }
  const items = (cached.items as T[]).filter((item) => !isRemoved(item));
  const removed = cached.items.length - items.length;
  if (removed === 0) {
    return cached;
  }
  const totalRecords = Math.max(0, cached.totalRecords - removed);
  return {
    ...cached,
    items,
    totalRecords,
    totalPages: cached.pageSize > 0 ? Math.ceil(totalRecords / cached.pageSize) : cached.totalPages,
  };
}
