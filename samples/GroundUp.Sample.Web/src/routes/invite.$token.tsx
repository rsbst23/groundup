import { createFileRoute } from '@tanstack/react-router';
import { AcceptancePage } from '../features/acceptance/AcceptancePage';
import { acceptanceOutcomeSchema } from '../features/acceptance/outcomeSearch';

/** The page an invitation email links to (auth.invitations.accept-url). */
export const Route = createFileRoute('/invite/$token')({
  validateSearch: acceptanceOutcomeSchema,
  component: InvitePage,
});

function InvitePage() {
  const { token } = Route.useParams();
  const search = Route.useSearch();
  return <AcceptancePage kind="invitation" token={token} search={search} />;
}
