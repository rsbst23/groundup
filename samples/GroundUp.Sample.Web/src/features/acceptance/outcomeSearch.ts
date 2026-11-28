import type { z } from 'zod';
import { isCallbackLanding, sessionSearchSchema } from '../session/sessionSearch';

/**
 * The sign-in outcome the callback adds when it returns to an accept or join page (GU-87):
 * the Session page's rules (`result`, `alreadyMember`, `error`, `idpError`), without `next`.
 * Anything that does not match is dropped.
 */
export const acceptanceOutcomeSchema = sessionSearchSchema.omit({ next: true });

export type AcceptanceOutcome = z.infer<typeof acceptanceOutcomeSchema>;

/** True when the page was opened by the callback rather than from the link itself. */
export function hasOutcome(outcome: AcceptanceOutcome): boolean {
  return isCallbackLanding(outcome);
}

/** What an accept or join page is about. */
export type AcceptanceKind = 'invitation' | 'joinLink';

/** The page the callback returns to: the same accept or join page, so the token is still there. */
export function acceptanceReturnUrl(kind: AcceptanceKind, token: string): string {
  return `${kind === 'invitation' ? '/invite' : '/join'}/${encodeURIComponent(token)}`;
}

const reasons: Record<string, string> = {
  not_found: 'This link is not valid. Check that it was copied completely.',
  revoked: 'This was withdrawn by the organization.',
  expired: 'This has expired. Ask the organization for a new one.',
  accepted: 'This invitation was already used.',
  used_up: 'This link has reached its maximum number of uses.',
};

/** Why a lookup says the link cannot be used, in plain words. */
export function lookupReasonText(reason: string | null): string {
  return (reason && reasons[reason]) ?? 'This link cannot be used.';
}
