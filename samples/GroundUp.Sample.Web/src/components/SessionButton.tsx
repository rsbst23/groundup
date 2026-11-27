import Button from '@mui/material/Button';
import { useSession } from '../features/session/sessionQuery';
import { useSessionActions } from '../features/session/useSessionActions';
import { useStartSignIn } from '../features/session/useStartSignIn';

/**
 * Sign in (back to this page afterwards) or sign out everywhere (the app and Keycloak), from the
 * top bar on any page. The Session page also has "Sign out of this app" for testing that case.
 */
export function SessionButton() {
  const { me, isLoading } = useSession();
  const startSignIn = useStartSignIn();
  const { signOutEverywhere } = useSessionActions();

  if (isLoading) {
    return null;
  }

  return me ? (
    <Button
      color="inherit"
      size="small"
      loading={signOutEverywhere.isPending}
      title="Signs out of this app and ends the Keycloak session"
      onClick={() => signOutEverywhere.mutate()}
    >
      Sign out
    </Button>
  ) : (
    <Button color="inherit" size="small" loading={startSignIn.isPending} onClick={() => startSignIn.mutate({ kind: 'login' })}>
      Sign in
    </Button>
  );
}
