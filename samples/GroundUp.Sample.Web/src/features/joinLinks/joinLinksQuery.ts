import type { ListState } from '../list/listSearch';
import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { createJoinLink, listJoinLinks, regenerateJoinLink, revokeJoinLink } from '../../api/joinLinks';
import { JoinLinkStatus } from '../../api/labels';
import type { JoinLinkDto } from '../../api/types';

/** Every join-links query key starts with this; the tenant is part of the key. */
export const joinLinksKey = ['join-links'] as const;

/** One view (page, sort, search, status) of the current tenant's join links; each view is cached separately. */
export function joinLinksQueryOptions(tenantId: string, state: ListState) {
  return queryOptions({
    queryKey: [...joinLinksKey, tenantId, state] as const,
    queryFn: ({ signal }) => listJoinLinks(state, signal),
    placeholderData: keepPreviousData,
  });
}

/** Regenerate and revoke apply to links still in use. */
export function isActive(link: JoinLinkDto): boolean {
  return link.status === JoinLinkStatus.Active;
}

/** The join-link actions. Each reloads the list. Create and regenerate return the one-time token. */
export function useJoinLinkActions() {
  const queryClient = useQueryClient();
  const reload = () => queryClient.invalidateQueries({ queryKey: joinLinksKey });

  return {
    create: useMutation({ mutationFn: createJoinLink, onSuccess: reload }),
    regenerate: useMutation({ mutationFn: (link: JoinLinkDto) => regenerateJoinLink(link.id), onSuccess: reload }),
    revoke: useMutation({ mutationFn: (link: JoinLinkDto) => revokeJoinLink(link.id), onSuccess: reload }),
  };
}
