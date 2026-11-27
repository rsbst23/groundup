import { createFileRoute } from '@tanstack/react-router';
import { Placeholder } from '../components/Placeholder';

export const Route = createFileRoute('/members')({
  component: MembersPage,
});

function MembersPage() {
  return (
    <Placeholder
      title="Members"
      story="GU-90"
      description="List members, remove a member, assign and remove roles."
    />
  );
}
