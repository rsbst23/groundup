import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemText from '@mui/material/ListItemText';
import Typography from '@mui/material/Typography';
import { useSyncExternalStore } from 'react';
import { closeTenantPicker, isTenantPickerOpen, subscribeTenantPicker } from '../features/session/pickerRequest';
import { useSession } from '../features/session/sessionQuery';
import { useSessionActions } from '../features/session/useSessionActions';
import { ErrorPanel } from './ErrorPanel';

/**
 * The app-wide organization picker. It opens on its own right after a sign-in that needs a choice
 * (the user stays on the page they started from), and from the banner's "Choose organization"
 * button. Choosing switches the tenant; the page then reloads its data for that organization.
 * "Not now" leaves the session with no current tenant.
 */
export function TenantPickerDialog() {
  const requested = useSyncExternalStore(subscribeTenantPicker, isTenantPickerOpen);
  const { me, tenants } = useSession();
  const { switchTenant } = useSessionActions();

  // Only meaningful while signed in with something to choose from
  const open = requested && me !== null && tenants.length > 0;

  const choose = (tenantId: string) =>
    switchTenant.mutate(tenantId, { onSuccess: () => closeTenantPicker() });

  return (
    <Dialog open={open} onClose={closeTenantPicker} fullWidth maxWidth="xs">
      <DialogTitle>Choose an organization</DialogTitle>
      <DialogContent>
        <List>
          {tenants.map((tenant) => {
            const switchable = tenant.switchable && tenant.type !== 'enterprise';
            const isCurrent = tenant.id === me?.tenantId;
            return switchable ? (
              <ListItemButton
                key={tenant.id}
                selected={isCurrent}
                disabled={switchTenant.isPending}
                onClick={() => choose(tenant.id)}
              >
                <ListItemText primary={tenant.name} secondary={isCurrent ? 'current' : tenant.slug} />
              </ListItemButton>
            ) : (
              <ListItem key={tenant.id}>
                <ListItemText
                  primary={tenant.name}
                  secondary={`Not switchable here${tenant.subdomain ? ` (sign in on ${tenant.subdomain})` : ''}`}
                />
              </ListItem>
            );
          })}
        </List>
        {switchTenant.isError && <ErrorPanel error={switchTenant.error} title="Could not switch" />}
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
          Without an organization, pages with organization data show TENANT_REQUIRED.
        </Typography>
      </DialogContent>
      <DialogActions>
        <Button onClick={closeTenantPicker}>Not now</Button>
      </DialogActions>
    </Dialog>
  );
}
