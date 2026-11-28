import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import type { ReactNode } from 'react';
import { SignedOutPanel } from '../session/components/SignedOutPanel';
import { requestTenantPicker } from '../session/pickerRequest';
import { useSession } from '../session/sessionQuery';

interface RequireTenantProps {
  /** What the page manages, for the message: "Choose an organization to manage its members." */
  purpose: string;
  children: ReactNode;
}

/**
 * Shows the page only with a signed-in session that has a current tenant: the admin APIs work on
 * the current tenant. Signed out it shows the sign-in panel; with no current tenant, the picker.
 */
export function RequireTenant({ purpose, children }: RequireTenantProps) {
  const { me, isLoading } = useSession();
  if (isLoading) {
    return null;
  }
  if (!me) {
    return <SignedOutPanel />;
  }
  if (!me.tenantId) {
    return (
      <Paper sx={{ p: 3 }}>
        <Typography variant="h5" gutterBottom>
          No current organization
        </Typography>
        <Typography gutterBottom>Choose an organization to {purpose}.</Typography>
        <Button variant="contained" onClick={requestTenantPicker}>
          Choose organization
        </Button>
      </Paper>
    );
  }
  return <>{children}</>;
}
