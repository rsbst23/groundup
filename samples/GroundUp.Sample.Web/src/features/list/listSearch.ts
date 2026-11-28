import { z } from 'zod';

/**
 * A list page's view, kept in the URL (`?page=2&sort=email%20desc&q=ali&status=2`) so a refresh, the
 * back button and a copied link show the same rows (GU-98). `page` starts at 1; `status` is the
 * enum number the API sends.
 */
export interface ListState {
  page: number;
  sort?: string;
  q?: string;
  status?: number;
}

// The router parses query values as JSON, so `q=123` arrives as a number; keep it as text.
const text = (schema: z.ZodString) =>
  z.preprocess((value) => (typeof value === 'number' ? String(value) : value), schema.optional()).catch(undefined);

/**
 * The search params of a list route. Anything invalid is dropped, so a hand-edited or old link still
 * opens (on the defaults) instead of failing.
 */
export const listSearchSchema = z.object({
  page: z.coerce.number().int().min(1).optional().catch(undefined),
  sort: text(z.string().regex(/^[A-Za-z]{1,64}( (asc|desc))?$/)),
  q: text(z.string().trim().min(1).max(200)),
  status: z.coerce.number().int().min(0).max(99).optional().catch(undefined),
});

export type ListSearch = z.infer<typeof listSearchSchema>;

/** The view for validated search params, with the defaults filled in. */
export function toListState(search: ListSearch): ListState {
  return { page: search.page ?? 1, sort: search.sort, q: search.q, status: search.status };
}

/** The search params for a view: defaults are left out so the URL stays short. */
export function toListSearch(state: ListState): ListSearch {
  return {
    page: state.page > 1 ? state.page : undefined,
    sort: state.sort || undefined,
    q: state.q?.trim() || undefined,
    status: state.status,
  };
}

export type SortDirection = 'asc' | 'desc';

/** `"email desc"` → `{ field: 'email', direction: 'desc' }`; undefined when not sorted. */
export function parseSort(sort: string | undefined): { field: string; direction: SortDirection } | undefined {
  if (!sort) {
    return undefined;
  }
  const [field = '', direction] = sort.split(' ');
  return { field, direction: direction === 'desc' ? 'desc' : 'asc' };
}

/** The sort after clicking a column header: ascending first, then it toggles. */
export function nextSort(current: string | undefined, field: string): string {
  const sorted = parseSort(current);
  return sorted?.field === field && sorted.direction === 'asc' ? `${field} desc` : field;
}

/**
 * Where to go when a page came back empty although the list has rows (a change emptied the last
 * page, or the link pointed past the end): the last page with rows. Undefined when nothing to do.
 */
export function pageBackTarget(page: { items: unknown[]; pageNumber: number; totalRecords: number }, pageSize: number) {
  if (page.items.length > 0 || page.pageNumber <= 1 || page.totalRecords === 0) {
    return undefined;
  }
  return lastPageWithRows(page.totalRecords, pageSize);
}

/** The last page that has rows (at least 1). */
export function lastPageWithRows(totalRecords: number, pageSize: number): number {
  return Math.max(1, Math.ceil(totalRecords / pageSize));
}
