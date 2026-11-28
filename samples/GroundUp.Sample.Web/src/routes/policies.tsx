import { createFileRoute } from '@tanstack/react-router';
import { listSearchSchema } from '../features/list/listSearch';
import { PoliciesPage } from '../features/policies/PoliciesPage';

// The list's view (page, sort, search) is in the URL (GU-98)
export const Route = createFileRoute('/policies')({
  validateSearch: listSearchSchema,
  component: PoliciesPage,
});
