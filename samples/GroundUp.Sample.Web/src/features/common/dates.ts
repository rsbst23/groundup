/**
 * The end of a calendar day in the browser's time zone, as a UTC ISO string: an expiry picked as
 * "2026-12-01" lasts until that day ends where the user is. Null for an empty or invalid value.
 */
export function endOfLocalDayIso(day: string): string | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day.trim());
  if (!match) {
    return null;
  }
  const [, year, month, date] = match.map(Number) as [number, number, number, number];
  const end = new Date(year, month - 1, date, 23, 59, 59, 999);
  // new Date rolls over impossible dates (2026-02-31); refuse them instead
  if (end.getFullYear() !== year || end.getMonth() !== month - 1 || end.getDate() !== date) {
    return null;
  }
  return end.toISOString();
}

/** Today in the browser's time zone as YYYY-MM-DD, for a date input's minimum. */
export function todayLocal(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** A UTC timestamp from the API in the browser's locale and time zone, or a dash when there is none. */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) {
    return '—';
  }
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
}
