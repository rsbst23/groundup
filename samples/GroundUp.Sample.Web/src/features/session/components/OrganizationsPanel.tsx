import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogTitle from '@mui/material/DialogTitle';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import type { MyTenantDto } from '../../../api/types';
import { ErrorPanel } from '../../../components/ErrorPanel';
import { useSessionActions } from '../useSessionActions';

interface OrganizationsPanelProps {
  tenants: MyTenantDto[];
  currentTenantId: string | null;
  /** True on the tenant picker: no current tenant and something to choose. */
  choosing: boolean;
}

/** The user's organizations: switch to one, or leave one. */
export function OrganizationsPanel({ tenants, currentTenantId, choosing }: OrganizationsPanelProps) {
  const { switchTenant, leave } = useSessionActions();
  const [leaving, setLeaving] = useState<MyTenantDto | null>(null);

  const confirmLeave = () => {
    if (leaving) {
      leave.mutate(leaving.id, { onSettled: () => setLeaving(null) });
    }
  };

  return (
    <Paper sx={{ p: 3, ...(choosing ? { border: 2, borderColor: 'primary.main' } : {}) }}>
      <Typography variant="h5" gutterBottom>
        {choosing ? 'Choose an organization' : 'Your organizations'}
      </Typography>
      {tenants.length === 0 ? (
        <Typography color="text.secondary">You do not belong to any organization.</Typography>
      ) : (
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Name</TableCell>
              <TableCell>Slug</TableCell>
              <TableCell>Type</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {tenants.map((tenant) => {
              const isCurrent = tenant.id === currentTenantId;
              const canSwitch = tenant.switchable && tenant.type !== 'enterprise';
              return (
                <TableRow key={tenant.id} selected={isCurrent}>
                  <TableCell>
                    {tenant.name} {isCurrent && <Chip size="small" color="primary" label="current" sx={{ ml: 1 }} />}
                  </TableCell>
                  <TableCell>
                    <code>{tenant.slug}</code>
                  </TableCell>
                  <TableCell>{tenant.type}</TableCell>
                  <TableCell align="right">
                    <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end', alignItems: 'center' }}>
                      {!canSwitch ? (
                        <Typography variant="body2" color="text.secondary">
                          Not switchable here{tenant.subdomain ? ` (sign in on ${tenant.subdomain})` : ''}
                        </Typography>
                      ) : (
                        !isCurrent && (
                          <Button
                            size="small"
                            variant={choosing ? 'contained' : 'outlined'}
                            loading={switchTenant.isPending && switchTenant.variables === tenant.id}
                            disabled={switchTenant.isPending}
                            onClick={() => switchTenant.mutate(tenant.id)}
                          >
                            Switch
                          </Button>
                        )
                      )}
                      <Button size="small" color="error" onClick={() => setLeaving(tenant)}>
                        Leave
                      </Button>
                    </Stack>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      )}
      {switchTenant.isError && (
        <Stack sx={{ mt: 2 }}>
          <ErrorPanel error={switchTenant.error} title="Could not switch" />
        </Stack>
      )}
      {leave.isError && (
        <Stack sx={{ mt: 2 }}>
          <ErrorPanel error={leave.error} title="Could not leave" />
        </Stack>
      )}

      <Dialog open={leaving !== null} onClose={() => setLeaving(null)}>
        <DialogTitle>Leave {leaving?.name}?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            Your membership and roles in {leaving?.name} are removed. If it is your current organization, your session
            continues with no current tenant. The last TenantAdmin cannot leave.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setLeaving(null)}>Cancel</Button>
          <Button color="error" variant="contained" loading={leave.isPending} onClick={confirmLeave}>
            Leave
          </Button>
        </DialogActions>
      </Dialog>
    </Paper>
  );
}
