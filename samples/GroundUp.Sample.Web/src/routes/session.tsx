import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useQueryClient } from '@tanstack/react-query';
import { createFileRoute, useNavigate } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';
import { CallbackOutcome } from '../features/session/components/CallbackOutcome';
import { CreateOrganizationForm } from '../features/session/components/CreateOrganizationForm';
import { OrganizationsPanel } from '../features/session/components/OrganizationsPanel';
import { SessionDetails } from '../features/session/components/SessionDetails';
import { SignedOutPanel } from '../features/session/components/SignedOutPanel';
import { SignOutPanel } from '../features/session/components/SignOutPanel';
import { onSessionChanged, useSession } from '../features/session/sessionQuery';
import { isCallbackLanding, sessionSearchSchema } from '../features/session/sessionSearch';

export const Route = createFileRoute('/session')({
  validateSearch: sessionSearchSchema,
  component: SessionPage,
});

function SessionPage() {
  const search = Route.useSearch();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { me, tenants, currentTenant, isLoading } = useSession();

  // The outcome is kept in state, so the message stays after it is removed from the address
  const [outcome] = useState(search);
  const handled = useRef(false);

  useEffect(() => {
    if (handled.current || !isCallbackLanding(outcome)) {
      return;
    }
    handled.current = true; // React's development double render must not navigate twice

    void (async () => {
      await onSessionChanged(queryClient);
      if (outcome.result === 'signed_in' && outcome.next) {
        await navigate({ href: outcome.next, replace: true });
      } else {
        await navigate({ to: '/session', search: {}, replace: true });
      }
    })();
  }, [outcome, navigate, queryClient]);

  if (isLoading) {
    return <Typography color="text.secondary">Loading the session…</Typography>;
  }

  const choosing = me !== null && me.tenantId === null && tenants.length > 0;
  const organizations = me && (
    <OrganizationsPanel tenants={tenants} currentTenantId={me.tenantId} choosing={choosing} />
  );

  return (
    <Stack spacing={2}>
      {isCallbackLanding(outcome) && <CallbackOutcome outcome={outcome} />}
      {!me ? (
        <SignedOutPanel />
      ) : (
        <>
          {choosing && organizations}
          <SessionDetails me={me} currentTenant={currentTenant} />
          {!choosing && organizations}
          <CreateOrganizationForm />
          <SignOutPanel />
        </>
      )}
    </Stack>
  );
}
