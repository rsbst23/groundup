import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createOrganization, leaveTenant, logout, refreshSession, setTenant } from '../../api/auth';
import { onSessionChanged, onSessionRefreshed } from './sessionQuery';

/**
 * The signed-in actions. Each one that can change the user or the tenant calls onSessionChanged
 * (CSRF token dropped, everything loaded for the old tenant cleared, session reloaded); the others
 * only reload the session.
 */
export function useSessionActions() {
  const queryClient = useQueryClient();
  const changed = () => onSessionChanged(queryClient);
  const refreshed = () => onSessionRefreshed(queryClient);

  return {
    switchTenant: useMutation({ mutationFn: setTenant, onSuccess: changed }),
    // Same user and tenant, new token times
    refresh: useMutation({ mutationFn: refreshSession, onSuccess: refreshed }),
    // The session stays where it is; the list of organizations changes
    createOrganization: useMutation({ mutationFn: createOrganization, onSuccess: refreshed }),
    leave: useMutation({ mutationFn: leaveTenant, onSuccess: changed }),
    /** Clears the app's cookies. Keycloak's session stays, so the next sign-in may not ask for a password. */
    signOut: useMutation({ mutationFn: logout, onSuccess: changed }),
    /** Clears the app's cookies, then ends the Keycloak session too; Keycloak returns to the home page. */
    signOutEverywhere: useMutation({
      mutationFn: async () => {
        const { logoutUrl } = await logout();
        window.location.assign(logoutUrl);
        await new Promise<never>(() => undefined);
      },
    }),
  };
}
