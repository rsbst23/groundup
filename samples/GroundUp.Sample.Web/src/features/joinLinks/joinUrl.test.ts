import { describe, expect, it } from 'vitest';
import { JoinLinkStatus } from '../../api/labels';
import type { JoinLinkDto } from '../../api/types';
import { isActive } from './joinLinksQuery';
import { joinUrl, parseMaxUses, usesText } from './joinUrl';

describe('joinUrl', () => {
  it('builds this site\'s join page address with the token encoded', () => {
    expect(joinUrl('abc', 'http://localhost:5173')).toBe('http://localhost:5173/join/abc');
    expect(joinUrl('a/b+c=', 'http://localhost:5173/')).toBe('http://localhost:5173/join/a%2Fb%2Bc%3D');
  });
});

describe('usesText', () => {
  it('shows the maximum, or unlimited', () => {
    expect(usesText(2, 10)).toBe('2 / 10');
    expect(usesText(0, null)).toBe('0 / unlimited');
  });
});

describe('parseMaxUses', () => {
  it('reads empty as unlimited and a whole number of at least 1', () => {
    expect(parseMaxUses('')).toEqual({ value: undefined, error: null });
    expect(parseMaxUses(' 5 ')).toEqual({ value: 5, error: null });
  });

  it('refuses zero, negatives, decimals and text', () => {
    for (const bad of ['0', '-1', '2.5', 'ten']) {
      expect(parseMaxUses(bad).error).not.toBeNull();
    }
  });
});

describe('isActive', () => {
  it('is true only for active links', () => {
    const link = (status: number) => ({ status }) as JoinLinkDto;
    expect(isActive(link(JoinLinkStatus.Active))).toBe(true);
    expect(isActive(link(JoinLinkStatus.Revoked))).toBe(false);
    expect(isActive(link(JoinLinkStatus.UsedUp))).toBe(false);
  });
});
