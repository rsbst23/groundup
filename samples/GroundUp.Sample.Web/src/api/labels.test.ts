import { describe, expect, it } from 'vitest';
import {
  InvitationStatus,
  invitationStatusLabel,
  JoinLinkStatus,
  joinLinkStatusLabel,
  MembershipStatus,
  membershipStatusLabel,
  RoleAssignmentSource,
  roleSourceLabel,
} from './labels';

describe('enum labels', () => {
  it('names every value the API sends', () => {
    expect(membershipStatusLabel(MembershipStatus.Active)).toBe('Active');
    expect(membershipStatusLabel(MembershipStatus.Pending)).toBe('Pending');
    expect(roleSourceLabel(RoleAssignmentSource.JoinLink)).toBe('Join link');
    expect(roleSourceLabel(RoleAssignmentSource.DefaultRole)).toBe('Default role');
    expect(roleSourceLabel(RoleAssignmentSource.Provisioning)).toBe('Provisioning');
    expect(invitationStatusLabel(InvitationStatus.Expired)).toBe('Expired');
    expect(joinLinkStatusLabel(JoinLinkStatus.UsedUp)).toBe('Used up');
  });

  it('shows a value it does not know instead of nothing', () => {
    expect(invitationStatusLabel(9)).toBe('Unknown (9)');
    expect(joinLinkStatusLabel(-1)).toBe('Unknown (-1)');
  });
});
