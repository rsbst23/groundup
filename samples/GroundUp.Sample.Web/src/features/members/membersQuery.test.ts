import { describe, expect, it } from 'vitest';
import { RoleAssignmentSource } from '../../api/labels';
import type { TenantMemberDto, TenantRoleDto } from '../../api/types';
import { memberName, roleChipLabel, rolesToAdd } from './membersQuery';

const role = (id: string, name: string): TenantRoleDto => ({
  id, name, description: null, roleType: 1, tenantId: 't1', isSystem: false, isDefault: false, policyCount: 0, activeMemberCount: 0, pendingMemberCount: 0,
});

const member = (overrides: Partial<TenantMemberDto> = {}): TenantMemberDto => ({
  userId: 'u1',
  email: 'bob@example.com',
  displayName: 'Bob Member',
  status: 0,
  roles: [],
  ...overrides,
});

describe('roleChipLabel', () => {
  it('is the name alone for a manual grant', () => {
    expect(roleChipLabel({ roleId: 'r1', roleName: 'Editor', sources: [RoleAssignmentSource.Manual] })).toBe('Editor');
    expect(roleChipLabel({ roleId: 'r1', roleName: 'Editor', sources: [] })).toBe('Editor');
  });

  it('shows the reasons otherwise', () => {
    expect(roleChipLabel({ roleId: 'r1', roleName: 'Member', sources: [RoleAssignmentSource.Invitation] })).toBe('Member · Invitation');
    expect(roleChipLabel({
      roleId: 'r1',
      roleName: 'Member',
      sources: [RoleAssignmentSource.JoinLink, RoleAssignmentSource.Manual],
    })).toBe('Member · Join link, Manual');
  });
});

describe('rolesToAdd', () => {
  it('leaves out the roles held, sorted by name', () => {
    const held = member({ roles: [{ roleId: 'r2', roleName: 'Editor', sources: [0] }] });

    expect(rolesToAdd([role('r3', 'Viewer'), role('r2', 'Editor'), role('r1', 'Auditor')], held).map((r) => r.name))
      .toEqual(['Auditor', 'Viewer']);
  });
});

describe('memberName', () => {
  it('is the display name, or the email when there is none', () => {
    expect(memberName(member())).toBe('Bob Member');
    expect(memberName(member({ displayName: null }))).toBe('bob@example.com');
    expect(memberName(member({ displayName: '  ' }))).toBe('bob@example.com');
  });
});
