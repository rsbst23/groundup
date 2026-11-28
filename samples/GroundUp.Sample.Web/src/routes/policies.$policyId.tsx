import { createFileRoute } from '@tanstack/react-router';
import { PolicyEditorPage } from '../features/policies/PolicyEditorPage';
import { policyEditorSearchSchema } from '../features/policies/policySearch';

// Carries the policies list's view (Back to policies returns to it) and the catalogue's (GU-97d)
export const Route = createFileRoute('/policies/$policyId')({
  validateSearch: policyEditorSearchSchema,
  component: PolicyEditorPage,
});
