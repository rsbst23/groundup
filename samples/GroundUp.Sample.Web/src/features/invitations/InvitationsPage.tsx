import Alert from '@mui/material/Alert';
import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { useState } from 'react';
import { InvitationStatus, invitationStatusLabel } from '../../api/labels';
import type { InvitationDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { PermissionButton } from '../access/PermissionButton';
import { PermissionKeys } from '../access/permissions';
import { RequirePermission } from '../access/RequirePermission';
import { RequireTenant } from '../access/RequireTenant';
import { ListTable, type ListColumn, type StatusOption } from '../list/ListTable';
import { useListView, usePageBack } from '../list/useListView';
import { ConfirmDialog } from '../common/ConfirmDialog';
import { formatDateTime } from '../common/dates';
import { useSession } from '../session/sessionQuery';
import { canResendOrRevoke, invitationsQueryOptions, useInvitationActions } from './invitationsQuery';
import { InviteDialog } from './InviteDialog';

/** The current organization's invitations: send, resend, revoke. */
export function InvitationsPage() {
  return (
    <RequireTenant purpose="invite people to it">
      <RequirePermission permission={PermissionKeys.InvitationsSend}>
        <InvitationsTable />
      </RequirePermission>
    </RequireTenant>
  );
}

const statusColor: Record<number, 'warning' | 'success' | 'default'> = {
  [InvitationStatus.Pending]: 'warning',
  [InvitationStatus.Accepted]: 'success',
};

/** Every status the list shows; Expired is a Pending invitation past its expiry. */
const invitationStatusOptions: StatusOption[] = Object.values(InvitationStatus).map((value) => ({
  value,
  label: invitationStatusLabel(value),
}));

function InvitationsTable() {
  const { me, currentTenant } = useSession();
  const tenantId = me!.tenantId!;
  const organizationName = currentTenant?.name ?? 'this organization';
  const navigate = useNavigate({ from: '/invitations' });
  const view = useListView(useSearch({ from: '/invitations' }), (search, replace) => void navigate({ search, replace }));
  const [inviting, setInviting] = useState(false);
  const [dialogKey, setDialogKey] = useState(0);
  const [revoking, setRevoking] = useState<InvitationDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionError, setActionError] = useState<{ title: string; error: unknown } | null>(null);

  const invitations = useQuery(invitationsQueryOptions(tenantId, view.state));
  usePageBack(invitations.data, invitations.isPlaceholderData, view);
  const actions = useInvitationActions();
  const busy = actions.resend.isPending || actions.revoke.isPending;

  const columns: ListColumn<InvitationDto>[] = [
    { label: 'Email', sortField: 'email', render: (invitation) => invitation.email },
    { label: 'Role', sortField: 'roleName', render: (invitation) => invitation.roleName ?? '—' },
    {
      label: 'Status',
      sortField: 'status',
      render: (invitation) => (
        <Chip
          size="small"
          variant="outlined"
          label={invitationStatusLabel(invitation.status)}
          color={statusColor[invitation.status] ?? 'default'}
        />
      ),
    },
    { label: 'Expires', sortField: 'expiresAt', render: (invitation) => formatDateTime(invitation.expiresAt) },
    { label: 'Created', sortField: 'createdAt', render: (invitation) => formatDateTime(invitation.createdAt) },
    {
      label: 'Actions',
      align: 'right',
      render: (invitation) =>
        canResendOrRevoke(invitation) && (
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
        ),
    },
  ];

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
        <ListTable
          columns={columns}
          rows={invitations.data?.items}
          rowKey={(invitation) => invitation.id}
          totalRecords={invitations.data?.totalRecords ?? 0}
          state={view.state}
          onChange={view.change}
          searchLabel="Search email"
          statusOptions={invitationStatusOptions}
          emptyText="No invitations yet."
        />
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
                  (import.meta.env.DEV ? ' In Development no email goes out: open the Mailbox page to see it and its link.' : ''),
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
