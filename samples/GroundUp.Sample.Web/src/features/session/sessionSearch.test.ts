import { describe, expect, it } from 'vitest';
import { isCallbackLanding, sessionSearchSchema } from './sessionSearch';

describe('sessionSearchSchema', () => {
  it('keeps the values the callback sends', () => {
    expect(sessionSearchSchema.parse({ result: 'signed_in', alreadyMember: true })).toEqual({
      result: 'signed_in',
      alreadyMember: true,
    });
    expect(sessionSearchSchema.parse({ error: 'IDP_ERROR', idpError: 'access_denied' })).toEqual({
      error: 'IDP_ERROR',
      idpError: 'access_denied',
    });
  });

  it('accepts alreadyMember as the string "true"', () => {
    expect(sessionSearchSchema.parse({ result: 'signed_in', alreadyMember: 'true' }).alreadyMember).toBe(true);
  });

  it('drops values that do not match', () => {
    expect(
      sessionSearchSchema.parse({
        result: 'hacked',
        alreadyMember: 'false',
        error: '<b>Your account is locked, call 555-0100</b>',
        idpError: 'ACCESS DENIED',
      }),
    ).toEqual({});
  });

  it('drops a lower-case or overlong error code', () => {
    expect(sessionSearchSchema.parse({ error: 'reauth_required' }).error).toBeUndefined();
    expect(sessionSearchSchema.parse({ error: 'A'.repeat(65) }).error).toBeUndefined();
  });

  it('is empty without any values', () => {
    expect(sessionSearchSchema.parse({})).toEqual({});
  });

  it('keeps next for a path in this app', () => {
    expect(sessionSearchSchema.parse({ result: 'signed_in', next: '/data?page=2' }).next).toBe('/data?page=2');
  });

  it.each(['//evil.example.com', 'https://evil.example.com', '/api/members', 42])('drops next %s', (next) => {
    expect(sessionSearchSchema.parse({ next }).next).toBeUndefined();
  });
});

describe('isCallbackLanding', () => {
  it('is true for a result or an error, false otherwise', () => {
    expect(isCallbackLanding({ result: 'no_tenant' })).toBe(true);
    expect(isCallbackLanding({ error: 'FLOW_EXPIRED' })).toBe(true);
    expect(isCallbackLanding({})).toBe(false);
  });
});
