/**
 * The address people open to join with a link: this web app's join page. The API returns only the
 * token, so the link is built here, on the site the admin is using.
 */
export function joinUrl(token: string, origin: string = window.location.origin): string {
  return `${origin.replace(/\/+$/, '')}/join/${encodeURIComponent(token)}`;
}

/** "2 / 10" or "2 / unlimited". */
export function usesText(useCount: number, maxUses: number | null): string {
  return `${useCount} / ${maxUses ?? 'unlimited'}`;
}

/** A maximum number of uses typed by the admin: empty means unlimited; otherwise a whole number of at least 1. */
export function parseMaxUses(value: string): { value: number | undefined; error: string | null } {
  const trimmed = value.trim();
  if (trimmed === '') {
    return { value: undefined, error: null };
  }
  if (!/^\d+$/.test(trimmed) || Number(trimmed) < 1) {
    return { value: undefined, error: 'A whole number of at least 1, or empty for unlimited.' };
  }
  return { value: Number(trimmed), error: null };
}
