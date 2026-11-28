import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useState } from 'react';
import type { CreateJoinLinkRequest } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { endOfLocalDayIso, todayLocal } from '../common/dates';
import { RoleSelect } from '../common/RoleSelect';
import { parseMaxUses } from './joinUrl';

interface CreateJoinLinkDialogProps {
  open: boolean;
  busy: boolean;
  error: unknown;
  onSubmit: (request: CreateJoinLinkRequest) => void;
  onClose: () => void;
}

/** Role, optional expiry and optional maximum number of uses for a new join link. */
export function CreateJoinLinkDialog({ open, busy, error, onSubmit, onClose }: CreateJoinLinkDialogProps) {
  const [roleId, setRoleId] = useState('');
  const [expiryDay, setExpiryDay] = useState('');
  const [maxUsesText, setMaxUsesText] = useState('');

  const expiresAt = expiryDay ? endOfLocalDayIso(expiryDay) : null;
  const expiryError = expiryDay && !expiresAt ? 'Not a valid date.' : null;
  const maxUses = parseMaxUses(maxUsesText);
  const ready = roleId !== '' && !expiryError && !maxUses.error;

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>Create a join link</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <RoleSelect value={roleId} onChange={setRoleId} helperText="The role people get when they join with the link." />
          <TextField
            label="Expires (optional)"
            size="small"
            type="date"
            value={expiryDay}
            onChange={(event) => setExpiryDay(event.target.value)}
            error={expiryError !== null}
            helperText={expiryError ?? 'The end of that day. Empty: does not expire.'}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: todayLocal() } }}
          />
          <TextField
            label="Maximum uses (optional)"
            size="small"
            value={maxUsesText}
            onChange={(event) => setMaxUsesText(event.target.value)}
            error={maxUses.error !== null}
            helperText={maxUses.error ?? 'Empty: unlimited.'}
            slotProps={{ htmlInput: { inputMode: 'numeric' } }}
          />
          {Boolean(error) && <ErrorPanel error={error} title="Could not create the link" />}
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
          onClick={() =>
            onSubmit({
              roleId,
              ...(expiresAt ? { expiresAt } : {}),
              ...(maxUses.value !== undefined ? { maxUses: maxUses.value } : {}),
            })
          }
        >
          Create link
        </Button>
      </DialogActions>
    </Dialog>
  );
}
