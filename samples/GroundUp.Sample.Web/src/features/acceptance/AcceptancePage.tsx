import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';
import { acceptInvitation, joinWithLink, lookupInvitation, lookupJoinLink } from '../../api/acceptance';
import { ErrorPanel } from '../../components/ErrorPanel';
import { outcomeMessage } from '../session/outcomeMessages';
import { onSessionChanged, useSession } from '../session/sessionQuery';
import {
  acceptanceReturnUrl,
  hasOutcome,
  lookupReasonText,
  type AcceptanceKind,
  type AcceptanceOutcome,
} from './outcomeSearch';

interface AcceptancePageProps {
  kind: AcceptanceKind;
  token: string;
  /** The callback's outcome from the address, when the sign-in has just come back here. */
  search: AcceptanceOutcome;
}

const text = {
  invitation: { title: 'Invitation', action: 'Accept invitation', noun: 'invitation' },
  joinLink: { title: 'Join link', action: 'Join', noun: 'link' },
} as const;

/**
 * The page an invitation email or a join link opens (no sign-in needed). It shows what the link is
 * for, starts the sign-in that accepts it, and shows the outcome when the sign-in comes back here.
 */
export function AcceptancePage({ kind, token, search }: AcceptancePageProps) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  // Kept in state, so the message stays after the outcome is removed from the address
  const [outcome] = useState(search);
  const handled = useRef(false);

  useEffect(() => {
    if (handled.current || !hasOutcome(outcome)) {
      return;
    }
    handled.current = true; // React's development double effect must not refresh twice
    void (async () => {
      await onSessionChanged(queryClient);
      await navigate({ to: '.', search: {}, replace: true });
    })();
  }, [outcome, navigate, queryClient]);

  return (
    <Paper sx={{ p: 3, maxWidth: 720 }}>
      <Typography variant="h5" gutterBottom>
        {text[kind].title}
      </Typography>
      {hasOutcome(outcome) ? (
        <OutcomePanel kind={kind} token={token} outcome={outcome} />
      ) : (
        <LookupPanel kind={kind} token={token} />
      )}
    </Paper>
  );
}

/** Starts the sign-in that accepts the link, then sends the whole page to Keycloak. */
function useStart(kind: AcceptanceKind, token: string) {
  return useMutation({
    mutationFn: async (forceLogin: boolean) => {
      const options = { forceLogin, returnUrl: acceptanceReturnUrl(kind, token) };
      const { redirectUrl } = kind === 'invitation' ? await acceptInvitation(token, options) : await joinWithLink(token, options);
      window.location.assign(redirectUrl);
      // The page is leaving; keep the buttons busy until it does
      await new Promise<never>(() => undefined);
    },
  });
}

function LookupPanel({ kind, token }: { kind: AcceptanceKind; token: string }) {
  const lookup = useQuery({
    queryKey: ['acceptance', kind, token] as const,
    queryFn: ({ signal }) => (kind === 'invitation' ? lookupInvitation(token, signal) : lookupJoinLink(token, signal)),
  });
  const start = useStart(kind, token);
  const { me } = useSession();

  if (lookup.isPending) {
    return <Typography color="text.secondary">Checking the {text[kind].noun}…</Typography>;
  }
  if (lookup.isError) {
    return <ErrorPanel error={lookup.error} title={`Could not check the ${text[kind].noun}`} />;
  }
  if (!lookup.data.usable) {
    return <Alert severity="warning">{lookupReasonText(lookup.data.reason)}</Alert>;
  }

  return (
    <Stack spacing={2}>
      <Typography>
        Join <strong>{lookup.data.organizationName}</strong> as <strong>{lookup.data.roleName}</strong>.
      </Typography>
      <Typography variant="body2" color="text.secondary">
        {me
          ? `You are signed in as ${me.email ?? me.userId}. Continue with this account, or sign in as someone else.`
          : kind === 'invitation'
            ? 'You sign in, or register if you have no account yet, with the email the invitation was sent to.'
            : 'You sign in, or register if you have no account yet.'}
      </Typography>
      <Stack direction="row" spacing={2}>
        <Button variant="contained" loading={start.isPending && start.variables === false} disabled={start.isPending} onClick={() => start.mutate(false)}>
          {text[kind].action}
        </Button>
        <Button variant="outlined" loading={start.isPending && start.variables === true} disabled={start.isPending} onClick={() => start.mutate(true)}>
          Sign in as someone else
        </Button>
      </Stack>
      {start.isError && <ErrorPanel error={start.error} title="Could not start the sign-in" />}
    </Stack>
  );
}

function OutcomePanel({ kind, token, outcome }: { kind: AcceptanceKind; token: string; outcome: AcceptanceOutcome }) {
  const { currentTenant } = useSession();
  const start = useStart(kind, token);
  const organization = currentTenant?.name ?? 'the organization';

  if (outcome.error) {
    const retry = outcome.error === 'INVITATION_EMAIL_MISMATCH' || outcome.error === 'REAUTH_REQUIRED';
    return (
      <Stack spacing={2}>
        <Alert
          severity="error"
          action={
            retry ? (
              <Button color="inherit" size="small" loading={start.isPending} onClick={() => start.mutate(true)}>
                Sign in as someone else
              </Button>
            ) : undefined
          }
        >
          <AlertTitle>The {text[kind].noun} was not accepted: {outcome.error}</AlertTitle>
          {outcomeMessage(outcome.error)}
          {outcome.idpError && <> Keycloak's code: {outcome.idpError}.</>}
        </Alert>
        {start.isError && <ErrorPanel error={start.error} title="Could not start the sign-in" />}
        <Links />
      </Stack>
    );
  }

  switch (outcome.result) {
    case 'signed_in':
      return (
        <Stack spacing={2}>
          <Alert severity="success">
            {outcome.alreadyMember ? `You are already a member of ${organization}. Nothing changed.` : `You joined ${organization}.`}
          </Alert>
          <Links />
        </Stack>
      );
    case 'tenant_picker':
      return (
        <Stack spacing={2}>
          <Alert severity="info">Signed in. Choose an organization on the Session page to continue.</Alert>
          <Links />
        </Stack>
      );
    default:
      return (
        <Stack spacing={2}>
          <Alert severity="info">Signed in, but you do not belong to any organization yet.</Alert>
          <Links />
        </Stack>
      );
  }
}

function Links() {
  return (
    <Stack direction="row" spacing={2}>
      <Button component={Link} to="/session" variant="outlined">
        Session
      </Button>
      <Button component={Link} to="/members" variant="outlined">
        Members
      </Button>
    </Stack>
  );
}
