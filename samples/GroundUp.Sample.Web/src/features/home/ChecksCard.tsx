import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { refreshSession } from '../../api/auth';
import { apiFetch } from '../../api/client';
import { ErrorPanel } from '../../components/ErrorPanel';
import { onSessionRefreshed } from '../session/sessionQuery';

/**
 * Two calls for checking the scaffold before real pages exist (testing manual, Part 1):
 * a tenant endpoint (to see an error with its code and correlation ID) and a cookie POST
 * (to see the CSRF token added without any manual step).
 */
export function ChecksCard() {
  const tenantCall = useMutation({ mutationFn: () => apiFetch<unknown>('/api/members') });
  const queryClient = useQueryClient();
  // A refresh issues a new token, so the banner's times change.
  const cookiePost = useMutation({ mutationFn: refreshSession, onSuccess: () => onSessionRefreshed(queryClient) });

  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="h6" gutterBottom>
        Checks
      </Typography>
      <Stack spacing={2}>
        <Stack direction="row" spacing={2}>
          <Button variant="outlined" onClick={() => tenantCall.mutate()} loading={tenantCall.isPending}>
            Call a tenant endpoint (GET /api/members)
          </Button>
          <Button variant="outlined" onClick={() => cookiePost.mutate()} loading={cookiePost.isPending}>
            Send a cookie POST (POST /auth/refresh)
          </Button>
        </Stack>
        {tenantCall.isSuccess && <Alert severity="success">GET /api/members succeeded.</Alert>}
        {tenantCall.isError && <ErrorPanel error={tenantCall.error} title="GET /api/members" />}
        {cookiePost.isSuccess && <Alert severity="success">POST /auth/refresh succeeded (the CSRF token was sent).</Alert>}
        {cookiePost.isError && <ErrorPanel error={cookiePost.error} title="POST /auth/refresh" />}
      </Stack>
    </Paper>
  );
}
