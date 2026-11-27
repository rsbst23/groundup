import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { ErrorPanel } from '../../../components/ErrorPanel';
import { useSessionActions } from '../useSessionActions';

/** The two ways to sign out, with what each one leaves behind. */
export function SignOutPanel() {
  const { signOut, signOutEverywhere } = useSessionActions();
  const busy = signOut.isPending || signOutEverywhere.isPending;

  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="h5" gutterBottom>
        Sign out
      </Typography>
      <Stack spacing={1.5} sx={{ maxWidth: 640 }}>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
          <Button variant="outlined" loading={signOut.isPending} disabled={busy} onClick={() => signOut.mutate()}>
            Sign out of this app
          </Button>
          <Typography variant="body2" color="text.secondary">
            Clears this app's session. Keycloak keeps its own, so the next sign-in may not ask for a password.
          </Typography>
        </Stack>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
          <Button
            variant="contained"
            color="error"
            loading={signOutEverywhere.isPending}
            disabled={busy}
            onClick={() => signOutEverywhere.mutate()}
          >
            Sign out everywhere
          </Button>
          <Typography variant="body2" color="text.secondary">
            Also ends the Keycloak session; Keycloak returns you to the home page.
          </Typography>
        </Stack>
        {signOut.isError && <ErrorPanel error={signOut.error} title="Sign-out failed" />}
        {signOutEverywhere.isError && <ErrorPanel error={signOutEverywhere.error} title="Sign-out failed" />}
      </Stack>
    </Paper>
  );
}
