import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Paper from '@mui/material/Paper';
import type { ReactNode } from 'react';
import { useSession } from '../session/sessionQuery';
import { can, permissionRequiredText } from './permissions';

interface RequirePermissionProps {
  /** The key the page's list endpoint needs, for example `PermissionKeys.MembersRead`. */
  permission: string;
  /** What the user would do on the page, for the message: "see this page". */
  purpose?: string;
  children: ReactNode;
}

/**
 * Shows the page only when the session holds the permission its data needs; otherwise says which
 * permission is missing, without calling the API (GU-98). Put it inside `RequireTenant`: permissions
 * belong to the current tenant. The API still checks every request; a 403 that arrives anyway is
 * shown by `ErrorPanel` with the missing permission.
 */
export function RequirePermission({ permission, purpose = 'see this page', children }: RequirePermissionProps) {
  const { me } = useSession();
  if (can(me, permission)) {
    return <>{children}</>;
  }
  return (
    <Paper sx={{ p: 3 }}>
      <Alert severity="info">
        <AlertTitle>Permission needed</AlertTitle>
        {permissionRequiredText([permission], purpose)}
      </Alert>
    </Paper>
  );
}
