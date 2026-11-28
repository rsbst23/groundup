import { createFileRoute } from '@tanstack/react-router';
import { listSearchSchema } from '../features/list/listSearch';
import { RolesPage } from '../features/roles/RolesPage';

// The list's view (page, sort, search) is in the URL (GU-98)
export const Route = createFileRoute('/roles/')({
  validateSearch: listSearchSchema,
  component: RolesPage,
});
