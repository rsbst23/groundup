import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Button from '@mui/material/Button';
import { ErrorPanel } from '../../../components/ErrorPanel';
import { outcomeMessage } from '../outcomeMessages';
import type { SessionSearch } from '../sessionSearch';
import { useStartSignIn } from '../useStartSignIn';

interface CallbackOutcomeProps {
  outcome: SessionSearch;
}

/** What the sign-in callback reported when it sent the browser back to the Session page. */
export function CallbackOutcome({ outcome }: CallbackOutcomeProps) {
  const startSignIn = useStartSignIn();

  if (outcome.error) {
    const forceLogin = outcome.error === 'REAUTH_REQUIRED';
    return (
      <>
        <Alert
          severity="error"
          action={
            <Button
              color="inherit"
              size="small"
              loading={startSignIn.isPending}
              onClick={() => startSignIn.mutate({ kind: 'login', forceLogin })}
            >
              Sign in again
            </Button>
          }
        >
          <AlertTitle>The sign-in did not complete: {outcome.error}</AlertTitle>
          {outcomeMessage(outcome.error)}
          {outcome.idpError && <> Keycloak's code: {outcome.idpError}.</>}
        </Alert>
        {startSignIn.isError && <ErrorPanel error={startSignIn.error} title="Could not start the sign-in" />}
      </>
    );
  }

  switch (outcome.result) {
    case 'signed_in':
      return (
        <Alert severity="success">
          Signed in.
          {outcome.alreadyMember && ' You were already a member, so nothing changed.'}
          {outcome.next && ` Returning to ${outcome.next}…`}
        </Alert>
      );
    case 'tenant_picker':
      return <Alert severity="info">Signed in. Choose an organization below to continue.</Alert>;
    case 'no_tenant':
      return (
        <Alert severity="info">Signed in. You do not belong to any organization yet; create one below.</Alert>
      );
    default:
      return null;
  }
}
