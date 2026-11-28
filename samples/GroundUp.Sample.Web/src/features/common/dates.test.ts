import { describe, expect, it } from 'vitest';
import { endOfLocalDayIso, formatDateTime, todayLocal } from './dates';

describe('endOfLocalDayIso', () => {
  it('is the last millisecond of that day in the local time zone, as UTC', () => {
    expect(endOfLocalDayIso('2026-12-01')).toBe(new Date(2026, 11, 1, 23, 59, 59, 999).toISOString());
  });

  it('refuses empty, malformed and impossible dates', () => {
    expect(endOfLocalDayIso('')).toBeNull();
    expect(endOfLocalDayIso('12/01/2026')).toBeNull();
    expect(endOfLocalDayIso('2026-02-31')).toBeNull();
    expect(endOfLocalDayIso('2026-13-01')).toBeNull();
  });
});

describe('todayLocal', () => {
  it('formats a local date as YYYY-MM-DD', () => {
    expect(todayLocal(new Date(2026, 0, 5, 23, 30))).toBe('2026-01-05');
  });
});

describe('formatDateTime', () => {
  it('shows a dash for no value and the raw text for an unreadable one', () => {
    expect(formatDateTime(null)).toBe('—');
    expect(formatDateTime('not a date')).toBe('not a date');
  });

  it('formats a timestamp in the local locale', () => {
    const iso = '2026-11-24T10:00:00Z';
    expect(formatDateTime(iso)).toBe(new Date(iso).toLocaleString());
  });
});
