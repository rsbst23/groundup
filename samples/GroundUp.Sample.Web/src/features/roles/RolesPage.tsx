import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { useState } from 'react';
import { roleTypeLabel } from '../../api/labels';
import type { TenantRoleDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { RouterLink } from '../../components/RouterLink';
import { PermissionButton } from '../access/PermissionButton';
import { PermissionKeys } from '../access/permissions';
import { RequirePermission } from '../access/RequirePermission';
import { RequireTenant } from '../access/RequireTenant';
import { ConfirmDialog } from '../common/ConfirmDialog';
import { toListSearch } from '../list/listSearch';
import { ListTable, type ListColumn } from '../list/ListTable';
import { useListView, usePageBack } from '../list/useListView';
import { useSession } from '../session/sessionQuery';
import { CreateRoleDialog } from './CreateRoleDialog';
import { DefaultRoleBox } from './DefaultRoleBox';
import { policiesText } from './roleText';
import { rolePageQueryOptions, useRoleActions } from './rolesQuery';

/** The current organization's roles: create, delete, the default role; a role opens its page. */
export function RolesPage() {
  return (
    <RequireTenant purpose="manage its roles">
      <RequirePermission permission={PermissionKeys.RolesRead}>
        <RolesTable />
      </RequirePermission>
    </RequireTenant>
  );
}

function RolesTable() {
  const { me, currentTenant } = useSession();
  const tenantId = me!.tenantId!;
  const organizationName = currentTenant?.name ?? 'this organization';
  const navigate = useNavigate({ from: '/roles/' });
  const view = useListView(useSearch({ from: '/roles/' }), (search, replace) => void navigate({ search, replace }));
  const [creating, setCreating] = useState(false);
  const [dialogKey, setDialogKey] = useState(0);
  const [deleting, setDeleting] = useState<TenantRoleDto | null>(null);
  const [actionError, setActionError] = useState<{ title: string; error: unknown } | null>(null);

  const roles = useQuery(rolePageQueryOptions(tenantId, view.state));
  usePageBack(roles.data, roles.isPlaceholderData, view);
  const actions = useRoleActions();
  const listSearch = toListSearch(view.state);

  const columns: ListColumn<TenantRoleDto>[] = [
    {
      label: 'Name',
      sortField: 'name',
      render: (role) => (
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <RouterLink to="/roles/$roleId" params={{ roleId: role.id }} search={listSearch}>
            {role.name}
          </RouterLink>
          {role.isSystem && <Chip size="small" label="built-in" />}
          {role.isDefault && <Chip size="small" color="info" variant="outlined" label="default" />}
        </Stack>
      ),
    },
    { label: 'Type', sortField: 'roleType', render: (role) => roleTypeLabel(role.roleType) },
    { label: 'Policies', sortField: 'policyCount', render: policiesText },
    { label: 'Active', sortField: 'activeMemberCount', render: (role) => role.activeMemberCount },
    { label: 'Pending', sortField: 'pendingMemberCount', render: (role) => role.pendingMemberCount },
    {
      label: 'Description',
      render: (role) => (
        <Typography variant="body2" color="text.secondary">
          {role.description ?? ''}
        </Typography>
      ),
    },
    {
      label: 'Actions',
      align: 'right',
      render: (role) =>
        !role.isSystem && (
          <PermissionButton
            permission={PermissionKeys.RolesDelete}
            size="small"
            color="error"
            disabled={actions.remove.isPending}
            onClick={() => {
              setActionError(null);
              setDeleting(role);
            }}
          >
            Delete
          </PermissionButton>
        ),
    },
  ];

  return (
    <Paper sx={{ p: 3 }}>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="h5">Roles of {organizationName}</Typography>
        <PermissionButton
          permission={PermissionKeys.RolesWrite}
          variant="contained"
          onClick={() => {
            actions.create.reset();
            setDialogKey((k) => k + 1);
            setCreating(true);
          }}
        >
          New role
        </PermissionButton>
      </Stack>
      <Typography color="text.secondary" gutterBottom>
        A role grants the permissions of its policies. TenantAdmin is built in and has every permission.
      </Typography>

      <DefaultRoleBox tenantId={tenantId} />

      {actionError && (
        <Stack sx={{ my: 2 }}>
          <ErrorPanel error={actionError.error} title={actionError.title} />
        </Stack>
      )}

      {roles.isError ? (
        <ErrorPanel error={roles.error} title="Could not load the roles" />
      ) : (
        <ListTable
          columns={columns}
          rows={roles.data?.items}
          rowKey={(role) => role.id}
          totalRecords={roles.data?.totalRecords ?? 0}
          state={view.state}
          onChange={view.change}
          searchLabel="Search name or description"
          emptyText="No roles."
        />
      )}

      <CreateRoleDialog
        key={dialogKey}
        open={creating}
        busy={actions.create.isPending}
        error={actions.create.error}
        onClose={() => setCreating(false)}
        onSubmit={(request) =>
          actions.create.mutate(request, {
            onSuccess: (role) => {
              setCreating(false);
              void navigate({ to: '/roles/$roleId', params: { roleId: role.id }, search: listSearch });
            },
          })
        }
      />

      <ConfirmDialog
        open={deleting !== null}
        title="Delete role"
        confirmLabel="Delete"
        destructive
        busy={actions.remove.isPending}
        onClose={() => setDeleting(null)}
        onConfirm={() =>
          deleting &&
          actions.remove.mutate({ roleId: deleting.id }, {
            onError: (error) => setActionError({ title: `Could not delete the role ${deleting.name}`, error }),
            onSettled: () => setDeleting(null),
          })
        }
      >
        Delete the role <strong>{deleting?.name}</strong>? Only a role nobody holds, that no invitation or join link
        grants and that is not the default can be deleted.
      </ConfirmDialog>
    </Paper>
  );
}
