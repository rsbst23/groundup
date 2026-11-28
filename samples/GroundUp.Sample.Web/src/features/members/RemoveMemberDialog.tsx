import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogTitle from '@mui/material/DialogTitle';
import type { TenantMemberDto } from '../../api/types';
import { memberName } from './membersQuery';

interface RemoveMemberDialogProps {
  member: TenantMemberDto | null;
  organizationName: string;
  busy: boolean;
  onConfirm: (member: TenantMemberDto) => void;
  onClose: () => void;
}

/** Asks before removing a member: their roles go and their sessions for the organization end. */
export function RemoveMemberDialog({ member, organizationName, busy, onConfirm, onClose }: RemoveMemberDialogProps) {
  return (
    <Dialog open={member !== null} onClose={busy ? undefined : onClose}>
      <DialogTitle>Remove member</DialogTitle>
      <DialogContent>
        <DialogContentText>
          {member && (
            <>
              Remove <strong>{memberName(member)}</strong> ({member.email}) from {organizationName}? Their roles are
              removed and their sessions for the organization end.
            </>
          )}
        </DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          Cancel
        </Button>
        <Button color="error" variant="contained" loading={busy} onClick={() => member && onConfirm(member)}>
          Remove
        </Button>
      </DialogActions>
    </Dialog>
  );
}
