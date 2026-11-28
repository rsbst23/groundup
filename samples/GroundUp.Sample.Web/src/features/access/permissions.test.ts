import { describe, expect, it } from 'vitest';
import type { MeDto } from '../../api/types';
import { can, missingPermissionText, permissionRequiredText, PermissionKeys, refusalText } from './permissions';

describe('refusals (GU-98)', () => {
  it('names the missing permission for a page', () => {
    expect(permissionRequiredText([PermissionKeys.MembersRead], 'see this page')).toBe(
      'You need the permission auth.members.read to see this page.',
    );
    expect(permissionRequiredText(['a.read', 'a.write'])).toBe('You need the permissions a.read, a.write.');
  });

  it('reads PERMISSION_REQUIRED and ROLE_REQUIRED refusals', () => {
    expect(refusalText({ errorCode: 'PERMISSION_REQUIRED', errors: ['auth.members.manage'] })).toBe(
      'You need the permission auth.members.manage.',
    );
    expect(refusalText({ errorCode: 'ROLE_REQUIRED', errors: ['SuperAdmin'] })).toBe('You need the role SuperAdmin.');
    expect(refusalText({ errorCode: 'ROLE_REQUIRED', errors: ['Admin', 'SuperAdmin'] })).toBe(
      'You need one of the roles Admin, SuperAdmin.',
    );
  });

  it('leaves other errors alone', () => {
    expect(refusalText({ errorCode: 'FORBIDDEN', errors: [] })).toBeUndefined();
    expect(refusalText({ errorCode: 'PERMISSION_NOT_HELD', errors: ['auth.members.manage'] })).toBeUndefined();
    expect(refusalText({ errorCode: 'PERMISSION_REQUIRED', errors: [] })).toBeUndefined();
  });
});

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
