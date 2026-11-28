import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { useState } from 'react';
import type { PolicyDetailDto, TenantPolicyDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { PermissionButton } from '../access/PermissionButton';
import { can, PermissionKeys } from '../access/permissions';
import { RequirePermission } from '../access/RequirePermission';
import { RequireTenant } from '../access/RequireTenant';
import { ConfirmDialog } from '../common/ConfirmDialog';
import { ListTable, type ListColumn } from '../list/ListTable';
import { useListView, usePageBack } from '../list/useListView';
import { useSession } from '../session/sessionQuery';
import { sortedKeys } from './permissionChoices';
import { policyPageQueryOptions, policyQueryOptions, usePolicyActions } from './policiesQuery';
import { PolicyDialog } from './PolicyDialog';

/** The current organization's policies: named sets of permissions that roles grant (GU-96, GU-97). */
export function PoliciesPage() {
  return (
    <RequireTenant purpose="manage its policies">
      <RequirePermission permission={PermissionKeys.PermissionsRead}>
        <PoliciesTable />
      </RequirePermission>
    </RequireTenant>
  );
}

function PoliciesTable() {
  const { me, currentTenant } = useSession();
  const tenantId = me!.tenantId!;
  const organizationName = currentTenant?.name ?? 'this organization';
  const canWrite = can(me, PermissionKeys.PermissionsWrite);
  const navigate = useNavigate({ from: '/policies' });
  const view = useListView(useSearch({ from: '/policies' }), (search, replace) => void navigate({ search, replace }));
  const [creating, setCreating] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<TenantPolicyDto | null>(null);
  const [actionError, setActionError] = useState<{ title: string; error: unknown } | null>(null);

  const policies = useQuery(policyPageQueryOptions(tenantId, view.state));
  usePageBack(policies.data, policies.isPlaceholderData, view);
  const editing = useQuery(policyQueryOptions(tenantId, editingId));
  const actions = usePolicyActions();
  // The policy being edited, once loaded
  const loaded: PolicyDetailDto | undefined = editingId !== null && editing.data?.id === editingId ? editing.data : undefined;

  const open = (policy: TenantPolicyDto) => {
    actions.save.reset();
    setActionError(null);
    setEditingId(policy.id);
  };

  const columns: ListColumn<TenantPolicyDto>[] = [
    {
      label: 'Name',
      sortField: 'name',
      render: (policy) => (
        <Link component="button" variant="body2" onClick={() => open(policy)}>
          {policy.name}
        </Link>
      ),
    },
    {
      label: 'Description',
      render: (policy) => (
        <Typography variant="body2" color="text.secondary">
          {policy.description ?? ''}
        </Typography>
      ),
    },
    { label: 'Permissions', sortField: 'permissionCount', render: (policy) => policy.permissionKeys.length },
    { label: 'Roles', sortField: 'roleCount', render: (policy) => policy.roleCount },
    {
      label: 'Actions',
      align: 'right',
      render: (policy) => (
        <PermissionButton
          permission={PermissionKeys.PermissionsWrite}
          size="small"
          color="error"
          disabled={actions.remove.isPending}
          onClick={() => {
            setActionError(null);
            setDeleting(policy);
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
        <Typography variant="h5">Policies of {organizationName}</Typography>
        <PermissionButton
          permission={PermissionKeys.PermissionsWrite}
          variant="contained"
          onClick={() => {
            actions.create.reset();
            setCreating(true);
          }}
        >
          New policy
        </PermissionButton>
      </Stack>
      <Typography color="text.secondary" gutterBottom>
        A policy is a named set of permissions. Attach it to roles on their pages; everyone with those roles gets its
        permissions at once.
      </Typography>

      {actionError && (
        <Stack sx={{ my: 2 }}>
          <ErrorPanel error={actionError.error} title={actionError.title} />
        </Stack>
      )}
      {editing.isError && (
        <Stack sx={{ my: 2 }}>
          <ErrorPanel error={editing.error} title="Could not load the policy" />
        </Stack>
      )}

      {policies.isError ? (
        <ErrorPanel error={policies.error} title="Could not load the policies" />
      ) : (
        <ListTable
          columns={columns}
          rows={policies.data?.items}
          rowKey={(policy) => policy.id}
          totalRecords={policies.data?.totalRecords ?? 0}
          state={view.state}
          onChange={view.change}
          searchLabel="Search name or description"
          emptyText="No policies yet."
        />
      )}

      {creating && (
        <PolicyDialog
          policy={null}
          open
          canWrite={canWrite}
          busy={actions.create.isPending}
          error={actions.create.error}
          onClose={() => setCreating(false)}
          onSubmit={(values) =>
            actions.create.mutate(
              { name: values.name, description: values.description, permissionKeys: sortedKeys(values.keys) },
              { onSuccess: () => setCreating(false) },
            )
          }
        />
      )}

      {loaded && (
        <PolicyDialog
          key={loaded.id}
          policy={loaded}
          open
          canWrite={canWrite}
          busy={actions.save.isPending}
          error={actions.save.error}
          onClose={() => setEditingId(null)}
          onSubmit={(values) => actions.save.mutate({ before: loaded, ...values }, { onSuccess: () => setEditingId(null) })}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        title="Delete policy"
        confirmLabel="Delete"
        destructive
        busy={actions.remove.isPending}
        onClose={() => setDeleting(null)}
        onConfirm={() =>
          deleting &&
          actions.remove.mutate(deleting, {
            onError: (error) => setActionError({ title: `Could not delete the policy ${deleting.name}`, error }),
            onSettled: () => setDeleting(null),
          })
        }
      >
        Delete the policy <strong>{deleting?.name}</strong>? Only a policy no role uses can be deleted; detach it from its
        roles first.
      </ConfirmDialog>
    </Paper>
  );
}
