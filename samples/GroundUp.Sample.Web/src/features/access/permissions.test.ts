import { describe, expect, it } from 'vitest';
import type { MeDto } from '../../api/types';
import { can, missingPermissionText, PermissionKeys } from './permissions';

const me = (overrides: Partial<MeDto>): MeDto => ({
  userId: 'u1',
  email: 'a@example.com',
  displayName: 'A',
  tenantId: 't1',
  roles: [],
  allPermissions: false,
  permissions: [],
  issuedAt: null,
  expiresAt: null,
  authTime: null,
  ...overrides,
});

describe('can', () => {
  it('is true for a listed permission', () => {
    expect(can(me({ permissions: [PermissionKeys.MembersRead] }), PermissionKeys.MembersRead)).toBe(true);
  });

  it('is false for a permission not listed', () => {
    expect(can(me({ permissions: [PermissionKeys.MembersRead] }), PermissionKeys.MembersManage)).toBe(false);
  });

  it('is true for anything with allPermissions (TenantAdmin, SuperAdmin)', () => {
    expect(can(me({ allPermissions: true }), 'anything.at.all')).toBe(true);
  });

  it('is false signed out', () => {
    expect(can(null, PermissionKeys.MembersRead)).toBe(false);
    expect(can(undefined, PermissionKeys.MembersRead)).toBe(false);
  });

  it('copes with an older API that sends no permissions', () => {
    const old = { ...me({}), permissions: undefined } as unknown as MeDto;
    expect(can(old, PermissionKeys.MembersRead)).toBe(false);
  });

  it('names the missing permission', () => {
    expect(missingPermissionText(PermissionKeys.MembersManage)).toBe('Needs the permission auth.members.manage');
  });
});
