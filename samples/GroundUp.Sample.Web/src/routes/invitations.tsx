import { createFileRoute } from '@tanstack/react-router';
import { Placeholder } from '../components/Placeholder';

export const Route = createFileRoute('/invitations')({
  component: InvitationsPage,
});

function InvitationsPage() {
  return (
    <Placeholder
      title="Invitations"
      story="GU-90"
      description="Send, list, resend and revoke invitations."
    />
  );
}
