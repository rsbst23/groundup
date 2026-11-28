import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import { useParams, useSearch } from '@tanstack/react-router';
import { RouterLink } from '../../components/RouterLink';

/** One role: its policies and permissions (GU-97, task 3). */
export function RoleDetailPage() {
  const { roleId } = useParams({ from: '/roles/$roleId' });
  const search = useSearch({ from: '/roles/$roleId' });
  return (
    <Paper sx={{ p: 3 }}>
      <RouterLink to="/roles" search={search}>
        ‹ Roles
      </RouterLink>
      <Typography variant="h5" sx={{ mt: 1 }}>
        Role {roleId}
      </Typography>
    </Paper>
  );
}
