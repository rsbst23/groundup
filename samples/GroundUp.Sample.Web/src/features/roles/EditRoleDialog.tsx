import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useState } from 'react';
import type { TenantRoleDto, UpdateRoleRequest } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { ROLE_DESCRIPTION_MAX, ROLE_NAME_MAX } from './roleText';

interface EditRoleDialogProps {
  role: TenantRoleDto;
  open: boolean;
  busy: boolean;
  error: unknown;
  onSubmit: (request: UpdateRoleRequest) => void;
  onClose: () => void;
}

/** Rename a role and change its description. TenantAdmin keeps its name; its description can change. */
export function EditRoleDialog({ role, open, busy, error, onSubmit, onClose }: EditRoleDialogProps) {
  const [name, setName] = useState(role.name);
  const [description, setDescription] = useState(role.description ?? '');
  const changed = name.trim() !== role.name || (description.trim() || null) !== role.description;
  const ready = changed && name.trim() !== '' && name.trim().length <= ROLE_NAME_MAX;

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>Edit role</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label="Name"
            size="small"
            required
            autoFocus={!role.isSystem}
            value={name}
            onChange={(event) => setName(event.target.value)}
            disabled={role.isSystem}
            slotProps={{ htmlInput: { maxLength: ROLE_NAME_MAX } }}
            helperText={role.isSystem ? 'The built-in role keeps its name.' : 'Unique in the organization, ignoring case.'}
          />
          <TextField
            label="Description (optional)"
            size="small"
            multiline
            minRows={2}
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            slotProps={{ htmlInput: { maxLength: ROLE_DESCRIPTION_MAX } }}
          />
          {Boolean(error) && <ErrorPanel error={error} title="Could not save the role" />}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          Cancel
        </Button>
        <Button
          variant="contained"
          loading={busy}
          disabled={!ready}
          onClick={() => onSubmit({ name: role.isSystem ? role.name : name.trim(), description: description.trim() || null })}
        >
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}
