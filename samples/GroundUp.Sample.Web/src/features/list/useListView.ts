import { useEffect } from 'react';
import { PAGE_SIZE } from '../../api/paging';
import { pageBackTarget, toListSearch, toListState, type ListSearch, type ListState } from './listSearch';

/** A list page's view and how to change it. */
export interface ListView {
  state: ListState;
  change: (state: ListState, options?: { replace?: boolean }) => void;
}

/**
 * The view from the route's validated search params, and a `change` that writes it back to the URL.
 * `go` is the route's navigate: `(search, replace) => navigate({ search, replace })`.
 */
export function useListView(search: ListSearch, go: (search: ListSearch, replace: boolean) => void): ListView {
  return {
    state: toListState(search),
    change: (state, options) => go(toListSearch(state), options?.replace ?? false),
  };
}

/**
 * Moves to the last page with rows when the shown page came back empty although the list has rows:
 * a change emptied the last page, or a link or a tenant switch pointed past the end. Only real data
 * counts, not the previous page kept on screen while the next one loads.
 */
export function usePageBack(
  data: { items: unknown[]; pageNumber: number; totalRecords: number } | undefined,
  isPlaceholderData: boolean,
  view: ListView,
) {
  const target = data && !isPlaceholderData ? pageBackTarget(data, PAGE_SIZE) : undefined;
  const { state, change } = view;
  useEffect(() => {
    if (target !== undefined && target !== state.page) {
      change({ ...state, page: target }, { replace: true });
    }
    // Runs when the target changes; state and change are rebuilt on every render
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [target]);
}
