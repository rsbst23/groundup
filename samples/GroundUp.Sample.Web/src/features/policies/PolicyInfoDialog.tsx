import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useState } from 'react';
import type { UpdatePolicyRequest } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';

/** The API's limits (CreatePolicyDtoValidator, UpdatePolicyDtoValidator). */
const NAME_MAX = 200;
const DESCRIPTION_MAX = 1000;

interface PolicyInfoDialogProps {
  title: string;
  submitLabel: string;
  /** The current values when editing; empty for a new policy. */
  initial?: UpdatePolicyRequest;
  open: boolean;
  busy: boolean;
  error: unknown;
  onSubmit: (request: UpdatePolicyRequest) => void;
  onClose: () => void;
}

/** A policy's name and description (new or edited). Permissions are chosen on the policy's page. */
export function PolicyInfoDialog({ title, submitLabel, initial, open, busy, error, onSubmit, onClose }: PolicyInfoDialogProps) {
  const [name, setName] = useState(initial?.name ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const trimmedName = name.trim();
  const trimmedDescription = description.trim() || null;
  const changed = !initial || trimmedName !== initial.name || trimmedDescription !== (initial.description ?? null);
  const ready = changed && trimmedName !== '' && trimmedName.length <= NAME_MAX;

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label="Name"
            size="small"
            required
            autoFocus
            value={name}
            onChange={(event) => setName(event.target.value)}
            slotProps={{ htmlInput: { maxLength: NAME_MAX } }}
            helperText="Unique in the organization, ignoring case."
          />
          <TextField
            label="Description (optional)"
            size="small"
            multiline
            minRows={2}
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            slotProps={{ htmlInput: { maxLength: DESCRIPTION_MAX } }}
          />
          {Boolean(error) && <ErrorPanel error={error} title="Could not save the policy" />}
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
          onClick={() => onSubmit({ name: trimmedName, description: trimmedDescription })}
        >
          {submitLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
