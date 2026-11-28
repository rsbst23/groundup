import { createFileRoute } from '@tanstack/react-router';
import { InvitationsPage } from '../features/invitations/InvitationsPage';
import { listSearchSchema } from '../features/list/listSearch';

// The list's view (page, sort, search, status) is in the URL (GU-98)
export const Route = createFileRoute('/invitations')({
  validateSearch: listSearchSchema,
  component: InvitationsPage,
});