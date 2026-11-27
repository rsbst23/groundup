import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import { createRootRouteWithContext, Link, Outlet } from '@tanstack/react-router';
import { lazy, Suspense } from 'react';
import { AppLayout } from '../components/AppLayout';
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
  component: RootLayout,
  notFoundComponent: NotFound,
});

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
