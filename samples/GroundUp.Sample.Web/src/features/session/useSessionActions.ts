import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createOrganization, leaveTenant, logout, refreshSession, setTenant } from '../../api/auth';
import { onSessionChanged } from './sessionQuery';

/**
 * The signed-in actions on the Session page. Each one that changes the user or the tenant calls
 * onSessionChanged, which drops the CSRF token (it is tied to the user) and reloads the session.
 */
export function useSessionActions() {
  const queryClient = useQueryClient();
  const changed = () => onSessionChanged(queryClient);

  return {
    switchTenant: useMutation({ mutationFn: setTenant, onSuccess: changed }),
    refresh: useMutation({ mutationFn: refreshSession, onSuccess: changed }),
    // The session stays where it is; the list of organizations changes
    createOrganization: useMutation({ mutationFn: createOrganization, onSuccess: changed }),
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
