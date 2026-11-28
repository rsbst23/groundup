import { z } from 'zod';
import { listSearchSchema, type ListSearch } from '../list/listSearch';

/**
 * The role page's search params: the roles list's view (for **‹ Roles**) plus the members section's
 * own view under `members` (GU-97d). The router writes the nested view as JSON; an invalid one is dropped.
 */
export const roleDetailSearchSchema = listSearchSchema.extend({
  members: listSearchSchema.optional().catch(undefined),
});

export type RoleDetailSearch = z.infer<typeof roleDetailSearchSchema>;

/** The roles list's view, without the members section's, for the back link. */
export function rolesListSearch(search: RoleDetailSearch): ListSearch {
  return { page: search.page, sort: search.sort, q: search.q, status: search.status };
}
