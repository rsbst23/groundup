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
import { joinLinkStatusLabel } from '../../api/labels';
import { PAGE_SIZE } from '../../api/paging';
import type { JoinLinkDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { PermissionButton } from '../access/PermissionButton';
import { PermissionKeys } from '../access/permissions';
import { RequireTenant } from '../access/RequireTenant';
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
      <JoinLinksTable />
    </RequireTenant>
  );
}

type Confirming = { kind: 'regenerate' | 'revoke'; link: JoinLinkDto } | null;

function JoinLinksTable() {
  const { me, currentTenant } = useSession();
  const tenantId = me!.tenantId!;
  const organizationName = currentTenant?.name ?? 'this organization';
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [dialogKey, setDialogKey] = useState(0);
  const [shownUrl, setShownUrl] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<Confirming>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionError, setActionError] = useState<{ title: string; error: unknown } | null>(null);

  const links = useQuery(joinLinksQueryOptions(tenantId, page));
  const actions = useJoinLinkActions();
  const busy = actions.regenerate.isPending || actions.revoke.isPending;

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
        <>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Role</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Uses</TableCell>
                <TableCell>Expires</TableCell>
                <TableCell>Created</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {links.isPending && (
                <TableRow>
                  <TableCell colSpan={6}>Loading…</TableCell>
                </TableRow>
              )}
              {links.data?.items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6}>No join links yet.</TableCell>
                </TableRow>
              )}
              {links.data?.items.map((link) => (
                <TableRow key={link.id}>
                  <TableCell>{link.roleName ?? '—'}</TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      variant="outlined"
                      label={joinLinkStatusLabel(link.status)}
                      color={isActive(link) ? 'success' : 'default'}
                    />
                  </TableCell>
                  <TableCell>{usesText(link.useCount, link.maxUses)}</TableCell>
                  <TableCell>{link.expiresAt ? formatDateTime(link.expiresAt) : 'Never'}</TableCell>
                  <TableCell>{formatDateTime(link.createdAt)}</TableCell>
                  <TableCell align="right">
                    {isActive(link) && (
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
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {links.data && (
            <TablePagination
              component="div"
              count={links.data.totalRecords}
              page={page - 1}
              rowsPerPage={PAGE_SIZE}
              rowsPerPageOptions={[PAGE_SIZE]}
              onPageChange={(_, next) => setPage(next + 1)}
            />
          )}
        </>
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
