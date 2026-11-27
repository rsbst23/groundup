import { createFileRoute } from '@tanstack/react-router';
import { Placeholder } from '../components/Placeholder';

export const Route = createFileRoute('/join-links')({
  component: JoinLinksPage,
});

function JoinLinksPage() {
  return (
    <Placeholder
      title="Join links"
      story="GU-90"
      description="Create, list, regenerate and revoke join links."
    />
  );
}
