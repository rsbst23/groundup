import { createFileRoute } from '@tanstack/react-router';
import { InvitationsPage } from '../features/invitations/InvitationsPage';

export const Route = createFileRoute('/invitations')({
  component: InvitationsPage,
});
