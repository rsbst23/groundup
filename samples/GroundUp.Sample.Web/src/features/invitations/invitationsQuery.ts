import type { ListState } from '../list/listSearch';
import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { createInvitation, listInvitations, resendInvitation, revokeInvitation } from '../../api/invitations';
import { InvitationStatus } from '../../api/labels';
import type { InvitationDto } from '../../api/types';
import { membersKey } from '../members/membersQuery';

/** Every invitations query key starts with this; the tenant is part of the key. */
export const invitationsKey = ['invitations'] as const;

/** One view (page, sort, search, status) of the current tenant's invitations; each view is cached separately. */
export function invitationsQueryOptions(tenantId: string, state: ListState) {
  return queryOptions({
    queryKey: [...invitationsKey, tenantId, state] as const,
    queryFn: ({ signal }) => listInvitations(state, signal),
    placeholderData: keepPreviousData,
  });
}

/** Resend and revoke apply to invitations not used or withdrawn yet. */
export function canResendOrRevoke(invitation: InvitationDto): boolean {
  return invitation.status === InvitationStatus.Pending || invitation.status === InvitationStatus.Expired;
}

/** A plain email check before calling the API (which validates it too). */
export function looksLikeEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
}

/**
 * The invitation actions. Each reloads the invitations, and the members too: an invitation adds
 * a pending member, and revoking it removes that member.
 */
export function useInvitationActions() {
  const queryClient = useQueryClient();
  const reload = async () => {
    await queryClient.invalidateQueries({ queryKey: invitationsKey });
    await queryClient.invalidateQueries({ queryKey: membersKey });
  };

  return {
    create: useMutation({ mutationFn: createInvitation, onSuccess: reload }),
    resend: useMutation({ mutationFn: (invitation: InvitationDto) => resendInvitation(invitation.id), onSuccess: reload }),
    revoke: useMutation({ mutationFn: (invitation: InvitationDto) => revokeInvitation(invitation.id), onSuccess: reload }),
  };
}
