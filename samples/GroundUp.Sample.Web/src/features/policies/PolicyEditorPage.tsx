import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TablePagination from '@mui/material/TablePagination';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useBlocker, useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { useMemo, useState, type ReactNode } from 'react';
import { ApiError } from '../../api/errors';
import type { CatalogState } from '../../api/policies';
import type { PermissionDto, PolicyDetailDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { RouterLink } from '../../components/RouterLink';
import { PermissionButton } from '../access/PermissionButton';
import { can, PermissionKeys } from '../access/permissions';
import { RequirePermission } from '../access/RequirePermission';
import { RequireTenant } from '../access/RequireTenant';
import { ConfirmDialog } from '../common/ConfirmDialog';
import type { ListSearch } from '../list/listSearch';
import { ListTable, type ListColumn } from '../list/ListTable';
import { usePageBack, type ListView } from '../list/useListView';
import { useSession } from '../session/sessionQuery';
import { permissionCatalogQueryOptions, permissionModulesQueryOptions, policyQueryOptions, usePolicyActions } from './policiesQuery';
import { PolicyInfoDialog } from './PolicyInfoDialog';
import { policiesListSearch, toCatalogSearch, toCatalogState } from './policySearch';
import {
  addKeys,
  changes,
  currentKeys,
  isDirty,
  removeKey,
  startWorkingSet,
  undoRemoval,
  workingRows,
  type WorkingSet,
} from './workingSet';

/** The right side's page size; it is the policy's own set, paged in the browser. */
const WORKING_PAGE_SIZE = 25;

/** One policy: its name, the roles using it, and its permissions chosen from the catalogue (GU-97d). */
export function PolicyEditorPage() {
  return (
    <RequireTenant purpose="manage its policies">
      <RequirePermission permission={PermissionKeys.PermissionsRead}>
        <PolicyEditor />
      </RequirePermission>
    </RequireTenant>
  );
}

function PolicyEditor() {
  const { me } = useSession();
  const tenantId = me!.tenantId!;
  const { policyId } = useParams({ from: '/policies/$policyId' });
  const listSearch = policiesListSearch(useSearch({ from: '/policies/$policyId' }));
  const policy = useQuery(policyQueryOptions(tenantId, policyId));

  const back = (
    <RouterLink to="/policies" search={listSearch}>
      ‹ Policies
    </RouterLink>
  );

  if (policy.isPending) {
    return <Paper sx={{ p: 3 }}>{back}</Paper>;
  }
  if (policy.isError) {
    const notFound = policy.error instanceof ApiError && policy.error.status === 404;
    return (
      <Paper sx={{ p: 3 }}>
        {back}
        <Stack sx={{ mt: 2 }}>
          {notFound ? (
            <Alert severity="warning">Policy not found. It may have been deleted, or it belongs to another organization.</Alert>
          ) : (
            <ErrorPanel error={policy.error} title="Could not load the policy" />
          )}
        </Stack>
      </Paper>
    );
  }
  // Keyed by the saved set, so a reload after saving starts a fresh working set
  const savedKey = policy.data.permissions.map((p) => p.key).sort().join(',');
  return <PolicyContent key={`${policy.data.id}:${savedKey}`} policy={policy.data} back={back} listSearch={listSearch} />;
}

interface PolicyContentProps {
  policy: PolicyDetailDto;
  back: ReactNode;
  listSearch: ListSearch;
}

function PolicyContent({ policy, back, listSearch }: PolicyContentProps) {
  const { me } = useSession();
  const canWrite = can(me, PermissionKeys.PermissionsWrite);
  const navigate = useNavigate({ from: '/policies/$policyId' });
  const search = useSearch({ from: '/policies/$policyId' });
  const actions = usePolicyActions();

  const [ws, setWs] = useState<WorkingSet>(() => startWorkingSet(policy.permissions.map((p) => p.key)));
  // Names for the right side: the saved permissions, plus every catalogue row seen
  const [known, setKnown] = useState<Map<string, PermissionDto>>(() => new Map(policy.permissions.map((p) => [p.key, p])));
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [actionError, setActionError] = useState<{ title: string; error: unknown } | null>(null);
  const dirty = isDirty(ws);
  const current = currentKeys(ws);

  // Leaving the page (not just changing the catalogue's view) with unsaved changes asks first
  useBlocker({
    shouldBlockFn: ({ current: from, next }) =>
      dirty && from.pathname !== next.pathname && !window.confirm('Discard the unsaved changes to this policy?'),
    enableBeforeUnload: () => dirty,
  });

  // ── Available (the catalogue, paged on the server) ──
  const catalogState = toCatalogState(search.available);
  const catalogView: ListView = {
    state: catalogState,
    change: (state, options) =>
      void navigate({
        search: (prev) => ({ ...prev, available: toCatalogSearch({ ...state, module: catalogState.module }) }),
        replace: options?.replace ?? false,
      }),
  };
  const setModule = (module: string | undefined) =>
    void navigate({ search: (prev) => ({ ...prev, available: toCatalogSearch({ ...catalogState, module, page: 1 }) }) });
  const catalog = useQuery(permissionCatalogQueryOptions(catalogState as CatalogState));
  usePageBack(catalog.data, catalog.isPlaceholderData, catalogView);
  const modules = useQuery(permissionModulesQueryOptions());

  const remember = (permissions: PermissionDto[]) =>
    setKnown((map) => {
      if (permissions.every((p) => map.has(p.key))) {
        return map;
      }
      const next = new Map(map);
      for (const p of permissions) {
        next.set(p.key, p);
      }
      return next;
    });

  const add = (permissions: PermissionDto[]) => {
    remember(permissions);
    setWs((set) => addKeys(set, permissions.map((p) => p.key)));
  };

  const availableColumns: ListColumn<PermissionDto>[] = [
    { label: 'Name', sortField: 'name', render: (p) => p.name },
    {
      label: 'Key',
      sortField: 'key',
      render: (p) => (
        <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>
          {p.key}
        </Typography>
      ),
    },
    { label: 'Module', sortField: 'module', render: (p) => p.module },
    {
      label: '',
      align: 'right',
      render: (p) =>
        current.has(p.key) ? (
          <Typography variant="caption" color="text.secondary">
            In policy
          </Typography>
        ) : (
          <PermissionButton permission={PermissionKeys.PermissionsWrite} size="small" onClick={() => add([p])}>
            Add ›
          </PermissionButton>
        ),
    },
  ];
  const addableShown = (catalog.data?.items ?? []).filter((p) => !current.has(p.key));

  // ── In this policy (the working set, paged in the browser) ──
  const [workingQ, setWorkingQ] = useState('');
  const [workingPage, setWorkingPage] = useState(0);
  const rows = useMemo(() => {
    const q = workingQ.trim().toLowerCase();
    return workingRows(ws)
      .map((row) => ({ ...row, permission: known.get(row.key) }))
      .filter(
        (row) =>
          !q ||
          row.key.toLowerCase().includes(q) ||
          (row.permission?.name.toLowerCase().includes(q) ?? false) ||
          (row.permission?.description?.toLowerCase().includes(q) ?? false),
      )
      .sort((a, b) => (a.permission?.name ?? a.key).localeCompare(b.permission?.name ?? b.key));
  }, [ws, known, workingQ]);
  const shownPage = Math.min(workingPage, Math.max(0, Math.ceil(rows.length / WORKING_PAGE_SIZE) - 1));
  const pageRows = rows.slice(shownPage * WORKING_PAGE_SIZE, (shownPage + 1) * WORKING_PAGE_SIZE);
  const pending = changes(ws);

  const save = () => {
    setActionError(null);
    actions.setPermissions.mutate(
      { policyId: policy.id, keys: [...current].sort() },
      { onError: (error) => setActionError({ title: 'Could not save the permissions', error }) },
    );
  };

  return (
    <Paper sx={{ p: 3 }}>
      {back}
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 1 }}>
        <Typography variant="h5">{policy.name}</Typography>
        <span style={{ flexGrow: 1 }} />
        <PermissionButton
          permission={PermissionKeys.PermissionsWrite}
          size="small"
          onClick={() => {
            actions.update.reset();
            setEditing(true);
          }}
        >
          Edit
        </PermissionButton>
        <PermissionButton
          permission={PermissionKeys.PermissionsWrite}
          size="small"
          color="error"
          onClick={() => {
            setActionError(null);
            setDeleting(true);
          }}
        >
          Delete
        </PermissionButton>
      </Stack>
      {policy.description && <Typography color="text.secondary">{policy.description}</Typography>}
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {policy.roles.length === 0 ? 'No role uses this policy.' : 'Used by: '}
        {policy.roles.map((role, index) => (
          <span key={role.id}>
            {index > 0 && ', '}
            <RouterLink to="/roles/$roleId" params={{ roleId: role.id }}>
              {role.name}
            </RouterLink>
          </span>
        ))}
      </Typography>

      <Alert
        severity={dirty ? 'warning' : 'info'}
        sx={{ my: 2 }}
        action={
          canWrite && (
            <Stack direction="row" spacing={1}>
              <Button size="small" color="inherit" disabled={!dirty || actions.setPermissions.isPending} onClick={() => setWs(startWorkingSet(ws.saved))}>
                Discard
              </Button>
              <Button size="small" variant="contained" disabled={!dirty} loading={actions.setPermissions.isPending} onClick={save}>
                Save
              </Button>
            </Stack>
          )
        }
      >
        {current.size} {current.size === 1 ? 'permission' : 'permissions'}
        {dirty && ` · unsaved changes: +${pending.added.length}, −${pending.removed.length}`}
      </Alert>
      {actionError && (
        <Stack sx={{ mb: 2 }}>
          <ErrorPanel error={actionError.error} title={actionError.title} />
        </Stack>
      )}

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, gap: 3 }}>
        <Box>
          <Typography variant="h6">Available</Typography>
          {catalog.isError ? (
            <ErrorPanel error={catalog.error} title="Could not load the permissions" />
          ) : (
            <ListTable
              columns={availableColumns}
              rows={catalog.data?.items}
              rowKey={(p) => p.key}
              totalRecords={catalog.data?.totalRecords ?? 0}
              state={catalogView.state}
              onChange={catalogView.change}
              searchLabel="Search name, key or description"
              filters={
                <TextField
                  select
                  size="small"
                  label="Module"
                  value={catalogState.module ?? ''}
                  onChange={(event) => setModule(event.target.value || undefined)}
                  sx={{ minWidth: 160 }}
                >
                  <MenuItem value="">All</MenuItem>
                  {(modules.data ?? []).map((module) => (
                    <MenuItem key={module} value={module}>
                      {module}
                    </MenuItem>
                  ))}
                </TextField>
              }
              emptyText="No permissions."
            />
          )}
          {canWrite && addableShown.length > 0 && (
            <Button size="small" sx={{ mt: 1 }} onClick={() => add(addableShown)}>
              Add all {addableShown.length} shown ›
            </Button>
          )}
        </Box>

        <Box>
          <Typography variant="h6">In this policy ({current.size})</Typography>
          <TextField
            size="small"
            label="Search the policy's permissions"
            value={workingQ}
            onChange={(event) => {
              setWorkingQ(event.target.value);
              setWorkingPage(0);
            }}
            sx={{ my: 2, minWidth: 260 }}
          />
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell />
                <TableCell>Name</TableCell>
                <TableCell>Key</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {pageRows.length === 0 && (
                <TableRow>
                  <TableCell colSpan={3}>{workingQ ? 'Nothing matches.' : 'No permissions yet: add some from the left.'}</TableCell>
                </TableRow>
              )}
              {pageRows.map((row) => {
                const removed = row.state === 'removed';
                return (
                  <TableRow key={row.key}>
                    <TableCell sx={{ whiteSpace: 'nowrap' }}>
                      {removed ? (
                        <PermissionButton permission={PermissionKeys.PermissionsWrite} size="small" onClick={() => setWs((set) => undoRemoval(set, row.key))}>
                          Undo
                        </PermissionButton>
                      ) : (
                        <PermissionButton
                          permission={PermissionKeys.PermissionsWrite}
                          size="small"
                          color="error"
                          onClick={() => setWs((set) => removeKey(set, row.key))}
                        >
                          ‹ Remove
                        </PermissionButton>
                      )}
                    </TableCell>
                    <TableCell sx={removed ? { textDecoration: 'line-through', color: 'text.disabled' } : undefined}>
                      {row.permission?.name ?? row.key}
                      {row.state === 'added' && <Chip size="small" color="success" variant="outlined" label="new" sx={{ ml: 1 }} />}
                      {removed && <Chip size="small" variant="outlined" label="removed" sx={{ ml: 1 }} />}
                    </TableCell>
                    <TableCell sx={{ fontFamily: 'monospace', ...(removed ? { textDecoration: 'line-through', color: 'text.disabled' } : {}) }}>
                      {row.key}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
          <TablePagination
            component="div"
            count={rows.length}
            page={shownPage}
            rowsPerPage={WORKING_PAGE_SIZE}
            rowsPerPageOptions={[WORKING_PAGE_SIZE]}
            onPageChange={(_, next) => setWorkingPage(next)}
          />
        </Box>
      </Box>

      <PolicyInfoDialog
        key={`info-${editing}`}
        title="Edit policy"
        submitLabel="Save"
        initial={{ name: policy.name, description: policy.description }}
        open={editing}
        busy={actions.update.isPending}
        error={actions.update.error}
        onClose={() => setEditing(false)}
        onSubmit={(request) =>
          actions.update.mutate({ policyId: policy.id, request }, { onSuccess: () => setEditing(false) })
        }
      />

      <ConfirmDialog
        open={deleting}
        title="Delete policy"
        confirmLabel="Delete"
        destructive
        busy={actions.remove.isPending}
        onClose={() => setDeleting(false)}
        onConfirm={() =>
          actions.remove.mutate(
            { policy, leave: () => navigate({ to: '/policies', search: listSearch }) },
            {
              onError: (error) => setActionError({ title: `Could not delete the policy ${policy.name}`, error }),
              onSettled: () => setDeleting(false),
            },
          )
        }
      >
        Delete the policy <strong>{policy.name}</strong>? Only a policy no role uses can be deleted; detach it from its
        roles first.
      </ConfirmDialog>
    </Paper>
  );
}

