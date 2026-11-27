import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import type { ReactNode } from 'react';
import type { MeDto, MyTenantDto } from '../../../api/types';
import { ErrorPanel } from '../../../components/ErrorPanel';
import { formatRemaining } from '../formatRemaining';
import { useNow } from '../useNow';
import { useSessionActions } from '../useSessionActions';

/** A UTC ISO time in local time, with the UTC value on hover. */
function Time({ value }: { value: string | null }) {
  if (!value) {
    return <Typography variant="body2" color="text.secondary">not in the token</Typography>;
  }
  return (
    <span title={value}>
      {new Date(value).toLocaleString()}
    </span>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <TableRow>
      <TableCell component="th" sx={{ width: 180, fontWeight: 500 }}>
        {label}
      </TableCell>
      <TableCell>{children}</TableCell>
    </TableRow>
  );
}

interface SessionDetailsProps {
  me: MeDto;
  currentTenant: MyTenantDto | null;
}

/** What /auth/me reports about the session (the token itself is an HttpOnly cookie the app cannot read). */
export function SessionDetails({ me, currentTenant }: SessionDetailsProps) {
  const { refresh } = useSessionActions();
  const now = useNow(Boolean(me.expiresAt));
  const remaining = me.expiresAt ? formatRemaining(me.expiresAt, now) : null;

  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="h5" gutterBottom>
        Session
      </Typography>
      <Table size="small" sx={{ mb: 2 }}>
        <TableBody>
          <Row label="User id">
            <code>{me.userId}</code>
          </Row>
          <Row label="Email">{me.email ?? '—'}</Row>
          <Row label="Name">{me.displayName ?? '—'}</Row>
          <Row label="Current tenant">
            {me.tenantId ? (
              <>
                {currentTenant?.name ?? 'Unknown organization'} <code>{me.tenantId}</code>
              </>
            ) : (
              <Typography variant="body2" color="warning.main">
                No current tenant
              </Typography>
            )}
          </Row>
          <Row label="Roles">
            {me.roles.length === 0 ? '—' : me.roles.map((role) => <Chip key={role} size="small" label={role} sx={{ mr: 0.5 }} />)}
          </Row>
          <Row label="Token issued">
            <Time value={me.issuedAt} />
          </Row>
          <Row label="Token expires">
            <Time value={me.expiresAt} /> {me.expiresAt && `(${remaining ? `in ${remaining}` : 'expired'})`}
          </Row>
          <Row label="Signed in with password">
            <Time value={me.authTime} />
          </Row>
        </TableBody>
      </Table>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
        <Button variant="outlined" loading={refresh.isPending} onClick={() => refresh.mutate()}>
          Refresh token
        </Button>
        <Typography variant="body2" color="text.secondary">
          Issues a new token for the same session; the sign-in time does not change.
        </Typography>
      </Stack>
      {refresh.isError && (
        <Stack sx={{ mt: 2 }}>
          <ErrorPanel error={refresh.error} title="Refresh failed" />
        </Stack>
      )}
    </Paper>
  );
}
