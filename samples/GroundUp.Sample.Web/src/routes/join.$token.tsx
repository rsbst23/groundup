import { createFileRoute } from '@tanstack/react-router';
import { AcceptancePage } from '../features/acceptance/AcceptancePage';
import { acceptanceOutcomeSchema } from '../features/acceptance/outcomeSearch';

/** The page a join link opens (built by the Join links page). */
export const Route = createFileRoute('/join/$token')({
  validateSearch: acceptanceOutcomeSchema,
  component: JoinPage,
});

function JoinPage() {
  const { token } = Route.useParams();
  const search = Route.useSearch();
  return <AcceptancePage kind="joinLink" token={token} search={search} />;
}
