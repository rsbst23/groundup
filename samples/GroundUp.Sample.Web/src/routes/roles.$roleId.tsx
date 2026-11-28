import { createFileRoute } from '@tanstack/react-router';
import { listSearchSchema } from '../features/list/listSearch';
import { RoleDetailPage } from '../features/roles/RoleDetailPage';

// Carries the roles list's view, so Back to roles returns to the same page, sort and search
export const Route = createFileRoute('/roles/$roleId')({
  validateSearch: listSearchSchema,
  component: RoleDetailPage,
});
