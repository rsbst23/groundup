import { describe, expect, it } from 'vitest';
import { acceptanceOutcomeSchema, acceptanceReturnUrl, hasOutcome, lookupReasonText } from './outcomeSearch';

describe('acceptanceOutcomeSchema', () => {
  it('keeps the callback values', () => {
    expect(acceptanceOutcomeSchema.parse({ result: 'signed_in', alreadyMember: 'true' })).toEqual({
      result: 'signed_in',
      alreadyMember: true,
    });
    expect(acceptanceOutcomeSchema.parse({ error: 'INVITATION_EMAIL_MISMATCH', idpError: 'access_denied' })).toEqual({
      error: 'INVITATION_EMAIL_MISMATCH',
      idpError: 'access_denied',
    });
  });

  it('drops crafted values and has no next', () => {
    const parsed = acceptanceOutcomeSchema.parse({
      result: 'owned',
      error: '<script>',
      idpError: 'Bad Value',
      alreadyMember: 'yes',
      next: '/members',
    });
    expect(parsed).toEqual({});
    expect(hasOutcome(parsed)).toBe(false);
  });
});

describe('hasOutcome', () => {
  it('is true for a result or an error', () => {
    expect(hasOutcome({ result: 'no_tenant' })).toBe(true);
    expect(hasOutcome({ error: 'FLOW_EXPIRED' })).toBe(true);
    expect(hasOutcome({})).toBe(false);
  });
});

describe('acceptanceReturnUrl', () => {
  it('returns to the same page, with the token encoded', () => {
    expect(acceptanceReturnUrl('invitation', 'abc')).toBe('/invite/abc');
    expect(acceptanceReturnUrl('joinLink', 'a/b+c')).toBe('/join/a%2Fb%2Bc');
  });
});

describe('lookupReasonText', () => {
  it('explains each reason, and anything else generically', () => {
    for (const reason of ['not_found', 'revoked', 'expired', 'accepted', 'used_up']) {
      expect(lookupReasonText(reason)).not.toBe('This link cannot be used.');
    }
    expect(lookupReasonText('something_new')).toBe('This link cannot be used.');
    expect(lookupReasonText(null)).toBe('This link cannot be used.');
  });
});
