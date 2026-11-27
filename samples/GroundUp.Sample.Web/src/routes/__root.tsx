import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import { createRootRouteWithContext, Link, Outlet, type ErrorComponentProps } from '@tanstack/react-router';
import { lazy, Suspense } from 'react';
import { AppLayout } from '../components/AppLayout';
import { ErrorPanel } from '../components/ErrorPanel';
import { sessionQueryOptions } from '../features/session/sessionQuery';
import type { RouterContext } from '../router';

// Devtools only in development, loaded lazily so the production build does not include them.
const Devtools = import.meta.env.DEV
  ? lazy(async () => {
      const [{ TanStackRouterDevtools }, { ReactQueryDevtools }] = await Promise.all([
        import('@tanstack/react-router-devtools'),
        import('@tanstack/react-query-devtools'),
      ]);
      return {
        default: () => (
          <>
            <TanStackRouterDevtools position="bottom-right" />
            <ReactQueryDevtools buttonPosition="bottom-left" />
          </>
        ),
      };
    })
  : () => null;

export const Route = createRootRouteWithContext<RouterContext>()({
  // Every page renders with the session known (signed out is null, not an error).
  loader: ({ context }) => context.queryClient.ensureQueryData(sessionQueryOptions),
  component: RootLayout,
  notFoundComponent: NotFound,
  errorComponent: ApiUnavailable,
});

/** Shown when the session cannot be loaded at all, usually because the sample API is not running. */
function ApiUnavailable({ error }: ErrorComponentProps) {
  return (
    <Box sx={{ p: 4, maxWidth: 800 }}>
      <Typography variant="h5" gutterBottom>
        The sample API is not reachable
      </Typography>
      <Typography gutterBottom>
        Start the sample API (F5, or <code>dotnet run --project samples/GroundUp.Sample</code>) on port 5000, then reload
        this page.
      </Typography>
      <Box sx={{ my: 2 }}>
        <ErrorPanel error={error} title="Loading the session failed" />
      </Box>
      <Button variant="contained" onClick={() => window.location.reload()}>
        Reload
      </Button>
    </Box>
  );
}

function RootLayout() {
  return (
    <AppLayout>
      <Outlet />
      <Suspense fallback={null}>
        <Devtools />
      </Suspense>
    </AppLayout>
  );
}

function NotFound() {
  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="h5" gutterBottom>
        Page not found
      </Typography>
      <Typography>
        There is no page at this address. <Link to="/">Go to Home</Link>.
      </Typography>
    </Paper>
  );
}
