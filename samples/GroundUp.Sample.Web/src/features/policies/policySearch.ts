import { z } from 'zod';
import type { CatalogState } from '../../api/policies';
import { listSearchSchema, type ListSearch } from '../list/listSearch';

/** The catalogue's view on the editor page: the list view plus a module (GU-97d). */
export const catalogSearchSchema = listSearchSchema.extend({
  module: z.string().trim().min(1).max(100).optional().catch(undefined),
});

/**
 * The policy editor's search params: the policies list's view (for **‹ Policies**) plus the
 * catalogue's view under `available`. The router writes the nested view as JSON; an invalid one is dropped.
 */
export const policyEditorSearchSchema = listSearchSchema.extend({
  available: catalogSearchSchema.optional().catch(undefined),
});

export type PolicyEditorSearch = z.infer<typeof policyEditorSearchSchema>;

/** The policies list's view, without the catalogue's, for the back link. */
export function policiesListSearch(search: PolicyEditorSearch): ListSearch {
  return { page: search.page, sort: search.sort, q: search.q, status: search.status };
}

/** The catalogue view with defaults. */
export function toCatalogState(search: z.infer<typeof catalogSearchSchema> | undefined): CatalogState {
  return { page: search?.page ?? 1, sort: search?.sort, q: search?.q, module: search?.module };
}

/** The catalogue view as search params (defaults left out). */
export function toCatalogSearch(state: CatalogState): z.infer<typeof catalogSearchSchema> {
  return {
    page: state.page > 1 ? state.page : undefined,
    sort: state.sort || undefined,
    q: state.q?.trim() || undefined,
    status: undefined,
    module: state.module || undefined,
  };
}
