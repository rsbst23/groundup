import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { useState } from 'react';
import type { TenantPolicyDto } from '../../api/types';
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
import { policyPageQueryOptions, usePolicyActions } from './policiesQuery';
import { PolicyInfoDialog } from './PolicyInfoDialog';

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
  const navigate = useNavigate({ from: '/policies/' });
  const view = useListView(useSearch({ from: '/policies/' }), (search, replace) => void navigate({ search, replace }));
  const [creating, setCreating] = useState(false);
  const [dialogKey, setDialogKey] = useState(0);
  const [deleting, setDeleting] = useState<TenantPolicyDto | null>(null);
  const [actionError, setActionError] = useState<{ title: string; error: unknown } | null>(null);

  const policies = useQuery(policyPageQueryOptions(tenantId, view.state));
  usePageBack(policies.data, policies.isPlaceholderData, view);
  const actions = usePolicyActions();
  const listSearch = toListSearch(view.state);

  const columns: ListColumn<TenantPolicyDto>[] = [
    {
      label: 'Name',
      sortField: 'name',
      render: (policy) => (
        <RouterLink to="/policies/$policyId" params={{ policyId: policy.id }} search={listSearch}>
          {policy.name}
        </RouterLink>
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
            setDialogKey((k) => k + 1);
            setCreating(true);
          }}
        >
          New policy
        </PermissionButton>
      </Stack>
      <Typography color="text.secondary" gutterBottom>
        A policy is a named set of permissions. Open one to choose its permissions; attach it to roles on their pages.
      </Typography>

      {actionError && (
        <Stack sx={{ my: 2 }}>
          <ErrorPanel error={actionError.error} title={actionError.title} />
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

      <PolicyInfoDialog
        key={dialogKey}
        title="New policy"
        submitLabel="Create and choose permissions"
        open={creating}
        busy={actions.create.isPending}
        error={actions.create.error}
        onClose={() => setCreating(false)}
        onSubmit={(request) =>
          actions.create.mutate(request, {
            onSuccess: (policy) => {
              setCreating(false);
              void navigate({ to: '/policies/$policyId', params: { policyId: policy.id }, search: listSearch });
            },
          })
        }
      />

      <ConfirmDialog
        open={deleting !== null}
        title="Delete policy"
        confirmLabel="Delete"
        destructive
        busy={actions.remove.isPending}
        onClose={() => setDeleting(null)}
        onConfirm={() =>
          deleting &&
          actions.remove.mutate(
            { policy: deleting },
            {
              onError: (error) => setActionError({ title: `Could not delete the policy ${deleting.name}`, error }),
              onSettled: () => setDeleting(null),
            },
          )
        }
      >
        Delete the policy <strong>{deleting?.name}</strong>? Only a policy no role uses can be deleted; detach it from its
        roles first.
      </ConfirmDialog>
    </Paper>
  );
}
