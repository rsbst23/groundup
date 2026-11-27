import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import { useQueryClient } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useEffect } from 'react';
import { Placeholder } from '../components/Placeholder';
import { onSessionChanged } from '../features/session/sessionQuery';
import { isCallbackLanding, sessionSearchSchema, type SessionSearch } from '../features/session/sessionSearch';

export const Route = createFileRoute('/session')({
  validateSearch: sessionSearchSchema,
  component: SessionPage,
});

function SessionPage() {
  const search = Route.useSearch();
  const queryClient = useQueryClient();
  const landed = isCallbackLanding(search);

  // The callback just set (or kept) the cookie: load the session again
  useEffect(() => {
    if (landed) {
      void onSessionChanged(queryClient);
    }
  }, [landed, queryClient]);

  return (
    <Stack spacing={2}>
      {landed && <CallbackOutcome search={search} />}
      <Placeholder
        title="Session"
        story="GU-89"
        description="Sign up, sign in, the tenant picker, switching and leaving tenants, refresh and sign-out."
      />
    </Stack>
  );
}

/** What the sign-in callback reported when it sent the browser back here (GU-87). */
function CallbackOutcome({ search }: { search: SessionSearch }) {
  if (search.error) {
    return (
      <Alert severity="error">
        The sign-in did not complete: <strong>{search.error}</strong>
        {search.idpError && <> (identity provider: {search.idpError})</>}
      </Alert>
    );
  }

  switch (search.result) {
    case 'signed_in':
      return (
        <Alert severity="success">
          Signed in{search.alreadyMember ? '. You were already a member, so nothing changed.' : '.'}
        </Alert>
      );
    case 'tenant_picker':
      return <Alert severity="info">Signed in. Choose an organization to continue (the picker comes with GU-89).</Alert>;
    case 'no_tenant':
      return <Alert severity="info">Signed in. You do not belong to any organization yet.</Alert>;
    default:
      return null;
  }
}
