/**
 * The CSRF token for cookie-authenticated POST, PUT, PATCH and DELETE requests (GU-72).
 *
 * Kept in memory only. It is tied to the signed-in user, so it is dropped whenever the user or
 * tenant changes (`clearCsrfToken`). Signing in is a full-page redirect, which reloads the app and
 * empties memory anyway; a client-side sign-in would have to call `clearCsrfToken` itself.
 */
let token: string | null = null;
let pending: Promise<string | null> | null = null;

/**
 * Returns the current token, fetching it once if there is none. Concurrent callers share one fetch.
 * `fetchToken` returns null when nobody is signed in (then requests go without the header, which
 * is correct: only cookie-authenticated requests need it).
 */
export function getCsrfToken(fetchToken: () => Promise<string | null>): Promise<string | null> {
  if (token !== null) {
    return Promise.resolve(token);
  }
  if (pending === null) {
    const request = fetchToken()
      .then((fetched) => {
        // A clear during the fetch wins: do not store a token for a user who has changed.
        if (pending === request) {
          token = fetched;
        }
        return fetched;
      })
      .finally(() => {
        if (pending === request) {
          pending = null;
        }
      });
    pending = request;
  }
  return pending;
}

/** Drops the token, so the next unsafe request fetches a new one. */
export function clearCsrfToken(): void {
  token = null;
  pending = null;
}
