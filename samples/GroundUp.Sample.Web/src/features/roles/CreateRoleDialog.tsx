import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useState } from 'react';
import { RoleType, roleTypeLabel } from '../../api/labels';
import type { CreateRoleRequest } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { ROLE_DESCRIPTION_MAX, ROLE_NAME_MAX } from './roleText';

interface CreateRoleDialogProps {
  open: boolean;
  busy: boolean;
  error: unknown;
  onSubmit: (request: CreateRoleRequest) => void;
  onClose: () => void;
}

/** Name, description and type for a new role. A new role grants nothing until a policy is attached. */
export function CreateRoleDialog({ open, busy, error, onSubmit, onClose }: CreateRoleDialogProps) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [roleType, setRoleType] = useState<number>(RoleType.Application);
  const ready = name.trim() !== '' && name.trim().length <= ROLE_NAME_MAX && description.length <= ROLE_DESCRIPTION_MAX;

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>New role</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label="Name"
            size="small"
            required
            autoFocus
            value={name}
            onChange={(event) => setName(event.target.value)}
            slotProps={{ htmlInput: { maxLength: ROLE_NAME_MAX } }}
            helperText="Unique in the organization, ignoring case."
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
          <TextField
            select
            label="Type"
            size="small"
            value={String(roleType)}
            onChange={(event) => setRoleType(Number(event.target.value))}
          >
            {[RoleType.Application, RoleType.Workspace].map((value) => (
              <MenuItem key={value} value={String(value)}>
                {roleTypeLabel(value)}
              </MenuItem>
            ))}
          </TextField>
          {Boolean(error) && <ErrorPanel error={error} title="Could not create the role" />}
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
          onClick={() => onSubmit({ name: name.trim(), description: description.trim() || null, roleType })}
        >
          Create
        </Button>
      </DialogActions>
    </Dialog>
  );
}
