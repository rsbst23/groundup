import Alert from '@mui/material/Alert';
import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { useState, type ReactNode } from 'react';
import { ApiError } from '../../api/errors';
import { roleTypeLabel } from '../../api/labels';
import type { PolicyDto, TenantRoleDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { RouterLink } from '../../components/RouterLink';
import { PermissionButton } from '../access/PermissionButton';
import { can, PermissionKeys } from '../access/permissions';
import { RequirePermission } from '../access/RequirePermission';
import { RequireTenant } from '../access/RequireTenant';
import { ConfirmDialog } from '../common/ConfirmDialog';
import type { ListSearch } from '../list/listSearch';
import { useSession } from '../session/sessionQuery';
import { AttachPolicyDialog } from './AttachPolicyDialog';
import { EditRoleDialog } from './EditRoleDialog';
import {
  allPoliciesQueryOptions,
  attachablePolicies,
  roleQueryOptions,
  rolePermissionsQueryOptions,
  rolePoliciesQueryOptions,
  useRoleActions,
} from './rolesQuery';

/** One role: rename, delete, its policies and the permissions they grant (GU-97). */
export function RoleDetailPage() {
  return (
    <RequireTenant purpose="manage its roles">
      <RequirePermission permission={PermissionKeys.RolesRead}>
        <RoleDetail />
      </RequirePermission>
    </RequireTenant>
  );
}

function RoleDetail() {
  const { me } = useSession();
  const tenantId = me!.tenantId!;
  const { roleId } = useParams({ from: '/roles/$roleId' });
  const listSearch = useSearch({ from: '/roles/$roleId' });
  const role = useQuery(roleQueryOptions(tenantId, roleId));

  const back = (
    <RouterLink to="/roles" search={listSearch}>
      ‹ Roles
    </RouterLink>
  );

  if (role.isPending) {
    return <Paper sx={{ p: 3 }}>{back}</Paper>;
  }
  if (role.isError) {
    const notFound = role.error instanceof ApiError && role.error.status === 404;
    return (
      <Paper sx={{ p: 3 }}>
        {back}
        <Stack sx={{ mt: 2 }}>
          {notFound ? (
            <Alert severity="warning">Role not found. It may have been deleted, or it belongs to another organization.</Alert>
          ) : (
            <ErrorPanel error={role.error} title="Could not load the role" />
          )}
        </Stack>
      </Paper>
    );
  }
  return <RoleContent role={role.data} tenantId={tenantId} back={back} listSearch={listSearch} />;
}

interface RoleContentProps {
  role: TenantRoleDto;
  tenantId: string;
  back: ReactNode;
  listSearch: ListSearch;
}

function RoleContent({ role, tenantId, back, listSearch }: RoleContentProps) {
  const { me } = useSession();
  const navigate = useNavigate();
  const actions = useRoleActions();
  const [editing, setEditing] = useState(false);
  const [dialogKey, setDialogKey] = useState(0);
  const [deleting, setDeleting] = useState(false);
  const [attaching, setAttaching] = useState(false);
  const [detaching, setDetaching] = useState<PolicyDto | null>(null);
  const [actionError, setActionError] = useState<{ title: string; error: unknown } | null>(null);

  const canReadPolicies = can(me, PermissionKeys.PermissionsRead);
  const policies = useQuery(rolePoliciesQueryOptions(tenantId, role.id, !role.isSystem));
  const allPolicies = useQuery(allPoliciesQueryOptions(tenantId, !role.isSystem && canReadPolicies && attaching));
  const permissions = useQuery(rolePermissionsQueryOptions(tenantId, role.id));

  return (
    <Paper sx={{ p: 3 }}>
      {back}
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 1 }}>
        <Typography variant="h5">{role.name}</Typography>
        {role.isSystem && <Chip size="small" label="built-in" />}
        {role.isDefault && <Chip size="small" color="info" variant="outlined" label="default" />}
        <span style={{ flexGrow: 1 }} />
        <PermissionButton
          permission={PermissionKeys.RolesWrite}
          size="small"
          onClick={() => {
            actions.update.reset();
            setDialogKey((k) => k + 1);
            setEditing(true);
          }}
        >
          {role.isSystem ? 'Edit description' : 'Edit'}
        </PermissionButton>
        {!role.isSystem && (
          <PermissionButton
            permission={PermissionKeys.RolesDelete}
            size="small"
            color="error"
            onClick={() => {
              setActionError(null);
              setDeleting(true);
            }}
          >
            Delete
          </PermissionButton>
        )}
      </Stack>
      <Typography color="text.secondary" gutterBottom>
        {[role.description, roleTypeLabel(role.roleType)].filter(Boolean).join(' · ')}
      </Typography>

      {actionError && (
        <Stack sx={{ my: 2 }}>
          <ErrorPanel error={actionError.error} title={actionError.title} />
        </Stack>
      )}

      {role.isSystem ? (
        <Alert severity="info" sx={{ my: 2 }}>
          {role.name} has all permissions in this organization. It needs no policies, and none can be attached.
        </Alert>
      ) : (
        <>
          <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mt: 3 }}>
            <Typography variant="h6">Policies</Typography>
            <PermissionButton
              // Attaching needs auth.roles.write; listing the policies to pick from needs auth.permissions.read
              permission={can(me, PermissionKeys.RolesWrite) ? PermissionKeys.PermissionsRead : PermissionKeys.RolesWrite}
              size="small"
              variant="contained"
              onClick={() => {
                actions.attach.reset();
                setDialogKey((k) => k + 1);
                setAttaching(true);
              }}
            >
              Attach policy
            </PermissionButton>
          </Stack>
          {policies.isError ? (
            <ErrorPanel error={policies.error} title="Could not load the role's policies" />
          ) : (
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Policy</TableCell>
                  <TableCell>Description</TableCell>
                  <TableCell align="right">Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {policies.isPending && (
                  <TableRow>
                    <TableCell colSpan={3}>Loading…</TableCell>
                  </TableRow>
                )}
                {policies.data?.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={3}>No policies: the role grants no permissions yet.</TableCell>
                  </TableRow>
                )}
                {policies.data?.map((policy) => (
                  <TableRow key={policy.id}>
                    <TableCell>{policy.name}</TableCell>
                    <TableCell>{policy.description ?? ''}</TableCell>
                    <TableCell align="right">
                      <PermissionButton
                        permission={PermissionKeys.RolesWrite}
                        size="small"
                        color="error"
                        disabled={actions.detach.isPending}
                        onClick={() => {
                          setActionError(null);
                          setDetaching(policy);
                        }}
                      >
                        Detach
                      </PermissionButton>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </>
      )}

      {!role.isSystem && (
        <>
          <Typography variant="h6" sx={{ mt: 3 }}>
            Effective permissions
          </Typography>
          {permissions.isError ? (
            <ErrorPanel error={permissions.error} title="Could not load the role's permissions" />
          ) : (
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Permission</TableCell>
                  <TableCell>Granted by</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {permissions.isPending && (
                  <TableRow>
                    <TableCell colSpan={2}>Loading…</TableCell>
                  </TableRow>
                )}
                {permissions.data?.permissions.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={2}>None.</TableCell>
                  </TableRow>
                )}
                {permissions.data?.permissions.map((permission) => (
                  <TableRow key={permission.key}>
                    <TableCell>
                      <Typography variant="body2">{permission.name}</Typography>
                      <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>
                        {permission.key}
                      </Typography>
                    </TableCell>
                    <TableCell>{permission.grantedBy.map((p) => p.name).join(', ')}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </>
      )}

      <EditRoleDialog
        key={`edit-${dialogKey}`}
        role={role}
        open={editing}
        busy={actions.update.isPending}
        error={actions.update.error}
        onClose={() => setEditing(false)}
        onSubmit={(request) => actions.update.mutate({ roleId: role.id, request }, { onSuccess: () => setEditing(false) })}
      />

      <AttachPolicyDialog
        key={`attach-${dialogKey}`}
        roleName={role.name}
        choices={allPolicies.data && policies.data ? attachablePolicies(allPolicies.data, policies.data) : undefined}
        open={attaching}
        busy={actions.attach.isPending}
        error={actions.attach.error ?? (allPolicies.isError ? allPolicies.error : null)}
        onClose={() => setAttaching(false)}
        onSubmit={(policyId) => actions.attach.mutate({ roleId: role.id, policyId }, { onSuccess: () => setAttaching(false) })}
      />

      <ConfirmDialog
        open={detaching !== null}
        title="Detach policy"
        confirmLabel="Detach"
        destructive
        busy={actions.detach.isPending}
        onClose={() => setDetaching(null)}
        onConfirm={() =>
          detaching &&
          actions.detach.mutate(
            { roleId: role.id, policyId: detaching.id },
            {
              onError: (error) => setActionError({ title: `Could not detach ${detaching.name}`, error }),
              onSettled: () => setDetaching(null),
            },
          )
        }
      >
        Detach <strong>{detaching?.name}</strong> from {role.name}? People with the role lose the permissions only this
        policy grants, at once.
      </ConfirmDialog>

      <ConfirmDialog
        open={deleting}
        title="Delete role"
        confirmLabel="Delete"
        destructive
        busy={actions.remove.isPending}
        onClose={() => setDeleting(false)}
        onConfirm={() =>
          actions.remove.mutate(
            { roleId: role.id, leave: () => navigate({ to: '/roles', search: listSearch }) },
            {
              onError: (error) => setActionError({ title: `Could not delete the role ${role.name}`, error }),
              onSettled: () => setDeleting(false),
            },
          )
        }
      >
        Delete the role <strong>{role.name}</strong>? Only a role nobody holds, that no invitation or join link grants and
        that is not the default can be deleted.
      </ConfirmDialog>
    </Paper>
  );
}
