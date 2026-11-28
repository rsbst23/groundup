import Alert from '@mui/material/Alert';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { useState } from 'react';
import { MembershipStatus, membershipStatusLabel } from '../../api/labels';
import type { TenantMemberDto, TenantRoleDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { PermissionButton } from '../access/PermissionButton';
import { can, PermissionKeys, permissionRequiredText } from '../access/permissions';
import { ConfirmDialog } from '../common/ConfirmDialog';
import { toListSearch, toListState } from '../list/listSearch';
import { ListTable, type ListColumn, type StatusOption } from '../list/ListTable';
import { usePageBack, type ListView } from '../list/useListView';
import { memberName, roleChipLabel } from '../members/membersQuery';
import { useSession } from '../session/sessionQuery';
import { AddMemberDialog } from './AddMemberDialog';
import { roleMembersQueryOptions, useRoleMemberActions } from './rolesQuery';

const statusOptions: StatusOption[] = [MembershipStatus.Active, MembershipStatus.Pending].map((value) => ({
  value,
  label: membershipStatusLabel(value),
}));

/**
 * The people holding the role (GU-97d): a table with its own view in the URL (`members`), Remove from
 * role, and Add member. Listing needs auth.members.read; adding and removing need auth.roles.assign
 * (the API also applies the role rules, such as who may grant TenantAdmin and the last admin).
 */
export function RoleMembersSection({ role, tenantId }: { role: TenantRoleDto; tenantId: string }) {
  const { me } = useSession();
  const canRead = can(me, PermissionKeys.MembersRead);
  const navigate = useNavigate({ from: '/roles/$roleId' });
  const search = useSearch({ from: '/roles/$roleId' });
  const view: ListView = {
    state: toListState(search.members ?? {}),
    change: (state, options) =>
      void navigate({ search: (prev) => ({ ...prev, members: toListSearch(state) }), replace: options?.replace ?? false }),
  };

  const members = useQuery(roleMembersQueryOptions(tenantId, role.id, view.state, canRead));
  usePageBack(members.data, members.isPlaceholderData, view);
  const actions = useRoleMemberActions(role.id);
  const [adding, setAdding] = useState(false);
  const [dialogKey, setDialogKey] = useState(0);
  const [removing, setRemoving] = useState<TenantMemberDto | null>(null);
  const [actionError, setActionError] = useState<{ title: string; error: unknown } | null>(null);

  const columns: ListColumn<TenantMemberDto>[] = [
    {
      label: 'Name',
      sortField: 'displayName',
      render: (member) => (
        <Typography variant="body2">
          {memberName(member)}
          {member.userId === me?.userId && ' (you)'}
        </Typography>
      ),
    },
    { label: 'Email', sortField: 'email', render: (member) => member.email },
    {
      label: 'Status',
      sortField: 'status',
      render: (member) => (
        <Chip
          size="small"
          variant="outlined"
          label={membershipStatusLabel(member.status)}
          color={member.status === MembershipStatus.Pending ? 'warning' : 'success'}
        />
      ),
    },
    {
      label: 'Other roles',
      render: (member) => (
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          {member.roles
            .filter((r) => r.roleId !== role.id)
            .map((r) => (
              <Chip key={r.roleId} size="small" label={roleChipLabel(r)} />
            ))}
        </Stack>
      ),
    },
    {
      label: 'Actions',
      align: 'right',
      render: (member) => (
        <PermissionButton
          permission={PermissionKeys.RolesAssign}
          size="small"
          color="error"
          disabled={actions.remove.isPending}
          onClick={() => {
            setActionError(null);
            setRemoving(member);
          }}
        >
          Remove from role
        </PermissionButton>
      ),
    },
  ];

  return (
    <>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mt: 3 }}>
        <Typography variant="h6">Members</Typography>
        {canRead && (
          <PermissionButton
            permission={PermissionKeys.RolesAssign}
            size="small"
            variant="contained"
            onClick={() => {
              actions.add.reset();
              setDialogKey((k) => k + 1);
              setAdding(true);
            }}
          >
            Add member
          </PermissionButton>
        )}
      </Stack>

      {!canRead ? (
        <Alert severity="info" sx={{ mt: 1 }}>
          {permissionRequiredText([PermissionKeys.MembersRead], 'see the members')}
        </Alert>
      ) : (
        <>
          {actionError && (
            <Stack sx={{ my: 2 }}>
              <ErrorPanel error={actionError.error} title={actionError.title} />
            </Stack>
          )}
          {members.isError ? (
            <ErrorPanel error={members.error} title="Could not load the role's members" />
          ) : (
            <ListTable
              columns={columns}
              rows={members.data?.items}
              rowKey={(member) => member.userId}
              totalRecords={members.data?.totalRecords ?? 0}
              state={view.state}
              onChange={view.change}
              searchLabel="Search name or email"
              statusOptions={statusOptions}
              emptyText="Nobody holds this role."
            />
          )}
        </>
      )}

      <AddMemberDialog
        key={dialogKey}
        tenantId={tenantId}
        role={role}
        open={adding}
        busy={actions.add.isPending}
        error={actions.add.error}
        onClose={() => setAdding(false)}
        onSubmit={(member) => actions.add.mutate(member, { onSuccess: () => setAdding(false) })}
      />

      <ConfirmDialog
        open={removing !== null}
        title="Remove from role"
        confirmLabel="Remove"
        destructive
        busy={actions.remove.isPending}
        onClose={() => setRemoving(null)}
        onConfirm={() =>
          removing &&
          actions.remove.mutate(removing, {
            onError: (error) => setActionError({ title: `Could not remove ${memberName(removing)} from ${role.name}`, error }),
            onSettled: () => setRemoving(null),
          })
        }
      >
        Remove <strong>{removing ? memberName(removing) : ''}</strong> from {role.name}? This removes the role whatever
        the reasons they hold it (invitation, join link, default role). They stay a member of the organization.
      </ConfirmDialog>
    </>
  );
}
