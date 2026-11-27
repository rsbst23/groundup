import { proxiedPrefixes } from '../../proxiedPrefixes';

/** Query keys the sign-in callback adds; they are removed from the page a sign-in returns to. */
export const OUTCOME_KEYS = ['result', 'alreadyMember', 'error', 'idpError', 'next'] as const;

/** The longest page the app will return to after a sign-in. */
export const MAX_NEXT_LENGTH = 1000;

/** The API refuses a longer returnUrl (ReturnUrlRules.MaxLength). */
const MAX_RETURN_URL_LENGTH = 2048;

const SESSION_PATH = '/session';

interface LocationLike {
  pathname: string;
  search: string;
}

/**
 * True for a path in this app: a single leading '/', no backslash, at most 1000 characters, and not
 * an API path the dev server proxies (compared as a plain prefix, as Vite does, so '/authors' is refused).
 */
export function isAppPath(value: string): boolean {
  if (value.length === 0 || value.length > MAX_NEXT_LENGTH) {
    return false;
  }
  if (value[0] !== '/' || value[1] === '/' || value[1] === '\\' || value.includes('\\')) {
    return false;
  }
  return !proxiedPrefixes.some((prefix) => value.startsWith(prefix));
}

/**
 * The page to come back to after a sign-in: the path and query of `location` without the callback's
 * values. Null on the Session page itself, or when the result is not an app path.
 */
export function pageToReturnTo(location: LocationLike): string | null {
  if (location.pathname === SESSION_PATH) {
    return null;
  }
  const params = new URLSearchParams(location.search);
  for (const key of OUTCOME_KEYS) {
    params.delete(key);
  }
  const query = params.toString();
  const page = query ? `${location.pathname}?${query}` : location.pathname;
  return isAppPath(page) ? page : null;
}

/**
 * The returnUrl sent when starting a sign-in: every sign-in lands on the Session page, which handles
 * the outcome and then continues to `next` (the page the user started from).
 */
export function buildReturnUrl(location: LocationLike): string {
  const page = pageToReturnTo(location);
  if (page === null) {
    return SESSION_PATH;
  }
  const returnUrl = `${SESSION_PATH}?next=${encodeURIComponent(page)}`;
  return returnUrl.length <= MAX_RETURN_URL_LENGTH ? returnUrl : SESSION_PATH;
}
