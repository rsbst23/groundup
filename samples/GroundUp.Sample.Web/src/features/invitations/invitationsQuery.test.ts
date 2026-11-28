import { describe, expect, it } from 'vitest';
import { InvitationStatus } from '../../api/labels';
import type { InvitationDto } from '../../api/types';
import { canResendOrRevoke, looksLikeEmail } from './invitationsQuery';

describe('canResendOrRevoke', () => {
  it('allows pending and expired invitations only', () => {
    const invitation = (status: number) => ({ status }) as InvitationDto;
    expect(canResendOrRevoke(invitation(InvitationStatus.Pending))).toBe(true);
    expect(canResendOrRevoke(invitation(InvitationStatus.Expired))).toBe(true);
    expect(canResendOrRevoke(invitation(InvitationStatus.Accepted))).toBe(false);
    expect(canResendOrRevoke(invitation(InvitationStatus.Revoked))).toBe(false);
  });
});

describe('looksLikeEmail', () => {
  it('accepts an address and rejects obvious mistakes', () => {
    expect(looksLikeEmail(' invitee@example.com ')).toBe(true);
    for (const bad of ['', 'invitee', 'invitee@', '@example.com', 'a b@example.com', 'invitee@example']) {
      expect(looksLikeEmail(bad)).toBe(false);
    }
  });
});
