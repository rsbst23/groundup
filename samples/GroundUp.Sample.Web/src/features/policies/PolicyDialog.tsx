import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { PolicyDetailDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { RouterLink } from '../../components/RouterLink';
import { keysChanged, moduleState, toggleKey, toggleModule } from './permissionChoices';
import { permissionCatalogQueryOptions } from './policiesQuery';

/** The API's limits (CreatePolicyDtoValidator, UpdatePolicyDtoValidator). */
const NAME_MAX = 200;
const DESCRIPTION_MAX = 1000;

export interface PolicyDialogValues {
  name: string;
  description: string | null;
  keys: Set<string>;
}

interface PolicyDialogProps {
  /** The policy being edited, or null for a new one. */
  policy: PolicyDetailDto | null;
  open: boolean;
  busy: boolean;
  error: unknown;
  /** False when the session lacks auth.permissions.write: the dialog is read-only. */
  canWrite: boolean;
  onSubmit: (values: PolicyDialogValues) => void;
  onClose: () => void;
}

/** Name, description and the permissions (the catalogue by module) of a new or existing policy. */
export function PolicyDialog({ policy, open, busy, error, canWrite, onSubmit, onClose }: PolicyDialogProps) {
  const catalog = useQuery(permissionCatalogQueryOptions());
  const [name, setName] = useState(policy?.name ?? '');
  const [description, setDescription] = useState(policy?.description ?? '');
  const [keys, setKeys] = useState<Set<string>>(() => new Set(policy?.permissions.map((p) => p.key) ?? []));

  const trimmedName = name.trim();
  const trimmedDescription = description.trim() || null;
  const changed =
    policy === null ||
    trimmedName !== policy.name ||
    trimmedDescription !== policy.description ||
    keysChanged(policy.permissions.map((p) => p.key), keys);
  const ready = canWrite && changed && trimmedName !== '' && trimmedName.length <= NAME_MAX;

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth="md">
      <DialogTitle>{policy ? `Policy: ${policy.name}` : 'New policy'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label="Name"
            size="small"
            required
            autoFocus
            value={name}
            onChange={(event) => setName(event.target.value)}
            disabled={!canWrite}
            slotProps={{ htmlInput: { maxLength: NAME_MAX } }}
            helperText="Unique in the organization, ignoring case."
          />
          <TextField
            label="Description (optional)"
            size="small"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            disabled={!canWrite}
            slotProps={{ htmlInput: { maxLength: DESCRIPTION_MAX } }}
          />

          <Typography variant="subtitle1">Permissions ({keys.size})</Typography>
          {catalog.isError && <ErrorPanel error={catalog.error} title="Could not load the permissions" />}
          {catalog.isPending && <Typography color="text.secondary">Loading the permissions…</Typography>}
          {catalog.data?.map((module) => {
            const state = moduleState(module, keys);
            return (
              <Box key={module.module}>
                <FormControlLabel
                  label={<Typography sx={{ fontWeight: 600 }}>{module.module}</Typography>}
                  control={
                    <Checkbox
                      size="small"
                      checked={state === 'all'}
                      indeterminate={state === 'some'}
                      disabled={!canWrite}
                      onChange={() => setKeys((current) => toggleModule(module, current))}
                    />
                  }
                />
                <Box sx={{ pl: 4, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' } }}>
                  {module.permissions.map((permission) => (
                    <FormControlLabel
                      key={permission.key}
                      control={
                        <Checkbox
                          size="small"
                          checked={keys.has(permission.key)}
                          disabled={!canWrite}
                          onChange={() => setKeys((current) => toggleKey(current, permission.key))}
                        />
                      }
                      label={
                        <Box>
                          <Typography variant="body2">{permission.name}</Typography>
                          <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>
                            {permission.key}
                          </Typography>
                        </Box>
                      }
                    />
                  ))}
                </Box>
              </Box>
            );
          })}

          {policy && (
            <Typography variant="body2" color="text.secondary">
              {policy.roles.length === 0 ? 'No role uses this policy.' : 'Used by: '}
              {policy.roles.map((role, index) => (
                <span key={role.id}>
                  {index > 0 && ', '}
                  <RouterLink to="/roles/$roleId" params={{ roleId: role.id }} onClick={onClose}>
                    {role.name}
                  </RouterLink>
                </span>
              ))}
            </Typography>
          )}

          {Boolean(error) && <ErrorPanel error={error} title="Could not save the policy" />}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          {canWrite ? 'Cancel' : 'Close'}
        </Button>
        {canWrite && (
          <Button
            variant="contained"
            loading={busy}
            disabled={!ready}
            onClick={() => onSubmit({ name: trimmedName, description: trimmedDescription, keys })}
          >
            {policy ? 'Save' : 'Create'}
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}
