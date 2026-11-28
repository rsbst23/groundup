import { createFileRoute } from '@tanstack/react-router';
import { RoleDetailPage } from '../features/roles/RoleDetailPage';
import { roleDetailSearchSchema } from '../features/roles/roleSearch';

// Carries the roles list's view (Back to roles returns to it) and the members section's (GU-97d)
export const Route = createFileRoute('/roles/$roleId')({
  validateSearch: roleDetailSearchSchema,
  component: RoleDetailPage,
});
