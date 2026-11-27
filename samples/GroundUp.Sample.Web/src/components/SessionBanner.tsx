import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import { Link } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { formatRemaining } from '../features/session/formatRemaining';
import { useSession } from '../features/session/sessionQuery';

function useNow(enabled: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!enabled) {
      return undefined;
    }
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [enabled]);
  return now;
}

/** Who is signed in, the current tenant, roles and the token's expiry, on every page. */
export function SessionBanner() {
  const { me, currentTenant, isLoading } = useSession();
  const now = useNow(Boolean(me?.expiresAt));

  if (isLoading) {
    return null;
  }

  if (!me) {
    return (
      <Paper variant="outlined" sx={{ px: 2, py: 1, mb: 2 }}>
        <Typography variant="body2">
          Not signed in. <Link to="/session">Sign in</Link>
        </Typography>
      </Paper>
    );
  }

  const remaining = me.expiresAt ? formatRemaining(me.expiresAt, now) : null;
  const tenantLabel = me.tenantId ? (currentTenant?.name ?? me.tenantId) : 'No current tenant';

  return (
    <Paper variant="outlined" sx={{ px: 2, py: 1, mb: 2 }}>
      <Box sx={{ display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography variant="body2">
          <strong>{me.email ?? me.userId}</strong>
        </Typography>
        <Typography variant="body2" color={me.tenantId ? 'text.primary' : 'warning.main'}>
          Tenant: {tenantLabel}
        </Typography>
        <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap' }}>
          {me.roles.length === 0 ? (
            <Typography variant="body2" color="text.secondary">
              No roles
            </Typography>
          ) : (
            me.roles.map((role) => <Chip key={role} size="small" label={role} />)
          )}
        </Box>
        <Box sx={{ flexGrow: 1 }} />
        {me.expiresAt && (
          <Typography variant="body2" color={remaining ? 'text.secondary' : 'warning.main'} title={me.expiresAt}>
            {remaining ? `Token expires in ${remaining}` : 'Token expired'}
          </Typography>
        )}
      </Box>
    </Paper>
  );
}
