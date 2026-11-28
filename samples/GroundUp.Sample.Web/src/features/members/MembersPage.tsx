import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { useState } from 'react';
import { MembershipStatus, membershipStatusLabel } from '../../api/labels';
import type { TenantMemberDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { PermissionButton } from '../access/PermissionButton';
import { can, missingPermissionText, PermissionKeys } from '../access/permissions';
import { RequirePermission } from '../access/RequirePermission';
import { RequireTenant } from '../access/RequireTenant';
import { ListTable, type ListColumn, type StatusOption } from '../list/ListTable';
import { useListView, usePageBack } from '../list/useListView';
import { useSession } from '../session/sessionQuery';
import { AddRoleMenu } from './AddRoleMenu';
import { memberName, membersQueryOptions, roleChipLabel, rolesQueryOptions, useMemberActions } from './membersQuery';
import { RemoveMemberDialog } from './RemoveMemberDialog';

/** The current organization's members: their roles, adding and removing roles, removing members. */
export function MembersPage() {
  return (
    <RequireTenant purpose="manage its members">
      <RequirePermission permission={PermissionKeys.MembersRead}>
        <MembersTable />
      </RequirePermission>
    </RequireTenant>
  );
}

interface ActionError {
  title: string;
  error: unknown;
}

/** The list only has Active and Pending members. */
const memberStatusOptions: StatusOption[] = [MembershipStatus.Active, MembershipStatus.Pending].map((value) => ({
  value,
  label: membershipStatusLabel(value),
}));

function MembersTable() {
  const { me, currentTenant } = useSession();
  const tenantId = me!.tenantId!;
  const organizationName = currentTenant?.name ?? 'this organization';
  const navigate = useNavigate({ from: '/members' });
  const view = useListView(useSearch({ from: '/members' }), (search, replace) => void navigate({ search, replace }));
  const [removing, setRemoving] = useState<TenantMemberDto | null>(null);
  const [actionError, setActionError] = useState<ActionError | null>(null);

  const members = useQuery(membersQueryOptions(tenantId, view.state));
  usePageBack(members.data, members.isPlaceholderData, view);
  const canAssign = can(me, PermissionKeys.RolesAssign);
  const roles = useQuery(rolesQueryOptions(tenantId, canAssign && can(me, PermissionKeys.RolesRead)));
  const rolesUnavailable = !can(me, PermissionKeys.RolesRead)
    ? missingPermissionText(PermissionKeys.RolesRead)
    : roles.isError
      ? 'The roles could not be loaded.'
      : null;

  const actions = useMemberActions(me!.userId);
  const busy = actions.removeMember.isPending || actions.addRole.isPending || actions.removeRole.isPending;
  const failed = (title: string) => (error: unknown) => setActionError({ title, error });

  const columns: ListColumn<TenantMemberDto>[] = [
    {
      label: 'Name',
      sortField: 'displayName',
      render: (member) => (
        <Typography variant="body2">
          {memberName(member)}
          {member.userId === me!.userId && ' (you)'}
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
          label={membershipStatusLabel(member.status)}
          color={member.status === MembershipStatus.Pending ? 'warning' : 'success'}
          variant="outlined"
        />
      ),
    },
    {
      label: 'Roles',
      render: (member) => (
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          {member.roles.length === 0 && (
            <Typography variant="body2" color="text.secondary">
              No roles
            </Typography>
          )}
          {member.roles.map((role) => (
            <Tooltip key={role.roleId} title={canAssign ? '' : missingPermissionText(PermissionKeys.RolesAssign)}>
              <Chip
                size="small"
                label={roleChipLabel(role)}
                disabled={busy}
                onDelete={
                  canAssign
                    ? () => {
                        setActionError(null);
                        actions.removeRole.mutate(
                          { member, role },
                          { onError: failed(`Could not remove the role ${role.roleName} from ${memberName(member)}`) },
                        );
                      }
                    : undefined
                }
              />
            </Tooltip>
          ))}
        </Stack>
      ),
    },
    {
      label: 'Actions',
      align: 'right',
      render: (member) => (
        <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
          <AddRoleMenu
            member={member}
            roles={roles.data}
            rolesUnavailable={rolesUnavailable}
            busy={busy}
            onAdd={(role) => {
              setActionError(null);
              actions.addRole.mutate(
                { member, role },
                { onError: failed(`Could not give ${memberName(member)} the role ${role.name}`) },
              );
            }}
          />
          <PermissionButton
            permission={PermissionKeys.MembersManage}
            size="small"
            color="error"
            disabled={busy}
            onClick={() => setRemoving(member)}
          >
            Remove
          </PermissionButton>
        </Stack>
      ),
    },
  ];
  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="h5" gutterBottom>
        Members of {organizationName}
      </Typography>
      <Typography color="text.secondary" gutterBottom>
        Active and pending members (invited, not joined yet). Role chips show why a role is held when it is more than a
        manual grant; removing a role removes it whatever the reasons.
      </Typography>

      {actionError && (
        <Stack sx={{ my: 2 }}>
          <ErrorPanel error={actionError.error} title={actionError.title} />
        </Stack>
      )}

      {members.isError ? (
        <ErrorPanel error={members.error} title="Could not load the members" />
      ) : (
        <ListTable
          columns={columns}
          rows={members.data?.items}
          rowKey={(member) => member.userId}
          totalRecords={members.data?.totalRecords ?? 0}
          state={view.state}
          onChange={view.change}
          searchLabel="Search name or email"
          statusOptions={memberStatusOptions}
          emptyText="No members."
        />
      )}
      <RemoveMemberDialog
        member={removing}
        organizationName={organizationName}
        busy={actions.removeMember.isPending}
        onClose={() => setRemoving(null)}
        onConfirm={(member) => {
          setActionError(null);
          actions.removeMember.mutate(member, {
            onError: failed(`Could not remove ${memberName(member)}`),
            onSettled: () => setRemoving(null),
          });
        }}
      />
    </Paper>
  );
}
