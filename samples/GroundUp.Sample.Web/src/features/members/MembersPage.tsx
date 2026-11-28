import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TablePagination from '@mui/material/TablePagination';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { MembershipStatus, membershipStatusLabel } from '../../api/labels';
import { PAGE_SIZE } from '../../api/paging';
import type { TenantMemberDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { PermissionButton } from '../access/PermissionButton';
import { can, missingPermissionText, PermissionKeys } from '../access/permissions';
import { RequireTenant } from '../access/RequireTenant';
import { useSession } from '../session/sessionQuery';
import { AddRoleMenu } from './AddRoleMenu';
import { memberName, membersQueryOptions, roleChipLabel, rolesQueryOptions, useMemberActions } from './membersQuery';
import { RemoveMemberDialog } from './RemoveMemberDialog';

/** The current organization's members: their roles, adding and removing roles, removing members. */
export function MembersPage() {
  return (
    <RequireTenant purpose="manage its members">
      <MembersTable />
    </RequireTenant>
  );
}

interface ActionError {
  title: string;
  error: unknown;
}

function MembersTable() {
  const { me, currentTenant } = useSession();
  const tenantId = me!.tenantId!;
  const organizationName = currentTenant?.name ?? 'this organization';
  const [page, setPage] = useState(1);
  const [removing, setRemoving] = useState<TenantMemberDto | null>(null);
  const [actionError, setActionError] = useState<ActionError | null>(null);

  const members = useQuery(membersQueryOptions(tenantId, page));
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
        <>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Member</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Roles</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {members.isPending && (
                <TableRow>
                  <TableCell colSpan={4}>Loading…</TableCell>
                </TableRow>
              )}
              {members.data?.items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={4}>No members.</TableCell>
                </TableRow>
              )}
              {members.data?.items.map((member) => (
                <TableRow key={member.userId}>
                  <TableCell>
                    <Typography variant="body2">{memberName(member)}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {member.email}
                      {member.userId === me!.userId && ' (you)'}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      label={membershipStatusLabel(member.status)}
                      color={member.status === MembershipStatus.Pending ? 'warning' : 'success'}
                      variant="outlined"
                    />
                  </TableCell>
                  <TableCell>
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
                  </TableCell>
                  <TableCell align="right">
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
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {members.data && (
            <TablePagination
              component="div"
              count={members.data.totalRecords}
              page={page - 1}
              rowsPerPage={PAGE_SIZE}
              rowsPerPageOptions={[PAGE_SIZE]}
              onPageChange={(_, next) => setPage(next + 1)}
            />
          )}
        </>
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
