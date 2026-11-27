import Button from '@mui/material/Button';
import { useSession } from '../features/session/sessionQuery';
import { useSessionActions } from '../features/session/useSessionActions';
import { useStartSignIn } from '../features/session/useStartSignIn';

/** Sign in (back to this page afterwards) or sign out of this app, from the top bar on any page. */
export function SessionButton() {
  const { me, isLoading } = useSession();
  const startSignIn = useStartSignIn();
  const { signOut } = useSessionActions();

  if (isLoading) {
    return null;
  }

  return me ? (
    <Button color="inherit" size="small" loading={signOut.isPending} onClick={() => signOut.mutate()}>
      Sign out
    </Button>
  ) : (
    <Button color="inherit" size="small" loading={startSignIn.isPending} onClick={() => startSignIn.mutate({ kind: 'login' })}>
      Sign in
    </Button>
  );
}
