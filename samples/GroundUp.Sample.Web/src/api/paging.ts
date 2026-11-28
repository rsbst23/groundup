import type { ListState } from '../features/list/listSearch';
import { withQuery } from './query';

/** The page size the admin pages use. */
export const PAGE_SIZE = 25;

/** A page of a list endpoint (the API's PaginatedData). `pageNumber` starts at 1. */
export interface PaginatedData<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalRecords: number;
  totalPages: number;
}

/** `path?pageNumber=…&pageSize=…`. `page` starts at 1, as in the API. */
export function pagedPath(path: string, page: number, pageSize: number = PAGE_SIZE): string {
  return withQuery(path, { pageNumber: Math.max(1, Math.floor(page)), pageSize });
}

/**
 * A list view as the API's `FilterParams` query: paging, `sortBy`, `searchTerm` and the status as
 * `filters[status]` (GU-98). Fields a list does not allow are ignored by the API.
 */
export function listPath(path: string, state: ListState, pageSize: number = PAGE_SIZE): string {
  return withQuery(path, {
    pageNumber: Math.max(1, Math.floor(state.page)),
    pageSize,
    sortBy: state.sort || undefined,
    searchTerm: state.q?.trim() || undefined,
    'filters[status]': state.status,
  });
}
