import { MutationCache, QueryCache, QueryClient, type QueryKey } from '@tanstack/react-query';
import { ApiError } from './api/errors';
import { markSignedOut, sessionKey } from './features/session/sessionQuery';

function isSessionQuery(queryKey: QueryKey): boolean {
  return queryKey[0] === sessionKey[0];
}

/**
 * A 401 from any call means the session is over: mark it signed out so the banner and pages
 * update. The session queries handle their own 401 (they return null or an empty list).
 * REAUTH_REQUIRED is also a 401; the page shows the error panel's "Sign in again" action.
 */
function handleUnauthorized(error: unknown, queryKey?: QueryKey): void {
  if (error instanceof ApiError && error.status === 401 && !(queryKey && isSessionQuery(queryKey))) {
    markSignedOut(queryClient);
  }
}

/** Retry once for network errors and 5xx; never for 4xx, which will not change on a retry. */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
    return false;
  }
  return failureCount < 1;
}

export const queryClient: QueryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: shouldRetry,
      // A test tool: refresh on purpose, not when coming back from the Keycloak tab.
      refetchOnWindowFocus: false,
    },
    mutations: { retry: false },
  },
  queryCache: new QueryCache({ onError: (error, query) => handleUnauthorized(error, query.queryKey) }),
  mutationCache: new MutationCache({ onError: (error) => handleUnauthorized(error) }),
});
