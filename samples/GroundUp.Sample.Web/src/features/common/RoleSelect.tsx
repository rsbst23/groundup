import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import { useQuery } from '@tanstack/react-query';
import { can, missingPermissionText, PermissionKeys } from '../access/permissions';
import { rolesQueryOptions } from '../members/membersQuery';
import { useSession } from '../session/sessionQuery';

interface RoleSelectProps {
  value: string;
  onChange: (roleId: string) => void;
  disabled?: boolean;
  helperText?: string;
}

/** A required pick of one of the current tenant's roles (GET /api/roles, needs auth.roles.read). */
export function RoleSelect({ value, onChange, disabled, helperText }: RoleSelectProps) {
  const { me } = useSession();
  const canRead = can(me, PermissionKeys.RolesRead);
  const roles = useQuery(rolesQueryOptions(me?.tenantId ?? '', canRead && Boolean(me?.tenantId)));
  const sorted = [...(roles.data ?? [])].sort((a, b) => a.name.localeCompare(b.name));

  const problem = !canRead
    ? missingPermissionText(PermissionKeys.RolesRead)
    : roles.isError
      ? 'The roles could not be loaded.'
      : null;

  return (
    <TextField
      select
      required
      label="Role"
      size="small"
      value={value}
      onChange={(event) => onChange(event.target.value)}
      disabled={disabled || problem !== null || roles.isPending}
      error={problem !== null}
      helperText={problem ?? helperText}
      fullWidth
    >
      {sorted.map((role) => (
        <MenuItem key={role.id} value={role.id}>
          {role.name}
          {role.isDefault ? ' (default for new members)' : ''}
        </MenuItem>
      ))}
    </TextField>
  );
}
