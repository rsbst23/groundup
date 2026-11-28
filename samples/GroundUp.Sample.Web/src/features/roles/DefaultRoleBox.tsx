import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { ErrorPanel } from '../../components/ErrorPanel';
import { PermissionButton } from '../access/PermissionButton';
import { PermissionKeys } from '../access/permissions';
import { ConfirmDialog } from '../common/ConfirmDialog';
import { rolesQueryOptions } from '../members/membersQuery';
import { defaultRoleText, defaultRoleChoices } from './roleText';
import { defaultRoleQueryOptions, useRoleActions } from './rolesQuery';

/**
 * The tenant's default role (GU-95): none unless set. People who join the organization by signing
 * in get it; invitations and join links grant their own role.
 */
export function DefaultRoleBox({ tenantId }: { tenantId: string }) {
  const current = useQuery(defaultRoleQueryOptions(tenantId));
  const roles = useQuery(rolesQueryOptions(tenantId, true));
  const actions = useRoleActions();
  const [choosing, setChoosing] = useState(false);
  const [choice, setChoice] = useState('');
  const [clearing, setClearing] = useState(false);
  const [error, setError] = useState<unknown>(null);

  if (current.isError) {
    return <ErrorPanel error={current.error} title="Could not load the default role" />;
  }

  return (
    <Stack spacing={1} sx={{ my: 2 }}>
      <Alert
        severity="info"
        action={
          <Stack direction="row" spacing={1}>
            <PermissionButton
              permission={PermissionKeys.RolesWrite}
              size="small"
              color="inherit"
              disabled={current.isPending}
              onClick={() => {
                setError(null);
                setChoice(current.data?.id ?? '');
                setChoosing(true);
              }}
            >
              {current.data ? 'Change' : 'Set'}
            </PermissionButton>
            {current.data && (
              <PermissionButton
                permission={PermissionKeys.RolesWrite}
                size="small"
                color="inherit"
                onClick={() => {
                  setError(null);
                  setClearing(true);
                }}
              >
                Clear
              </PermissionButton>
            )}
          </Stack>
        }
      >
        {current.isPending ? 'Loading the default role…' : defaultRoleText(current.data)}
      </Alert>
      {Boolean(error) && <ErrorPanel error={error} title="Could not change the default role" />}

      <Dialog open={choosing} onClose={actions.setDefault.isPending ? undefined : () => setChoosing(false)} fullWidth maxWidth="xs">
        <DialogTitle>Default role</DialogTitle>
        <DialogContent>
          <TextField
            select
            label="Role"
            size="small"
            fullWidth
            sx={{ mt: 1 }}
            value={choice}
            onChange={(event) => setChoice(event.target.value)}
            helperText="TenantAdmin cannot be the default role."
          >
            {defaultRoleChoices(roles.data ?? []).map((role) => (
              <MenuItem key={role.id} value={role.id}>
                {role.name}
              </MenuItem>
            ))}
          </TextField>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setChoosing(false)} disabled={actions.setDefault.isPending}>
            Cancel
          </Button>
          <Button
            variant="contained"
            loading={actions.setDefault.isPending}
            disabled={choice === '' || choice === current.data?.id}
            onClick={() =>
              actions.setDefault.mutate(choice, {
                onError: setError,
                onSettled: () => setChoosing(false),
              })
            }
          >
            Save
          </Button>
        </DialogActions>
      </Dialog>

      <ConfirmDialog
        open={clearing}
        title="Clear the default role"
        confirmLabel="Clear"
        busy={actions.clearDefault.isPending}
        onClose={() => setClearing(false)}
        onConfirm={() =>
          actions.clearDefault.mutate(undefined, { onError: setError, onSettled: () => setClearing(false) })
        }
      >
        New members who join by signing in will get no role. People who already have <strong>{current.data?.name}</strong>{' '}
        keep it.
      </ConfirmDialog>
    </Stack>
  );
}
