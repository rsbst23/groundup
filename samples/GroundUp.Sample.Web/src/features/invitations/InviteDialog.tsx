import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useState } from 'react';
import type { CreateInvitationRequest } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { endOfLocalDayIso, todayLocal } from '../common/dates';
import { RoleSelect } from '../common/RoleSelect';
import { looksLikeEmail } from './invitationsQuery';

interface InviteDialogProps {
  open: boolean;
  busy: boolean;
  error: unknown;
  onSubmit: (request: CreateInvitationRequest) => void;
  onClose: () => void;
}

/** Email, role and an optional earlier expiry for a new invitation. */
export function InviteDialog({ open, busy, error, onSubmit, onClose }: InviteDialogProps) {
  const [email, setEmail] = useState('');
  const [roleId, setRoleId] = useState('');
  const [expiryDay, setExpiryDay] = useState('');

  const emailError = email.trim() !== '' && !looksLikeEmail(email) ? 'Not an email address.' : null;
  const expiresAt = expiryDay ? endOfLocalDayIso(expiryDay) : null;
  const expiryError = expiryDay && !expiresAt ? 'Not a valid date.' : null;
  const ready = looksLikeEmail(email) && roleId !== '' && !expiryError;

  const close = () => {
    setEmail('');
    setRoleId('');
    setExpiryDay('');
    onClose();
  };

  return (
    <Dialog open={open} onClose={busy ? undefined : close} fullWidth maxWidth="sm">
      <DialogTitle>Invite someone</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label="Email"
            size="small"
            type="email"
            required
            autoFocus
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            error={emailError !== null}
            helperText={emailError ?? 'They get an email with a link to join. The link is never shown here.'}
          />
          <RoleSelect value={roleId} onChange={setRoleId} helperText="The role they get when they accept." />
          <TextField
            label="Expires (optional)"
            size="small"
            type="date"
            value={expiryDay}
            onChange={(event) => setExpiryDay(event.target.value)}
            error={expiryError !== null}
            helperText={expiryError ?? 'Only shortens the configured lifetime (7 days by default). The end of that day.'}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: todayLocal() } }}
          />
          {Boolean(error) && <ErrorPanel error={error} title="Could not send the invitation" />}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={close} disabled={busy}>
          Cancel
        </Button>
        <Button
          variant="contained"
          loading={busy}
          disabled={!ready}
          onClick={() => onSubmit({ email: email.trim(), roleId, ...(expiresAt ? { expiresAt } : {}) })}
        >
          Send invitation
        </Button>
      </DialogActions>
    </Dialog>
  );
}
