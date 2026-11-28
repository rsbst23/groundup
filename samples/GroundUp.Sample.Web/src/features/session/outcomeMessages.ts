/**
 * Plain-language explanations for the error codes the sign-in callback can return (GU-87, GU-89).
 * The code itself is always shown too; this only says what it means and what to do.
 */
const messages: Record<string, string> = {
  REAUTH_REQUIRED: 'Your session is too old. Sign in again with your password.',
  CSRF_STATE_MISMATCH:
    'The sign-in was finished in a different browser or tab from the one that started it, or took too long. Start again.',
  FLOW_ALREADY_CONSUMED: 'This sign-in was already completed. Start again if you are not signed in.',
  FLOW_EXPIRED: 'The sign-in took too long (over 10 minutes). Start again.',
  FLOW_FAILED: 'This sign-in already failed. Start again.',
  IDP_ERROR: 'Keycloak reported an error.',
  ACCESS_DENIED: 'You are not a member of this organization.',
  NO_DEFAULT_TENANT: 'Sign-up without an organization needs a default organization, and none is set.',
  EMAIL_NOT_VERIFIED: 'Verify your email address first (see the email from Keycloak), then sign in.',
  INVITATION_EMAIL_MISMATCH:
    'The invitation was sent to a different email address than the one you signed in with.',
  INVITATION_NO_LONGER_VALID:
    'The invitation can no longer be used: it was withdrawn or expired, or whoever sent it can no longer give its role.',
  INVITATION_ALREADY_USED: 'This invitation was already used.',
  INVITATION_WRONG_ORGANIZATION: 'This link belongs to a different organization than the address you opened it on.',
  JOIN_LINK_NO_LONGER_VALID:
    'The join link can no longer be used: it was withdrawn, expired or used up, or whoever made it can no longer give its role.',
  ENTERPRISE_NOT_SUPPORTED: 'Invitations and join links are not available for this kind of organization yet.',
};

export const GENERIC_OUTCOME_MESSAGE = 'The sign-in did not complete.';

/** The explanation for a callback error code, or a generic one for codes without their own. */
export function outcomeMessage(code: string): string {
  return messages[code] ?? GENERIC_OUTCOME_MESSAGE;
}

/** The codes that have their own explanation (for tests and documentation). */
export const KNOWN_OUTCOME_CODES = Object.keys(messages);
