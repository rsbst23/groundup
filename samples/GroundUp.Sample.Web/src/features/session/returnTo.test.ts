import { describe, expect, it } from 'vitest';
import { buildReturnUrl, isAppPath, MAX_NEXT_LENGTH, pageToReturnTo } from './returnTo';

describe('isAppPath', () => {
  it.each(['/', '/members', '/data?page=2', '/invite/abc?org=acme'])('accepts %s', (value) => {
    expect(isAppPath(value)).toBe(true);
  });

  it.each([
    '',
    '//evil.example.com',
    '/\\evil.example.com',
    '/a\\b',
    'https://evil.example.com',
    'members',
    '/api/members',
    '/auth/callback',
    '/authors',
    '/swagger',
    '/setup',
    '/health',
  ])('refuses %s', (value) => {
    expect(isAppPath(value)).toBe(false);
  });

  it('refuses a value longer than the limit', () => {
    expect(isAppPath('/' + 'a'.repeat(MAX_NEXT_LENGTH))).toBe(false);
    expect(isAppPath('/' + 'a'.repeat(MAX_NEXT_LENGTH - 1))).toBe(true);
  });
});

describe('pageToReturnTo', () => {
  it('keeps the path and query', () => {
    expect(pageToReturnTo({ pathname: '/data', search: '?page=2&sort=name' })).toBe('/data?page=2&sort=name');
  });

  it('removes the callback values', () => {
    expect(pageToReturnTo({ pathname: '/members', search: '?result=signed_in&error=X&next=%2Fa&tab=2' })).toBe(
      '/members?tab=2',
    );
  });

  it('is null on the Session page', () => {
    expect(pageToReturnTo({ pathname: '/session', search: '?result=signed_in' })).toBeNull();
  });
});

describe('buildReturnUrl', () => {
  it('is the Session page when starting there', () => {
    expect(buildReturnUrl({ pathname: '/session', search: '' })).toBe('/session');
  });

  it('carries the starting page as next', () => {
    expect(buildReturnUrl({ pathname: '/data', search: '?page=2' })).toBe('/session?next=%2Fdata%3Fpage%3D2');
    expect(buildReturnUrl({ pathname: '/', search: '' })).toBe('/session?next=%2F');
  });

  it('drops next when the return URL would be too long for the API', () => {
    // 900 characters that each encode to three
    const page = '/' + '%'.repeat(900);
    expect(buildReturnUrl({ pathname: page, search: '' })).toBe('/session');
  });

  it('gives a value the API accepts as a return URL (a single leading slash, no backslash or fragment)', () => {
    const value = buildReturnUrl({ pathname: '/invite/abc', search: '?org=acme#x' });
    expect(value.startsWith('/session')).toBe(true);
    expect(value).not.toMatch(/[\\#]/);
  });
});
