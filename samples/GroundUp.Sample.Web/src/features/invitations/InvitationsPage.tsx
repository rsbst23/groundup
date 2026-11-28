import Alert from '@mui/material/Alert';
import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TablePagination from '@mui/material/TablePagination';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { InvitationStatus, invitationStatusLabel } from '../../api/labels';
import { PAGE_SIZE } from '../../api/paging';
import type { InvitationDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { PermissionButton } from '../access/PermissionButton';
import { PermissionKeys } from '../access/permissions';
import { RequireTenant } from '../access/RequireTenant';
import { ConfirmDialog } from '../common/ConfirmDialog';
import { formatDateTime } from '../common/dates';
import { useSession } from '../session/sessionQuery';
import { canResendOrRevoke, invitationsQueryOptions, useInvitationActions } from './invitationsQuery';
import { InviteDialog } from './InviteDialog';

/** The current organization's invitations: send, resend, revoke. */
export function InvitationsPage() {
  return (
    <RequireTenant purpose="invite people to it">
      <InvitationsTable />
    </RequireTenant>
  );
}

const statusColor: Record<number, 'warning' | 'success' | 'default'> = {
  [InvitationStatus.Pending]: 'warning',
  [InvitationStatus.Accepted]: 'success',
};

function InvitationsTable() {
  const { me, currentTenant } = useSession();
  const tenantId = me!.tenantId!;
  const organizationName = currentTenant?.name ?? 'this organization';
  const [page, setPage] = useState(1);
  const [inviting, setInviting] = useState(false);
  const [dialogKey, setDialogKey] = useState(0);
  const [revoking, setRevoking] = useState<InvitationDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionError, setActionError] = useState<{ title: string; error: unknown } | null>(null);

  const invitations = useQuery(invitationsQueryOptions(tenantId, page));
  const actions = useInvitationActions();
  const busy = actions.resend.isPending || actions.revoke.isPending;

  return (
    <Paper sx={{ p: 3 }}>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="h5">Invitations to {organizationName}</Typography>
        <PermissionButton
          permission={PermissionKeys.InvitationsSend}
          variant="contained"
          onClick={() => {
            actions.create.reset();
            setDialogKey((k) => k + 1);
            setInviting(true);
          }}
        >
          Invite
        </PermissionButton>
      </Stack>
      <Typography color="text.secondary" gutterBottom>
        An invitation adds a pending member until it is accepted. Resend sends a new link (the old one stops working);
        revoke withdraws it and removes the pending member.
      </Typography>

      {notice && (
        <Alert severity="success" onClose={() => setNotice(null)} sx={{ my: 2 }}>
          {notice}
        </Alert>
      )}
      {actionError && (
        <Stack sx={{ my: 2 }}>
          <ErrorPanel error={actionError.error} title={actionError.title} />
        </Stack>
      )}

      {invitations.isError ? (
        <ErrorPanel error={invitations.error} title="Could not load the invitations" />
      ) : (
        <>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Email</TableCell>
                <TableCell>Role</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Expires</TableCell>
                <TableCell>Created</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {invitations.isPending && (
                <TableRow>
                  <TableCell colSpan={6}>Loading…</TableCell>
                </TableRow>
              )}
              {invitations.data?.items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6}>No invitations yet.</TableCell>
                </TableRow>
              )}
              {invitations.data?.items.map((invitation) => (
                <TableRow key={invitation.id}>
                  <TableCell>{invitation.email}</TableCell>
                  <TableCell>{invitation.roleName ?? '—'}</TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      variant="outlined"
                      label={invitationStatusLabel(invitation.status)}
                      color={statusColor[invitation.status] ?? 'default'}
                    />
                  </TableCell>
                  <TableCell>{formatDateTime(invitation.expiresAt)}</TableCell>
                  <TableCell>{formatDateTime(invitation.createdAt)}</TableCell>
                  <TableCell align="right">
                    {canResendOrRevoke(invitation) && (
                      <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                        <PermissionButton
                          permission={PermissionKeys.InvitationsSend}
                          size="small"
                          disabled={busy}
                          onClick={() => {
                            setActionError(null);
                            actions.resend.mutate(invitation, {
                              onSuccess: () => setNotice(`A new invitation was sent to ${invitation.email}. The old link no longer works.`),
                              onError: (error) => setActionError({ title: `Could not resend to ${invitation.email}`, error }),
                            });
                          }}
                        >
                          Resend
                        </PermissionButton>
                        <PermissionButton
                          permission={PermissionKeys.InvitationsSend}
                          size="small"
                          color="error"
                          disabled={busy}
                          onClick={() => setRevoking(invitation)}
                        >
                          Revoke
                        </PermissionButton>
                      </Stack>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {invitations.data && (
            <TablePagination
              component="div"
              count={invitations.data.totalRecords}
              page={page - 1}
              rowsPerPage={PAGE_SIZE}
              rowsPerPageOptions={[PAGE_SIZE]}
              onPageChange={(_, next) => setPage(next + 1)}
            />
          )}
        </>
      )}

      <InviteDialog
        key={dialogKey}
        open={inviting}
        busy={actions.create.isPending}
        error={actions.create.error}
        onClose={() => setInviting(false)}
        onSubmit={(request) =>
          actions.create.mutate(request, {
            onSuccess: () => {
              setInviting(false);
              setActionError(null);
              setNotice(
                `Invitation sent to ${request.email}.` +
                  (import.meta.env.DEV ? ' In Development no email goes out: the link is in the API\'s log.' : ''),
              );
            },
          })
        }
      />

      <ConfirmDialog
        open={revoking !== null}
        title="Revoke invitation"
        confirmLabel="Revoke"
        destructive
        busy={actions.revoke.isPending}
        onClose={() => setRevoking(null)}
        onConfirm={() =>
          revoking &&
          actions.revoke.mutate(revoking, {
            onSuccess: () => setNotice(`The invitation to ${revoking.email} was revoked.`),
            onError: (error) => setActionError({ title: `Could not revoke the invitation to ${revoking.email}`, error }),
            onSettled: () => setRevoking(null),
          })
        }
      >
        Revoke the invitation to <strong>{revoking?.email}</strong>? Its link stops working, and if they have not joined
        yet their pending membership is removed.
      </ConfirmDialog>
    </Paper>
  );
}
