import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import { ErrorPanel } from '../../../components/ErrorPanel';
import { useStartSignIn } from '../useStartSignIn';

const MAX_NAME_LENGTH = 200;

/** Sign in and sign up, for a signed-out visitor. Each action sends the page to Keycloak. */
export function SignedOutPanel() {
  const startSignIn = useStartSignIn();
  const [organizationName, setOrganizationName] = useState('');
  const trimmed = organizationName.trim();
  const nameError = trimmed.length > MAX_NAME_LENGTH ? `At most ${MAX_NAME_LENGTH} characters.` : null;
  const busy = startSignIn.isPending;

  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="h5" gutterBottom>
        Not signed in
      </Typography>
      <Stack spacing={2} sx={{ maxWidth: 520 }}>
        <Stack direction="row" spacing={2}>
          <Button variant="contained" disabled={busy} onClick={() => startSignIn.mutate({ kind: 'login' })}>
            Sign in
          </Button>
          <Button variant="outlined" disabled={busy} onClick={() => startSignIn.mutate({ kind: 'login', forceLogin: true })}>
            Sign in and ask for my password
          </Button>
        </Stack>
        <Typography variant="body2" color="text.secondary">
          Keycloak signs you in without a form if it still has your session. "Ask for my password" makes it ask, for
          example to use a different account.
        </Typography>

        <Divider />

        <Typography variant="h6">Sign up</Typography>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'flex-start' }}>
          <TextField
            label="Organization name"
            size="small"
            value={organizationName}
            onChange={(event) => setOrganizationName(event.target.value)}
            error={nameError !== null}
            helperText={nameError ?? 'You become its TenantAdmin.'}
            sx={{ flexGrow: 1 }}
          />
          <Button
            variant="contained"
            disabled={busy || trimmed.length === 0 || nameError !== null}
            onClick={() => startSignIn.mutate({ kind: 'signup', organizationName: trimmed })}
          >
            Sign up and create it
          </Button>
        </Stack>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
          <Button variant="outlined" disabled={busy} onClick={() => startSignIn.mutate({ kind: 'signup' })}>
            Sign up without an organization
          </Button>
          <Typography variant="body2" color="text.secondary">
            Joins the default organization, if one is set.
          </Typography>
        </Stack>

        {startSignIn.isError && <ErrorPanel error={startSignIn.error} title="Could not start the sign-in" />}
      </Stack>
    </Paper>
  );
}
