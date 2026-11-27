import { createFileRoute } from '@tanstack/react-router';
import { Placeholder } from '../components/Placeholder';

export const Route = createFileRoute('/session')({
  component: SessionPage,
});

function SessionPage() {
  return (
    <Placeholder
      title="Session"
      story="GU-89"
      description="Sign up, sign in, the tenant picker, switching and leaving tenants, refresh and sign-out."
    />
  );
}
