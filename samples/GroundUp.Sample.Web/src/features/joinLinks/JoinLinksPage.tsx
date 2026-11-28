import Alert from '@mui/material/Alert';
import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { useState } from 'react';
import { JoinLinkStatus, joinLinkStatusLabel } from '../../api/labels';
import type { JoinLinkDto } from '../../api/types';
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
import { CreateJoinLinkDialog } from './CreateJoinLinkDialog';
import { isActive, joinLinksQueryOptions, useJoinLinkActions } from './joinLinksQuery';
import { joinUrl, usesText } from './joinUrl';
import { ShowLinkDialog } from './ShowLinkDialog';

/** The current organization's join links: create, regenerate, revoke. */
export function JoinLinksPage() {
  return (
    <RequireTenant purpose="manage its join links">
      <RequirePermission permission={PermissionKeys.JoinLinksManage}>
        <JoinLinksTable />
      </RequirePermission>
    </RequireTenant>
  );
}

type Confirming = { kind: 'regenerate' | 'revoke'; link: JoinLinkDto } | null;

/** Every status the list shows; Expired and Used up are worked out from the expiry and the uses. */
const joinLinkStatusOptions: StatusOption[] = Object.values(JoinLinkStatus).map((value) => ({
  value,
  label: joinLinkStatusLabel(value),
}));

function JoinLinksTable() {
  const { me, currentTenant } = useSession();
  const tenantId = me!.tenantId!;
  const organizationName = currentTenant?.name ?? 'this organization';
  const navigate = useNavigate({ from: '/join-links' });
  const view = useListView(useSearch({ from: '/join-links' }), (search, replace) => void navigate({ search, replace }));
  const [creating, setCreating] = useState(false);
  const [dialogKey, setDialogKey] = useState(0);
  const [shownUrl, setShownUrl] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<Confirming>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionError, setActionError] = useState<{ title: string; error: unknown } | null>(null);

  const links = useQuery(joinLinksQueryOptions(tenantId, view.state));
  usePageBack(links.data, links.isPlaceholderData, view);
  const actions = useJoinLinkActions();
  const busy = actions.regenerate.isPending || actions.revoke.isPending;

  const columns: ListColumn<JoinLinkDto>[] = [
    { label: 'Role', sortField: 'roleName', render: (link) => link.roleName ?? '—' },
    {
      label: 'Status',
      sortField: 'status',
      render: (link) => (
        <Chip
          size="small"
          variant="outlined"
          label={joinLinkStatusLabel(link.status)}
          color={isActive(link) ? 'success' : 'default'}
        />
      ),
    },
    { label: 'Uses', sortField: 'useCount', render: (link) => usesText(link.useCount, link.maxUses) },
    {
      label: 'Expires',
      sortField: 'expiresAt',
      render: (link) => (link.expiresAt ? formatDateTime(link.expiresAt) : 'Never'),
    },
    { label: 'Created', sortField: 'createdAt', render: (link) => formatDateTime(link.createdAt) },
    {
      label: 'Actions',
      align: 'right',
      render: (link) =>
        isActive(link) && (
          <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
            <PermissionButton
              permission={PermissionKeys.JoinLinksManage}
              size="small"
              disabled={busy}
              onClick={() => setConfirming({ kind: 'regenerate', link })}
            >
              Regenerate
            </PermissionButton>
            <PermissionButton
              permission={PermissionKeys.JoinLinksManage}
              size="small"
              color="error"
              disabled={busy}
              onClick={() => setConfirming({ kind: 'revoke', link })}
            >
              Revoke
            </PermissionButton>
          </Stack>
        ),
    },
  ];

  const confirm = () => {
    if (!confirming) {
      return;
    }
    const { kind, link } = confirming;
    const role = link.roleName ?? 'its role';
    setActionError(null);
    if (kind === 'regenerate') {
      actions.regenerate.mutate(link, {
        onSuccess: (created) => setShownUrl(joinUrl(created.token)),
        onError: (error) => setActionError({ title: `Could not regenerate the ${role} link`, error }),
        onSettled: () => setConfirming(null),
      });
    } else {
      actions.revoke.mutate(link, {
        onSuccess: () => setNotice(`The ${role} link was revoked. People who joined with it stay.`),
        onError: (error) => setActionError({ title: `Could not revoke the ${role} link`, error }),
        onSettled: () => setConfirming(null),
      });
    }
  };

  return (
    <Paper sx={{ p: 3 }}>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="h5">Join links for {organizationName}</Typography>
        <PermissionButton
          permission={PermissionKeys.JoinLinksManage}
          variant="contained"
          onClick={() => {
            actions.create.reset();
            setDialogKey((k) => k + 1);
            setCreating(true);
          }}
        >
          Create link
        </PermissionButton>
      </Stack>
      <Typography color="text.secondary" gutterBottom>
        Anyone with a link can join with its role. A link is shown once, when it is created or regenerated; only its
        hash is stored.
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

      {links.isError ? (
        <ErrorPanel error={links.error} title="Could not load the join links" />
      ) : (
        <ListTable
          columns={columns}
          rows={links.data?.items}
          rowKey={(link) => link.id}
          totalRecords={links.data?.totalRecords ?? 0}
          state={view.state}
          onChange={view.change}
          searchLabel="Search role"
          statusOptions={joinLinkStatusOptions}
          emptyText="No join links yet."
        />
      )}
      <CreateJoinLinkDialog
        key={dialogKey}
        open={creating}
        busy={actions.create.isPending}
        error={actions.create.error}
        onClose={() => setCreating(false)}
        onSubmit={(request) =>
          actions.create.mutate(request, {
            onSuccess: (created) => {
              setCreating(false);
              setActionError(null);
              setShownUrl(joinUrl(created.token));
            },
          })
        }
      />

      <ShowLinkDialog url={shownUrl} onClose={() => setShownUrl(null)} />

      <ConfirmDialog
        open={confirming !== null}
        title={confirming?.kind === 'regenerate' ? 'Regenerate join link' : 'Revoke join link'}
        confirmLabel={confirming?.kind === 'regenerate' ? 'Regenerate' : 'Revoke'}
        destructive={confirming?.kind === 'revoke'}
        busy={actions.regenerate.isPending || actions.revoke.isPending}
        onClose={() => setConfirming(null)}
        onConfirm={confirm}
      >
        {confirming?.kind === 'regenerate'
          ? 'The current link stops working and a new one is shown once.'
          : 'The link stops working. People who already joined with it stay members.'}
      </ConfirmDialog>
    </Paper>
  );
}
