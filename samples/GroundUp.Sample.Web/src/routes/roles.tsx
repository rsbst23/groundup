import { createFileRoute } from '@tanstack/react-router';
import { Placeholder } from '../components/Placeholder';

export const Route = createFileRoute('/roles')({
  component: RolesPage,
});

function RolesPage() {
  return (
    <Placeholder
      title="Roles"
      story="GU-97"
      description="Roles, policies and the permissions they grant."
    />
  );
}
