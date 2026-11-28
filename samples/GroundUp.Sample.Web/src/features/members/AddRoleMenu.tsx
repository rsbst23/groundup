import AddIcon from '@mui/icons-material/Add';
import ListItemText from '@mui/material/ListItemText';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import { useState } from 'react';
import type { TenantMemberDto, TenantRoleDto } from '../../api/types';
import { PermissionButton } from '../access/PermissionButton';
import { PermissionKeys } from '../access/permissions';
import { rolesToAdd } from './membersQuery';

interface AddRoleMenuProps {
  member: TenantMemberDto;
  /** The tenant's roles; undefined while loading or when they cannot be read. */
  roles: TenantRoleDto[] | undefined;
  /** Why the roles are missing, when they could not be loaded. */
  rolesUnavailable: string | null;
  busy: boolean;
  onAdd: (role: TenantRoleDto) => void;
}

/** "Add role": a menu of the tenant's roles the member does not hold yet. */
export function AddRoleMenu({ member, roles, rolesUnavailable, busy, onAdd }: AddRoleMenuProps) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const choices = roles ? rolesToAdd(roles, member) : [];

  return (
    <>
      <PermissionButton
        permission={PermissionKeys.RolesAssign}
        size="small"
        startIcon={<AddIcon />}
        disabled={busy}
        onClick={(event) => setAnchor(event.currentTarget)}
      >
        Add role
      </PermissionButton>
      <Menu anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
        {rolesUnavailable && (
          <MenuItem disabled>
            <ListItemText primary="Roles unavailable" secondary={rolesUnavailable} />
          </MenuItem>
        )}
        {!rolesUnavailable && roles === undefined && <MenuItem disabled>Loading roles…</MenuItem>}
        {!rolesUnavailable && roles !== undefined && choices.length === 0 && (
          <MenuItem disabled>The member holds every role</MenuItem>
        )}
        {choices.map((role) => (
          <MenuItem
            key={role.id}
            onClick={() => {
              setAnchor(null);
              onAdd(role);
            }}
          >
            <ListItemText primary={role.name} secondary={role.description ?? undefined} />
          </MenuItem>
        ))}
      </Menu>
    </>
  );
}
