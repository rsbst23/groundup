import { queryOptions, useQuery, type QueryClient } from '@tanstack/react-query';
import { getMe, getMyTenants } from '../../api/auth';
import { clearCsrfToken } from '../../api/csrf';

/** Every session query key starts with this, so one invalidation refreshes them all. */
export const sessionKey = ['session'] as const;

/** GET /auth/me. Null data means signed out. */
export const sessionQueryOptions = queryOptions({
  queryKey: sessionKey,
  queryFn: ({ signal }) => getMe(signal),
});

/** GET /auth/me/tenants. Only fetched while signed in. */
export function myTenantsQueryOptions(signedIn: boolean) {
  return queryOptions({
    queryKey: [...sessionKey, 'tenants'] as const,
    queryFn: ({ signal }) => getMyTenants(signal),
    enabled: signedIn,
  });
}

/** The session, and the current tenant's name from the user's tenant list. */
export function useSession() {
  const session = useQuery(sessionQueryOptions);
  const me = session.data ?? null;
  const tenants = useQuery(myTenantsQueryOptions(me !== null));
  const currentTenant = me?.tenantId ? (tenants.data?.find((t) => t.id === me.tenantId) ?? null) : null;

  return {
    me,
    isLoading: session.isLoading,
    error: session.error,
    currentTenant,
    tenants: tenants.data ?? [],
  };
}

/**
 * Call after anything that can change who is signed in or the current tenant (sign-in, set-tenant,
 * leave, logout). Everything else the app has loaded belonged to the old user or tenant, so it is
 * cleared (reset to empty and refetched by the pages still showing it), not only marked stale, so the
 * old tenant's data is never shown under the new one. The CSRF token is tied to the user, so it is
 * dropped too.
 */
export async function onSessionChanged(queryClient: QueryClient): Promise<void> {
  clearCsrfToken();
  await queryClient.resetQueries({ predicate: (query) => query.queryKey[0] !== sessionKey[0] });
  await queryClient.invalidateQueries({ queryKey: sessionKey });
}

/**
 * Call after a change to the session that keeps the user and the tenant (token refresh, a new
 * organization in the list): only the session queries are reloaded.
 */
export async function onSessionRefreshed(queryClient: QueryClient): Promise<void> {
  await queryClient.invalidateQueries({ queryKey: sessionKey });
}

/** Marks the session as signed out without another request (used when any call returns 401). */
export function markSignedOut(queryClient: QueryClient): void {
  queryClient.setQueryData(sessionKey, null);
}
