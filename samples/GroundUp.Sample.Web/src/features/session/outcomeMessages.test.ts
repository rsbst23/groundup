import { describe, expect, it } from 'vitest';
import { GENERIC_OUTCOME_MESSAGE, KNOWN_OUTCOME_CODES, outcomeMessage } from './outcomeMessages';

describe('outcomeMessage', () => {
  it.each([
    'REAUTH_REQUIRED',
    'CSRF_STATE_MISMATCH',
    'FLOW_ALREADY_CONSUMED',
    'FLOW_EXPIRED',
    'IDP_ERROR',
    'ACCESS_DENIED',
    'NO_DEFAULT_TENANT',
    'EMAIL_NOT_VERIFIED',
    'INVITATION_EMAIL_MISMATCH',
  ])('%s has its own explanation', (code) => {
    expect(KNOWN_OUTCOME_CODES).toContain(code);
    expect(outcomeMessage(code)).not.toBe(GENERIC_OUTCOME_MESSAGE);
  });

  it('gives the generic explanation for other codes', () => {
    expect(outcomeMessage('TOKEN_GENERATION_FAILED')).toBe(GENERIC_OUTCOME_MESSAGE);
  });
});
