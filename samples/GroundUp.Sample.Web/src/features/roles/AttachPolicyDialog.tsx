import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import type { TenantPolicyDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';

interface AttachPolicyDialogProps {
  roleName: string;
  /** The policies not attached yet; undefined while they load. */
  choices: TenantPolicyDto[] | undefined;
  open: boolean;
  busy: boolean;
  error: unknown;
  onSubmit: (policyId: string) => void;
  onClose: () => void;
}

/** Pick one of the organization's policies to attach to the role. */
export function AttachPolicyDialog({ roleName, choices, open, busy, error, onSubmit, onClose }: AttachPolicyDialogProps) {
  const [policyId, setPolicyId] = useState('');
  const chosen = choices?.find((p) => p.id === policyId);

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>Attach a policy to {roleName}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {choices?.length === 0 ? (
            <Typography color="text.secondary">Every policy is already attached. Create one on the Policies page.</Typography>
          ) : (
            <TextField
              select
              label="Policy"
              size="small"
              fullWidth
              value={policyId}
              onChange={(event) => setPolicyId(event.target.value)}
              disabled={choices === undefined}
              helperText={choices === undefined ? 'Loading the policies…' : 'People with the role get its permissions at once.'}
            >
              {(choices ?? []).map((policy) => (
                <MenuItem key={policy.id} value={policy.id}>
                  {policy.name} ({policy.permissionKeys.length} {policy.permissionKeys.length === 1 ? 'permission' : 'permissions'})
                </MenuItem>
              ))}
            </TextField>
          )}
          {chosen && chosen.permissionKeys.length > 0 && (
            <Typography variant="caption" color="text.secondary">
              Grants: {chosen.permissionKeys.join(', ')}
            </Typography>
          )}
          {Boolean(error) && <ErrorPanel error={error} title="Could not attach the policy" />}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          Cancel
        </Button>
        <Button variant="contained" loading={busy} disabled={!chosen} onClick={() => onSubmit(policyId)}>
          Attach
        </Button>
      </DialogActions>
    </Dialog>
  );
}
